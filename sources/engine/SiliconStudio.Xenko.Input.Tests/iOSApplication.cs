// Copyright (c) 2011-2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.
using System.Runtime.CompilerServices;
using MonoTouch.Foundation;
using MonoTouch.UIKit;
using SiliconStudio.Xenko.Effects;
using SiliconStudio.Xenko.Starter;

namespace SiliconStudio.Xenko.Input.Tests
{
    public class Application
    {
        static void Main(string[] args)
        {
            UIApplication.Main(args, null, "AppDelegate");
        }
    }

    [Register("AppDelegate")]
    public class AppDelegate : XenkoApplicationDelegate
    {
        public override bool FinishedLaunching(UIApplication app, NSDictionary options)
        {
            RuntimeHelpers.RunModuleConstructor(typeof(MaterialKeys).Module.ModuleHandle);

            Game = new InputTestGame2();

            return base.FinishedLaunching(app, options);
        }
    }
}
