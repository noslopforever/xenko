﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using SiliconStudio.Assets;
using SiliconStudio.Assets.Compiler;
using SiliconStudio.Core;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Core.Serialization;
using SiliconStudio.Core.Serialization.Contents;
using SiliconStudio.Xenko.Graphics;
using SiliconStudio.Xenko.Rendering.Skyboxes;

namespace SiliconStudio.Xenko.Assets.Skyboxes
{
    /// <summary>
    /// The skybox asset.
    /// </summary>
    [DataContract("SkyboxAsset")]
    [AssetDescription(FileExtension)]
    [AssetContentType(typeof(Skybox))]
    [Display(1000, "Skybox")]
    public sealed class SkyboxAsset : Asset
    {
        /// <summary>
        /// The default file extension used by the <see cref="SkyboxAsset"/>.
        /// </summary>
        public const string FileExtension = ".xksky;.pdxsky";

        /// <summary>
        /// Initializes a new instance of the <see cref="SkyboxAsset"/> class.
        /// </summary>
        public SkyboxAsset()
        {
            Usage = SkyboxUsage.Lighting;
            DiffuseSHOrder = SkyboxPreFilteringDiffuseOrder.Order3;
            SpecularCubeMapSize = 256;
        }

        /// <summary>
        /// Gets or sets the type of skybox.
        /// </summary>
        /// <value>The type of skybox.</value>
        /// <userdoc>The source to use as skybox</userdoc>
        [DataMember(10)]
        [Display("CubeMap", Expand = ExpandRule.Always)]
        public Texture CubeMap { get; set; }

        /// <summary>
        /// Gets or sets the usge of this skybox
        /// </summary>
        /// <userdoc>The usage of this skybox</userdoc>
        [DataMember(15)]
        [DefaultValue(SkyboxUsage.Lighting)]
        public SkyboxUsage Usage { get; set; }

        /// <summary>
        /// Gets or sets the diffuse sh order.
        /// </summary>
        /// <value>The diffuse sh order.</value>
        /// <userdoc>Specify the order of the accuracy of spherical harmonics used to calculate the irradiance of the skybox</userdoc>
        [DefaultValue(SkyboxPreFilteringDiffuseOrder.Order3)]
        [Display("Diffuse SH Order")]
        [DataMember(20)]
        public SkyboxPreFilteringDiffuseOrder DiffuseSHOrder { get; set; }

        /// <summary>
        /// Gets or sets the diffuse sh order.
        /// </summary>
        /// <value>The diffuse sh order.</value>
        /// <userdoc>Specify the size of the irradiance cube map used for the specular lighting</userdoc>
        [DefaultValue(256)]
        [Display("Specular CubeMap Size")]
        [DataMember(30)]
        [DataMemberRange(64, int.MaxValue)]
        public int SpecularCubeMapSize { get; set; }

        public IEnumerable<IReference> GetDependencies()
        {
            if (CubeMap != null)
            {
                var reference = AttachedReferenceManager.GetAttachedReference(CubeMap);
                yield return new AssetReference(reference.Id, reference.Url);
            }
        }
    }
}