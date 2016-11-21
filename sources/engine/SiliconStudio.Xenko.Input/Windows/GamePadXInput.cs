﻿// Copyright (c) 2016 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

#if SILICONSTUDIO_PLATFORM_WINDOWS_DESKTOP
using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX;
using SharpDX.Text;
using SharpDX.XInput;
using SiliconStudio.Core.Mathematics;

namespace SiliconStudio.Xenko.Input
{
    public class GamePadXInput : GamePadDeviceBase, IDisposable
    {
        private readonly Controller controller;
        private State xinputState;
        private short[] lastAxisState = new short[6];
        private GamePadState state = new GamePadState();

        public GamePadXInput(Controller controller, Guid id, int index)
        {
            this.controller = controller;
            SetIndexInternal(index);
            Id = id;

            // Used to create a produc ID that is consistent with SDL2's method of generating them
            // Taken from SDL 2.0.5 (src\joystick\windows\SDL_xinputjoystick.c:149)
            var subType = controller.GetCapabilities(DeviceQueryType.Any).SubType;
            var pidBytes = Encoding.ASCII.GetBytes("xinput").ToList();
            pidBytes.Add((byte)subType);
            while(pidBytes.Count < 16)
                pidBytes.Add(0);
            ProductId = new Guid(pidBytes.ToArray());
        }

        public void Dispose()
        {
            if (Disconnected == null)
                throw new InvalidOperationException("Something should handle controller disconnect");
            Disconnected.Invoke(this, null);
        }

        public override string DeviceName => $"XInput GamePad {Index}";
        public override Guid Id { get; }
        public override Guid ProductId { get; }

        public override GamePadState State
        {
            get { return state; }
        }

        public event EventHandler Disconnected;

        public override void Update(List<InputEvent> inputEvents)
        {
            if ((int)controller.UserIndex != Index)
            {
                SetIndexInternal((int)controller.UserIndex);
            }

            if (controller.GetState(out xinputState))
            {
                // DPad/Shoulder/Thumb/Option buttons
                for (int i = 0; i < 16; i++)
                {
                    int mask = 1 << i;
                    var masked = ((int)xinputState.Gamepad.Buttons & mask);
                    if (masked != ((int)state.Buttons & mask))
                    {
                        ButtonState buttonState = (masked != 0) ? ButtonState.Down : ButtonState.Up;
                        GamePadButtonEvent buttonEvent = InputEventPool<GamePadButtonEvent>.GetOrCreate(this);
                        buttonEvent.State = buttonState;
                        buttonEvent.Button = (GamePadButton)mask; // 1 to 1 mapping with XInput buttons
                        inputEvents.Add(buttonEvent);
                        state.Update(buttonEvent);
                    }
                }
                
                // Axes
                if (ChangeAxis(0, xinputState.Gamepad.LeftThumbX))
                    inputEvents.Add(CreateAxisEvent(GamePadAxis.LeftThumbX, xinputState.Gamepad.LeftThumbX / 32768.0f));
                if (ChangeAxis(1, xinputState.Gamepad.LeftThumbY))
                    inputEvents.Add(CreateAxisEvent(GamePadAxis.LeftThumbY, xinputState.Gamepad.LeftThumbY / 32768.0f));

                if (ChangeAxis(2, xinputState.Gamepad.RightThumbX))
                    inputEvents.Add(CreateAxisEvent(GamePadAxis.RightThumbX, xinputState.Gamepad.RightThumbX / 32768.0f));
                if (ChangeAxis(3, xinputState.Gamepad.RightThumbY))
                    inputEvents.Add(CreateAxisEvent(GamePadAxis.RightThumbY, xinputState.Gamepad.RightThumbY / 32768.0f));

                if (ChangeAxis(4, xinputState.Gamepad.LeftTrigger))
                    inputEvents.Add(CreateAxisEvent(GamePadAxis.LeftTrigger, xinputState.Gamepad.LeftTrigger / 255.0f));
                if (ChangeAxis(5, xinputState.Gamepad.RightTrigger))
                    inputEvents.Add(CreateAxisEvent(GamePadAxis.RightTrigger, xinputState.Gamepad.RightTrigger / 255.0f));
            }

            if (!controller.IsConnected)
            {
                Dispose();
            }
        }

        public void SetVibration(float leftMotor, float rightMotor)
        {
            try
            {
                leftMotor = MathUtil.Clamp(leftMotor, 0.0f, 1.0f);
                rightMotor = MathUtil.Clamp(rightMotor, 0.0f, 1.0f);
                var vibration = new Vibration
                {
                    LeftMotorSpeed = (ushort)(leftMotor * 65535.0f),
                    RightMotorSpeed = (ushort)(rightMotor * 65535.0f)
                };
                controller.SetVibration(vibration);
            }
            catch (SharpDXException)
            {
                Dispose();
            }
        }

        public override void SetVibration(float smallLeft, float smallRight, float largeLeft, float largeRight)
        {
            SetVibration((smallLeft + largeLeft) * 0.5f, (smallRight + largeRight) * 0.5f);
        }

        private bool ChangeAxis(int index, short newValue)
        {
            if (lastAxisState[index] != newValue)
            {
                lastAxisState[index] = newValue;
                return true;
            }
            return false;
        }

        private GamePadAxisEvent CreateAxisEvent(GamePadAxis axis, float newValue)
        {
            GamePadAxisEvent axisEvent = InputEventPool<GamePadAxisEvent>.GetOrCreate(this);
            axisEvent.Value = newValue;
            axisEvent.Axis = axis;
            state.Update(axisEvent);
            return axisEvent;
        }
    }
}

#endif