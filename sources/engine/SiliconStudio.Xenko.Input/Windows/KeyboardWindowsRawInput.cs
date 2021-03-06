// Copyright (c) 2016-2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.

#if SILICONSTUDIO_PLATFORM_WINDOWS_DESKTOP && (SILICONSTUDIO_XENKO_UI_WINFORMS || SILICONSTUDIO_XENKO_UI_WPF)
using System;

namespace SiliconStudio.Xenko.Input
{
    internal class KeyboardWindowsRawInput : KeyboardDeviceBase
    {
        public KeyboardWindowsRawInput(InputSourceWindowsRawInput source)
        {
            // Raw input is usually prefered above other keyboards
            Priority = 100;
            Source = source;
        }

        public override string Name => "Windows Keyboard (Raw Input)";

        public override Guid Id => new Guid("d7437ff5-d14f-4491-9673-377b6d0e241c");

        public override IInputSource Source { get; }
    }
}
#endif