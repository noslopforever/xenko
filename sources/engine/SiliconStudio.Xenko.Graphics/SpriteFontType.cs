// Copyright (c) 2014-2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.


using SiliconStudio.Core;

namespace SiliconStudio.Xenko.Graphics
{
    public enum SpriteFontType
    {
        /// <summary>
        /// Static font which has fixed font size and is pre-compiled
        /// </summary>
        /// <userdoc>
        /// Offline rasterized sprite font which has a fixed size in-game
        /// </userdoc>
        [Display("Offline Rasterized")]
        Static,

        /// <summary>
        /// Font which can change its font size dynamically and is compiled at runtime
        /// </summary>
        /// <userdoc>
        /// Runtime (in-game) rasterized sprite font which is also resizable
        /// </userdoc>
        [Display("Runtime Rasterized")]
        Dynamic,

        /// <summary>
        /// Signed Distance Field font which is pre-compiled but can still be scaled at runtime
        /// </summary>
        /// <userdoc>
        /// Offline rasterized sprite font which is resizable in-game
        /// </userdoc>
        [Display("Signed Distance Field")]
        SDF,
    }
}
