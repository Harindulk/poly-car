using UnityEngine;
using UnityEngine.InputSystem;

namespace PolyCar
{
    /// <summary>What a driver (player or bot) asks the car to do this frame.</summary>
    public struct DriveInput
    {
        public float Throttle;  // 0..1
        public float Brake;     // 0..1 (also reverse)
        public float Steer;     // -1..1
        public bool Handbrake;
        public bool Boost;
    }

    /// <summary>
    /// Keyboard + any gamepad, polled straight from the Input System devices so the game needs no
    /// InputActions asset. WASD/arrows drive, Space handbrake, Shift boost, R respawn, C camera, Esc pause.
    /// </summary>
    public static class PolyInput
    {
        private const float StickDeadZone = 0.15f;

        public static DriveInput ReadDrive()
        {
            var input = new DriveInput();
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.Throttle = 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.Brake = 1f;
                float steer = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
                input.Steer = steer;
                input.Handbrake = kb.spaceKey.isPressed;
                input.Boost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed || kb.nKey.isPressed;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                input.Throttle = Mathf.Max(input.Throttle, pad.rightTrigger.ReadValue());
                input.Brake = Mathf.Max(input.Brake, pad.leftTrigger.ReadValue());
                float stick = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > StickDeadZone)
                {
                    // Rescale past the dead zone and soften the centre for finer control.
                    float s = (Mathf.Abs(stick) - StickDeadZone) / (1f - StickDeadZone);
                    s = Mathf.Sign(stick) * Mathf.Pow(s, 1.4f);
                    if (Mathf.Abs(s) > Mathf.Abs(input.Steer)) input.Steer = s;
                }
                if (pad.dpad.left.isPressed) input.Steer = -1f;
                if (pad.dpad.right.isPressed) input.Steer = 1f;
                input.Handbrake |= pad.buttonSouth.isPressed || pad.rightShoulder.isPressed;
                input.Boost |= pad.buttonWest.isPressed || pad.leftShoulder.isPressed;
            }
            return input;
        }

        public static bool RespawnPressed()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame);
        }

        public static bool PausePressed()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame)) || (pad != null && pad.startButton.wasPressedThisFrame);
        }

        public static bool CameraPressed()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && kb.cKey.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame);
        }

        public static bool LookBackHeld()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && kb.qKey.isPressed) || (pad != null && pad.rightStickButton.isPressed);
        }

        public static bool RestartPressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.tKey.wasPressedThisFrame;
        }

        public static bool BackPressed()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonEast.wasPressedThisFrame);
        }

        public static bool AnyKeyPressed()
        {
            var kb = Keyboard.current; var pad = Gamepad.current; var mouse = Mouse.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }
    }
}
