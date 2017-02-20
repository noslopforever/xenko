﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;
using System.Linq;
using SiliconStudio.Assets.Analysis;
using SiliconStudio.Assets.Yaml;
using SiliconStudio.Core;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Core.Diagnostics;
using SiliconStudio.Core.Reflection;
using SiliconStudio.Core.Serialization;
using SiliconStudio.Core.Yaml;
using SiliconStudio.Quantum;
using SiliconStudio.Quantum.Contents;

namespace SiliconStudio.Assets.Quantum
{
    [AssetPropertyGraph(typeof(Asset))]
    // ReSharper disable once RequiredBaseTypesIsNotInherited - due to a limitation on how ReSharper checks this requirement (see https://youtrack.jetbrains.com/issue/RSRP-462598)
    public class AssetPropertyGraph : IDisposable
    {
        public struct NodeOverride
        {
            public NodeOverride(IAssetNode overriddenNode, Index overriddenIndex, OverrideTarget target)
            {
                Node = overriddenNode;
                Index = overriddenIndex;
                Target = target;
            }
            public readonly IAssetNode Node;
            public readonly Index Index;
            public readonly OverrideTarget Target;
        }

        private struct NodeChangeHandlers
        {
            public readonly EventHandler<MemberNodeChangeEventArgs> ValueChange;
            public readonly EventHandler<ItemChangeEventArgs> ItemChange;
            public NodeChangeHandlers(EventHandler<MemberNodeChangeEventArgs> valueChange, EventHandler<ItemChangeEventArgs> itemChange)
            {
                ValueChange = valueChange;
                ItemChange = itemChange;
            }
        }

        private readonly Dictionary<IGraphNode, OverrideType> previousOverrides = new Dictionary<IGraphNode, OverrideType>();
        private readonly Dictionary<IGraphNode, ItemId> removedItemIds = new Dictionary<IGraphNode, ItemId>();

        protected readonly Asset Asset;
        private readonly AssetToBaseNodeLinker baseLinker;
        private readonly GraphNodeChangeListener nodeListener;
        private AssetPropertyGraph baseGraph;
        private readonly Dictionary<IAssetNode, NodeChangeHandlers> baseLinkedNodes = new Dictionary<IAssetNode, NodeChangeHandlers>();
        private IBaseToDerivedRegistry baseToDerivedRegistry;

        public AssetPropertyGraph(AssetPropertyGraphContainer container, AssetItem assetItem, ILogger logger)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (assetItem == null) throw new ArgumentNullException(nameof(assetItem));
            Container = container;
            AssetCollectionItemIdHelper.GenerateMissingItemIds(assetItem.Asset);
            CollectionItemIdsAnalysis.FixupItemIds(assetItem, logger);
            Asset = assetItem.Asset;
            RootNode = (AssetObjectNode)Container.NodeContainer.GetOrCreateNode(assetItem.Asset);
            var overrides = assetItem.YamlMetadata?.RetrieveMetadata(AssetObjectSerializerBackend.OverrideDictionaryKey);
            ApplyOverrides(RootNode, overrides);
            var objectReferences = assetItem.YamlMetadata?.RetrieveMetadata(AssetObjectSerializerBackend.ObjectReferencesKey);
            ApplyObjectReferences(RootNode, objectReferences);
            nodeListener = new AssetGraphNodeChangeListener(RootNode, this);
            nodeListener.Changing += AssetContentChanging;
            nodeListener.Changed += AssetContentChanged;
            nodeListener.ItemChanging += AssetItemChanging;
            nodeListener.ItemChanged += AssetItemChanged;

            baseLinker = new AssetToBaseNodeLinker(this) { LinkAction = LinkBaseNode };
        }

        public void Dispose()
        {
            nodeListener.Changing -= AssetContentChanging;
            nodeListener.Changed -= AssetContentChanged;
            nodeListener.ItemChanging -= AssetItemChanging;
            nodeListener.ItemChanged -= AssetItemChanged;
            nodeListener.Dispose();
        }

        public IAssetObjectNode RootNode { get; }

        public AssetPropertyGraphContainer Container { get; }

        /// <summary>
        /// Gets or sets whether a property is currently being updated from a change in the base of this asset.
        /// </summary>
        public bool UpdatingPropertyFromBase { get; private set; }

        /// <summary>
        /// Raised after one of the node referenced by the related root node has changed.
        /// </summary>
        public event EventHandler<MemberNodeChangeEventArgs> Changing { add { nodeListener.Changing += value; } remove { nodeListener.Changing -= value; } }

        /// <summary>
        /// Raised after one of the node referenced by the related root node has changed.
        /// </summary>
        /// <remarks>In addition to the usual <see cref="MemberNodeChangeEventArgs"/> generated by <see cref="IGraphNode"/> this event also gives information about override changes.</remarks>
        /// <seealso cref="AssetMemberNodeChangeEventArgs"/>.
        public event EventHandler<AssetMemberNodeChangeEventArgs> Changed;

        public event EventHandler<ItemChangeEventArgs> ItemChanging { add { nodeListener.ItemChanging += value; } remove { nodeListener.ItemChanging -= value; } }

        public event EventHandler<AssetItemNodeChangeEventArgs> ItemChanged;

        /// <summary>
        /// Raised when a base content has changed, after updating the related content of this graph.
        /// </summary>
        public Action<INodeChangeEventArgs, IGraphNode> BaseContentChanged;

        private IBaseToDerivedRegistry BaseToDerivedRegistry => baseToDerivedRegistry ?? (baseToDerivedRegistry = CreateBaseToDerivedRegistry());

        public void RefreshBase(AssetPropertyGraph baseAssetGraph)
        {
            // Unlink previously linked nodes
            ClearAllBaseLinks();

            baseGraph = baseAssetGraph;

            // Link nodes to the new base.
            // Note: in case of composition (prefabs, etc.), even if baseAssetGraph is null, each part (entities, etc.) will discover
            // its own base by itself via the FindTarget method.
            LinkToBase(RootNode, baseAssetGraph?.RootNode);
        }

        public void ReconcileWithBase()
        {
            ReconcileWithBase(RootNode);
        }

        public void ReconcileWithBase(IAssetNode rootNode)
        {
            var visitor = CreateReconcilierVisitor();
            visitor.Visiting += (node, path) => ReconcileWithBaseNode((IAssetNode)node);
            visitor.Visit(rootNode);
        }

        /// <summary>
        /// Resets the overrides attached to the given node and its descendants, recursively.
        /// </summary>
        /// <param name="rootNode">The node for which to reset overrides.</param>
        /// <param name="indexToReset">The index of the override to reset in this node, if relevant.</param>
        public void ResetOverride(IAssetNode rootNode, Index indexToReset)
        {
            var visitor = CreateReconcilierVisitor();
            visitor.SkipRootNode = true;
            visitor.Visiting += (node, path) =>
            {
                var memberNode = node as AssetMemberNode;
                memberNode?.OverrideContent(false);

                var objectNode = node as AssetObjectNode;
                if (objectNode != null)
                {
                    foreach (var overrideItem in objectNode.GetOverriddenItemIndices())
                    {
                        objectNode.OverrideItem(false, overrideItem);
                    }
                    foreach (var overrideKey in objectNode.GetOverriddenKeyIndices())
                    {
                        objectNode.OverrideKey(false, overrideKey);
                    }
                }
            };
            visitor.Visit(rootNode);

            ReconcileWithBase(rootNode);
        }

        public virtual bool IsObjectReference(IGraphNode targetNode, Index index, object value)
        {
            return false;
        }

        /// <summary>
        /// Creates an instance of <see cref="GraphVisitorBase"/> that is suited to reconcile properties with the base.
        /// </summary>
        /// <returns>A new instance of <see cref="GraphVisitorBase"/> for reconciliation.</returns>
        public virtual GraphVisitorBase CreateReconcilierVisitor()
        {
            return new GraphVisitorBase();
        }

        public virtual IGraphNode FindTarget(IGraphNode sourceNode, IGraphNode target)
        {
            return target;
        }

        public void PrepareForSave(ILogger logger, AssetItem assetItem)
        {
            if (assetItem.Asset != Asset) throw new ArgumentException($@"The given {nameof(AssetItem)} does not match the asset associated with this instance", nameof(assetItem));
            AssetCollectionItemIdHelper.GenerateMissingItemIds(assetItem.Asset);
            CollectionItemIdsAnalysis.FixupItemIds(assetItem, logger);
            assetItem.YamlMetadata.AttachMetadata(AssetObjectSerializerBackend.OverrideDictionaryKey, GenerateOverridesForSerialization(RootNode));
            assetItem.YamlMetadata.AttachMetadata(AssetObjectSerializerBackend.ObjectReferencesKey, GenerateObjectReferencesForSerialization(RootNode));
        }

        [CanBeNull]
        private static IAssetNode ResolveObjectPath([NotNull] IAssetNode rootNode, [NotNull] YamlAssetPath path, out Index index)
        {
            bool unused;
            return ResolveObjectPath(rootNode, path, out index, out unused);
        }

        [CanBeNull]
        private static IAssetNode ResolveObjectPath([NotNull] IAssetNode rootNode, [NotNull] YamlAssetPath path, out Index index, out bool resolveOnIndex)
        {
            var currentNode = rootNode;
            index = Index.Empty;
            resolveOnIndex = false;
            for (var i = 0; i < path.Items.Count; i++)
            {
                var item = path.Items[i];
                switch (item.Type)
                {
                    case YamlAssetPath.ItemType.Member:
                    {
                        index = Index.Empty;
                        resolveOnIndex = false;
                        if (currentNode.IsReference)
                        {
                            var memberNode = currentNode as IMemberNode;
                            if (memberNode == null) throw new InvalidOperationException($"An IMemberNode was expected when processing the path [{path}]");
                            currentNode = (IAssetNode)memberNode.Target;
                        }
                        var objectNode = currentNode as IObjectNode;
                        if (objectNode == null) throw new InvalidOperationException($"An IObjectNode was expected when processing the path [{path}]");
                        var name = item.AsMember();
                        currentNode = (IAssetNode)objectNode.TryGetChild(name);
                        break;
                    }
                    case YamlAssetPath.ItemType.Index:
                    {
                        index = new Index(item.Value);
                        resolveOnIndex = true;
                        var memberNode = currentNode as IMemberNode;
                        if (memberNode == null) throw new InvalidOperationException($"An IMemberNode was expected when processing the path [{path}]");
                        currentNode = (IAssetNode)memberNode.Target;
                        if (currentNode.IsReference && i < path.Items.Count - 1)
                        {
                            var objNode = currentNode as IObjectNode;
                            if (objNode == null) throw new InvalidOperationException($"An IObjectNode was expected when processing the path [{path}]");
                            currentNode = (IAssetNode)objNode.IndexedTarget(new Index(item.Value));
                        }
                        break;
                    }
                    case YamlAssetPath.ItemType.ItemId:
                    {
                        var ids = CollectionItemIdHelper.GetCollectionItemIds(currentNode.Retrieve());
                        var key = ids.GetKey(item.AsItemId());
                        index = new Index(key);
                        resolveOnIndex = false;
                        var memberNode = currentNode as IMemberNode;
                        if (memberNode == null) throw new InvalidOperationException($"An IMemberNode was expected when processing the path [{path}]");
                        currentNode = (IAssetNode)memberNode.Target;
                        if (currentNode.IsReference && i < path.Items.Count - 1)
                        {
                            var objNode = currentNode as IObjectNode;
                            if (objNode == null) throw new InvalidOperationException($"An IObjectNode was expected when processing the path [{path}]");
                            currentNode = (IAssetNode)objNode.IndexedTarget(new Index(key));
                        }
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                // Something wrong happen, the node is unreachable.
                if (currentNode == null)
                    return null;
            }

            return currentNode;
        }

        public static YamlAssetMetadata<OverrideType> GenerateOverridesForSerialization(IGraphNode rootNode)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            var visitor = new OverrideTypePathGenerator();
            visitor.Visit(rootNode);
            return visitor.Result;
        }

        public YamlAssetMetadata<Guid> GenerateObjectReferencesForSerialization(IGraphNode rootNode)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            var visitor = new ObjectReferencePathGenerator(this);
            visitor.Visit(rootNode);
            return visitor.Result;
        }

        public static void ApplyOverrides(IAssetNode rootNode, YamlAssetMetadata<OverrideType> overrides)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            if (overrides == null)
                return;

            foreach (var overrideInfo in overrides)
            {
                Index index;
                bool overrideOnKey;
                var node = ResolveObjectPath(rootNode, overrideInfo.Key, out index, out overrideOnKey);
                // The node is unreachable, skip this override.
                if (node == null)
                    continue;

                var memberNode = node as AssetMemberNode;
                memberNode?.SetContentOverride(overrideInfo.Value);

                var objectNode = node as IAssetObjectNode;
                if (objectNode != null)
                {
                    if (!overrideOnKey)
                    {
                        objectNode.OverrideItem(true, index);
                    }
                    else
                    {
                        objectNode.OverrideKey(true, index);
                    }
                }
            }
        }

        private void ApplyObjectReferences(IAssetObjectNode rootNode, YamlAssetMetadata<Guid> objectReferences)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            if (objectReferences == null)
                return;

            foreach (var objectReference in objectReferences)
            {
                Index index;
                var node = ResolveObjectPath(rootNode, objectReference.Key, out index);
                // The node is unreachable, skip this override.
                if (node == null)
                    continue;

                var memberNode = node as AssetMemberNode;
                if (memberNode != null)
                    memberNode.IsObjectReference = true;

                var objectNode = node as IAssetObjectNodeInternal;
                objectNode?.SetObjectReference(index, true);
            }
        }

        public List<NodeOverride> ClearAllOverrides()
        {
            // Unregister handlers - must be done first!
            ClearAllBaseLinks();

            var clearedOverrides = new List<NodeOverride>();
            // Clear override and base from node
            if (RootNode != null)
            {
                var visitor = new GraphVisitorBase { SkipRootNode = true };
                visitor.Visiting += (node, path) =>
                {
                    var memberNode = node as AssetMemberNode;
                    var objectNode = node as AssetObjectNode;

                    if (memberNode != null && memberNode.IsContentOverridden())
                    {
                        memberNode.OverrideContent(false);
                        clearedOverrides.Add(new NodeOverride(memberNode, Index.Empty, OverrideTarget.Content));
                    }
                    if (objectNode != null)
                    {
                        foreach (var index in objectNode.GetOverriddenItemIndices())
                        {
                            objectNode.OverrideItem(false, index);
                            clearedOverrides.Add(new NodeOverride(objectNode, index, OverrideTarget.Item));
                        }
                        foreach (var index in objectNode.GetOverriddenKeyIndices())
                        {
                            objectNode.OverrideKey(false, index);
                            clearedOverrides.Add(new NodeOverride(objectNode, index, OverrideTarget.Key));
                        }
                    }
                };
                visitor.Visit(RootNode);
            }

            return clearedOverrides;
        }

        public void RestoreOverrides(List<NodeOverride> overridesToRestore, AssetPropertyGraph archetypeBase)
        {
            foreach (var clearedOverride in overridesToRestore)
            {
                // TODO: this will need improvement when adding support for Seal
                switch (clearedOverride.Target)
                {
                    case OverrideTarget.Content:
                        ((AssetMemberNode)clearedOverride.Node).OverrideContent(true);
                        break;
                    case OverrideTarget.Item:
                        ((AssetObjectNode)clearedOverride.Node).OverrideItem(true, clearedOverride.Index);
                        break;
                    case OverrideTarget.Key:
                        ((AssetObjectNode)clearedOverride.Node).OverrideKey(true, clearedOverride.Index);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }


        // TODO: turn private
        public void LinkToBase(IAssetNode sourceRootNode, IAssetNode targetRootNode)
        {
            baseLinker.LinkGraph(sourceRootNode, targetRootNode);
        }

        [NotNull]
        protected virtual IBaseToDerivedRegistry CreateBaseToDerivedRegistry()
        {
            return new AssetBaseToDerivedRegistry(this);
        }

        // TODO: this method is should be called in every scenario of ReconcileWithBase, it is not the case yet.
        protected virtual bool CanUpdate(IAssetNode node, ContentChangeType changeType, Index index, object value)
        {
            return true;
        }

        protected internal virtual object CloneValueFromBase(object value, IAssetNode node)
        {
            return CloneFromBase(value);
        }

        /// <summary>
        /// Clones the given object, remove any override information on it, and propagate its id (from <see cref="IdentifiableHelper"/>) to the cloned object.
        /// </summary>
        /// <param name="value">The object to clone.</param>
        /// <returns>A clone of the given object.</returns>
        /// <remarks>If the given object is null, this method returns null.</remarks>
        /// <remarks>If the given object is a content reference, the given object won't be cloned but directly returned.</remarks>
        private static object CloneFromBase(object value)
        {
            if (value == null)
                return null;

            // TODO: check if the cloner is aware of the content type (attached reference) and does not already avoid cloning them.

            // TODO FIXME
            //if (SessionViewModel.Instance.ContentReferenceService.IsContentType(value.GetType()))
            //    return value;

            Dictionary<Guid, Guid> remapping;
            var result = AssetCloner.Clone<object>(value, AssetClonerFlags.GenerateNewIdsForIdentifiableObjects, out remapping);
            return result;
        }

        private void LinkBaseNode(IGraphNode currentNode, IGraphNode baseNode)
        {
            var assetNode = (IAssetNode)currentNode;
            ((IAssetNodeInternal)assetNode).SetPropertyGraph(this);
            ((IAssetNodeInternal)assetNode).SetBaseContent(baseNode);

            BaseToDerivedRegistry.RegisterBaseToDerived((IAssetNode)baseNode, (IAssetNode)currentNode);

            if (!baseLinkedNodes.ContainsKey(assetNode))
            {
                EventHandler<MemberNodeChangeEventArgs> valueChange = null;
                EventHandler<ItemChangeEventArgs> itemChange = null;
                if (baseNode != null)
                {
                    var member = assetNode.BaseNode as IMemberNode;
                    if (member != null)
                    {
                        valueChange = (s, e) => OnBaseContentChanged(e, currentNode);
                        member.Changed += valueChange;
                    }
                    var objectNode = assetNode.BaseNode as IObjectNode;
                    if (objectNode != null)
                    {
                        itemChange = (s, e) => OnBaseContentChanged(e, currentNode);
                        objectNode.ItemChanged += itemChange;
                    }
                }
                baseLinkedNodes.Add(assetNode, new NodeChangeHandlers(valueChange, itemChange));
            }
        }

        private void ClearAllBaseLinks()
        {
            foreach (var linkedNode in baseLinkedNodes)
            {
                var member = linkedNode.Key.BaseNode as IMemberNode;
                if (member != null)
                {
                    member.Changed -= linkedNode.Value.ValueChange;
                }
                var objectNode = linkedNode.Key.BaseNode as IObjectNode;
                if (objectNode != null)
                {
                    objectNode.ItemChanged -= linkedNode.Value.ItemChange;
                }

            }
            baseLinkedNodes.Clear();
        }

        private void AssetContentChanging(object sender, MemberNodeChangeEventArgs e)
        {
            var node = (AssetMemberNode)e.Member;
            var overrideValue = node.GetContentOverride();
            previousOverrides[e.Member] = overrideValue;
        }

        private void AssetContentChanged(object sender, MemberNodeChangeEventArgs e)
        {
            var previousOverride = previousOverrides[e.Member];
            previousOverrides.Remove(e.Member);
            var node = (AssetMemberNode)e.Member;
            var overrideValue = node.GetContentOverride();
            node.IsObjectReference = IsObjectReference(e.Member, Index.Empty, e.NewValue);
            Changed?.Invoke(sender, new AssetMemberNodeChangeEventArgs(e, previousOverride, overrideValue, ItemId.Empty));
        }

        private void AssetItemChanging(object sender, ItemChangeEventArgs e)
        {
            var overrideValue = OverrideType.Base;
            var node = (AssetObjectNode)e.Node;
            var collection = node.Retrieve();
            // For value change and remove, we store the current override state.
            if (CollectionItemIdHelper.HasCollectionItemIds(collection))
            {
                overrideValue = node.GetItemOverride(e.Index);
                if (e.ChangeType == ContentChangeType.CollectionRemove)
                {
                    // For remove, we also collect the id of the item that will be removed, so we can pass it to the Changed event.
                    var ids = CollectionItemIdHelper.GetCollectionItemIds(collection);
                    ItemId itemId;
                    ids.TryGet(e.Index.Value, out itemId);
                    removedItemIds[e.Node] = itemId;
                }
            }
            previousOverrides[e.Node] = overrideValue;
        }

        private void AssetItemChanged(object sender, ItemChangeEventArgs e)
        {
            var previousOverride = previousOverrides[e.Node];
            previousOverrides.Remove(e.Node);

            var itemId = ItemId.Empty;
            var overrideValue = OverrideType.Base;
            var node = (IAssetObjectNodeInternal)e.Node;
            var collection = node.Retrieve();
            if (e.ChangeType == ContentChangeType.CollectionUpdate || e.ChangeType == ContentChangeType.CollectionAdd)
            {
                // We're changing an item of a collection. If the collection has identifiable items, retrieve the override status of the item.
                if (CollectionItemIdHelper.HasCollectionItemIds(collection))
                {
                    overrideValue = node.GetItemOverride(e.Index);

                    // Also retrieve the id of the modified item (this should fail only if the collection doesn't have identifiable items)
                    var ids = CollectionItemIdHelper.GetCollectionItemIds(collection);
                    ids.TryGet(e.Index.Value, out itemId);
                }
                node.SetObjectReference(e.Index, IsObjectReference(e.Node, e.Index, e.NewValue));

            }
            else if (e.ChangeType == ContentChangeType.CollectionRemove)
            {
                // When deleting we are always overriding (unless there is no base or non-identifiable items)
                if (CollectionItemIdHelper.HasCollectionItemIds(collection))
                {
                    overrideValue = node.BaseNode != null && !UpdatingPropertyFromBase ? OverrideType.New : OverrideType.Base;
                    itemId = removedItemIds[e.Node];
                    removedItemIds.Remove(e.Node);
                }
            }

            ItemChanged?.Invoke(sender, new AssetItemNodeChangeEventArgs(e, previousOverride, overrideValue, itemId));
        }

        private void OnBaseContentChanged(INodeChangeEventArgs e, IGraphNode node)
        {
            // Ignore base change if propagation is disabled.
            if (!Container.PropagateChangesFromBase)
                return;

            UpdatingPropertyFromBase = true;
            // TODO: we want to refresh the base only starting from the modified node!
            RefreshBase(baseGraph);
            var rootNode = (IAssetNode)node;
            var visitor = CreateReconcilierVisitor();
            visitor.Visiting += (assetNode, path) => ReconcileWithBaseNode((IAssetNode)assetNode);
            visitor.Visit(rootNode);
            UpdatingPropertyFromBase = false;

            BaseContentChanged?.Invoke(e, node);
        }

        private void ReconcileWithBaseNode(IAssetNode assetNode)
        {
            var memberNode = assetNode as AssetMemberNode;
            var objectNode = assetNode as IAssetObjectNodeInternal;
            // Non-overridable members should not be reconcilied.
            if (assetNode?.BaseNode == null || !memberNode?.CanOverride == true)
                return;

            var localValue = assetNode.Retrieve();
            var baseValue = assetNode.BaseNode.Retrieve();

            // Reconcile occurs only when the node is not overridden.
            if (memberNode != null)
            {
                if (!memberNode.IsContentOverridden())
                {
                    memberNode.ResettingOverride = true;
                    if (ShouldReconcileMember(memberNode))
                    {
                        object clonedValue;
                        // Object references
                        if (baseValue is IIdentifiable && IsObjectReference(memberNode.BaseNode, Index.Empty, baseValue))
                            clonedValue = BaseToDerivedRegistry.ResolveFromBase(baseValue, memberNode);
                        else
                            clonedValue = CloneValueFromBase(baseValue, assetNode);
                        memberNode.Update(clonedValue);
                    }
                    memberNode.ResettingOverride = false;
                }
            }
            if (objectNode != null)
            {
                var baseNode = (IAssetObjectNodeInternal)assetNode.BaseNode;
                objectNode.ResettingOverride = true;
                // Handle collection and dictionary cases
                if ((assetNode.Descriptor is CollectionDescriptor || assetNode.Descriptor is DictionaryDescriptor) && CollectionItemIdHelper.HasCollectionItemIds(objectNode.Retrieve()))
                {
                    // Items to add and to remove are stored in local collections and processed later, since they might affect indices
                    var itemsToRemove = new List<ItemId>();
                    var itemsToAdd = new SortedList<object, ItemId>(new DefaultKeyComparer());

                    // Check for item present in the instance and absent from the base.
                    foreach (var index in objectNode.Indices)
                    {
                        // Skip overridden items
                        if (objectNode.IsItemOverridden(index))
                            continue;

                        var itemId = objectNode.IndexToId(index);
                        if (itemId != ItemId.Empty)
                        {
                            // Look if an item with the same id exists in the base.
                            if (!baseNode.HasId(itemId))
                            {
                                // If not, remove this item from the instance.
                                itemsToRemove.Add(itemId);
                            }
                        }
                        else
                        {
                            // This case should not happen, but if we have an empty id due to corrupted data let's just remove the item.
                            itemsToRemove.Add(itemId);
                        }
                    }

                    // Clean items marked as "override-deleted" that are absent from the base.
                    var ids = CollectionItemIdHelper.GetCollectionItemIds(localValue);
                    foreach (var deletedId in ids.DeletedItems.ToList())
                    {
                        if (baseNode.Indices.All(x => baseNode.IndexToId(x) != deletedId))
                        {
                            ids.UnmarkAsDeleted(deletedId);
                        }
                    }

                    // Add item present in the base and missing here, and also update items that have different values between base and instance
                    foreach (var index in baseNode.Indices)
                    {
                        var itemId = baseNode.IndexToId(index);
                        // TODO: What should we do if it's empty? It can happen only from corrupted data

                        // Skip items marked as "override-deleted"
                        if (itemId == ItemId.Empty || objectNode.IsItemDeleted(itemId))
                            continue;

                        Index localIndex;
                        if (!objectNode.TryIdToIndex(itemId, out localIndex))
                        {
                            // For dictionary, we might have a key collision, if so, we consider that the new value from the base is deleted in the instance.
                            var keyCollision = assetNode.Descriptor is DictionaryDescriptor && (objectNode.ItemReferences?.HasIndex(index) == true || objectNode.Indices.Any(x => index.Equals(x)));
                            // For specific collections (eg. EntityComponentCollection) it might not be possible to add due to other kinds of collisions or invalid value.
                            var itemRejected = !CanUpdate(assetNode, ContentChangeType.CollectionAdd, localIndex, baseNode.Retrieve(index));

                            // We cannot add the item, let's mark it as deleted.
                            if (keyCollision || itemRejected)
                            {
                                var instanceIds = CollectionItemIdHelper.GetCollectionItemIds(assetNode.Retrieve());
                                instanceIds.MarkAsDeleted(itemId);
                            }
                            else
                            {
                                // Add it if the key is available for add
                                itemsToAdd.Add(index.Value, itemId);
                            }
                        }
                        else
                        {
                            // If the item is present in both the instance and the base, check if we need to reconcile the value
                            // Skip it if it's overridden
                            if (!objectNode.IsItemOverridden(localIndex))
                            {
                                if (ShouldReconcileItem(objectNode, localIndex, index))
                                {
                                    object clonedValue;
                                    var baseItemValue = objectNode.BaseNode.Retrieve(index);
                                    // Object references
                                    if (baseItemValue is IIdentifiable && IsObjectReference(objectNode.BaseNode, index, baseItemValue))
                                        clonedValue = BaseToDerivedRegistry.ResolveFromBase(baseItemValue, objectNode);
                                    else
                                        clonedValue = CloneValueFromBase(baseItemValue, assetNode);

                                    objectNode.Update(clonedValue, localIndex);
                                }
                            }
                            // In dictionaries, the keys might be different between the instance and the base. We need to reconcile them too
                            if (objectNode.Descriptor is DictionaryDescriptor && !objectNode.IsKeyOverridden(localIndex))
                            {
                                if (ShouldReconcileIndex(localIndex, index))
                                {
                                    // Reconcile using a move (Remove + Add) of the key-value pair
                                    var clonedIndex = new Index(CloneValueFromBase(index.Value, assetNode));
                                    var localItemValue = assetNode.Retrieve(localIndex);
                                    objectNode.Remove(localItemValue, localIndex);
                                    objectNode.Add(localItemValue, clonedIndex);
                                    ids[clonedIndex.Value] = itemId;
                                }
                            }
                        }
                    }

                    // Process items marked to be removed
                    foreach (var item in itemsToRemove)
                    {
                        var index = objectNode.IdToIndex(item);
                        var value = assetNode.Retrieve(index);
                        objectNode.Remove(value, index);
                        // We're reconciling, so let's hack the normal behavior of marking the removed item as deleted.
                        ids.UnmarkAsDeleted(item);
                    }

                    // Process items marked to be added
                    foreach (var item in itemsToAdd)
                    {
                        var baseIndex = baseNode.IdToIndex(item.Value);
                        var baseItemValue = baseNode.Retrieve(baseIndex);
                        var clonedValue = CloneValueFromBase(baseItemValue, assetNode);
                        if (assetNode.Descriptor is CollectionDescriptor)
                        {
                            // In a collection, we need to find an index that matches the index on the base to maintain order.
                            // To do so, we iterate from the index in the base to zero.
                            var currentBaseIndex = baseIndex.Int - 1;

                            // Initialize the target index to zero, in case we don't find any better index.
                            var localIndex = new Index(0);

                            // Find the first item of the base that also exists (in term of id) in the local node, iterating backward (from baseIndex to 0)
                            while (currentBaseIndex >= 0)
                            {
                                ItemId baseId;
                                // This should not happen since the currentBaseIndex comes from the base.
                                if (!baseNode.TryIndexToId(new Index(currentBaseIndex), out baseId))
                                    throw new InvalidOperationException("Cannot find an identifier matching the index in the base collection");

                                Index sameIndexInInstance;
                                // If we have an matching item, we want to insert right after it
                                if (objectNode.TryIdToIndex(baseId, out sameIndexInInstance))
                                {
                                    localIndex = new Index(sameIndexInInstance.Int + 1);
                                    break;
                                }
                                currentBaseIndex--;
                            }

                            objectNode.Restore(clonedValue, localIndex, item.Value);
                        }
                        else
                        {
                            // This case is for dictionary. Key collisions have already been handle at that point so we can directly do the add without further checks.
                            objectNode.Restore(clonedValue, baseIndex, item.Value);
                        }
                    }
                }

                objectNode.ResettingOverride = false;
            }
        }

        private bool ShouldReconcileMember([NotNull] IAssetMemberNode memberNode)
        {
            var localValue = memberNode.Retrieve();
            var baseValue = memberNode.BaseNode.Retrieve();

            // Object references
            if (baseValue is IIdentifiable && IsObjectReference(memberNode.BaseNode, Index.Empty, baseValue))
            {
                var derivedTarget = BaseToDerivedRegistry.ResolveFromBase(baseValue, memberNode);
                return !Equals(localValue, derivedTarget);
            }

            // Non value type and non primitive types
            if (memberNode.IsReference || memberNode.BaseNode.IsReference)
            {
                return localValue?.GetType() != baseValue?.GetType();
            }

            // Content reference (note: they are not treated as reference but as primitive type)
            if (AssetRegistry.IsContentType(localValue?.GetType()) || AssetRegistry.IsContentType(baseValue?.GetType()))
            {
                var localRef = AttachedReferenceManager.GetAttachedReference(localValue);
                var baseRef = AttachedReferenceManager.GetAttachedReference(baseValue);
                return localRef?.Id != baseRef?.Id || localRef?.Url != baseRef?.Url;
            }

            // Value type, we check for equality
            return !Equals(localValue, baseValue);
        }

        private bool ShouldReconcileItem(IAssetObjectNode node, Index localIndex, Index baseIndex)
        {
            var localValue = node.Retrieve(localIndex);
            var baseValue = node.BaseNode.Retrieve(baseIndex);

            // Object references
            if (baseValue is IIdentifiable && IsObjectReference(node.BaseNode, baseIndex, baseValue))
            {
                var derivedTarget = BaseToDerivedRegistry.ResolveFromBase(baseValue, node);
                return !Equals(localValue, derivedTarget);
            }

            // Non value type and non primitive types
            if (node.IsReference || node.BaseNode.IsReference)
            {
                return localValue?.GetType() != baseValue?.GetType();
            }

            // Content reference (note: they are not treated as reference but as primitive type)
            if (AssetRegistry.IsContentType(localValue?.GetType()) || AssetRegistry.IsContentType(baseValue?.GetType()))
            {
                var localRef = AttachedReferenceManager.GetAttachedReference(localValue);
                var baseRef = AttachedReferenceManager.GetAttachedReference(baseValue);
                return localRef?.Id != baseRef?.Id || localRef?.Url != baseRef?.Url;
            }

            // Value type, we check for equality
            return !Equals(localValue, baseValue);
        }

        private static bool ShouldReconcileIndex(Index localIndex, Index baseIndex)
        {
            return !Equals(localIndex, baseIndex);
        }
    }
}
