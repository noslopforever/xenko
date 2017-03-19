﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Core.Reflection;
using SiliconStudio.Quantum;
using SiliconStudio.Quantum.Commands;

namespace SiliconStudio.Presentation.Quantum.Presenters
{
    public abstract class NodePresenterBase : IInitializingNodePresenter
    {
        private readonly INodePresenterFactoryInternal factory;
        private readonly List<INodePresenter> children = new List<INodePresenter>();

        protected NodePresenterBase([NotNull] INodePresenterFactoryInternal factory, INodePresenter parent)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            this.factory = factory;
            Parent = parent;
        }

        public virtual void Dispose()
        {
            // Do nothing by default
        }

        public INodePresenter Parent { get; }

        public IReadOnlyList<INodePresenter> Children => children;

        public abstract string Name { get; }
        public abstract List<INodePresenterCommand> Commands { get; }
        public abstract Type Type { get; }
        public abstract bool IsPrimitive { get; }
        public abstract bool IsEnumerable { get; }
        public abstract Index Index { get; }
        public abstract ITypeDescriptor Descriptor { get; }
        public abstract int? Order { get; }
        public abstract object Value { get; }
        public abstract event EventHandler<ValueChangingEventArgs> ValueChanging;
        public abstract event EventHandler<ValueChangedEventArgs> ValueChanged;

        protected abstract IObjectNode ParentingNode { get; }

        public abstract void UpdateValue(object newValue);

        public abstract void AddItem(object value);

        public abstract void AddItem(object value, Index index);

        public abstract void RemoveItem(object value, Index index);

        public abstract void UpdateItem(object newValue, Index index);

        protected void Refresh()
        {
            children.Clear();
            var parentingNode = ParentingNode;
            if (parentingNode != null)
            {
                factory.CreateChildren(this, parentingNode);
            }            
        }

        protected void AttachCommands()
        {
            foreach (var command in factory.AvailableCommands)
            {
                if (command.CanAttach(this))
                    Commands.Add(command);
            }
        }

        internal abstract Task RunCommand(INodeCommand command, object parameter);

        void IInitializingNodePresenter.AddChild([NotNull] IInitializingNodePresenter child)
        {
            children.Add(child);
        }

        void IInitializingNodePresenter.FinalizeInitialization()
        {
            children.Sort(GraphNodePresenter.CompareChildren);
        }
    }
}
