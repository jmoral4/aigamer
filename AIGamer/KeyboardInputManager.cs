namespace AIGamer
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;

    public class KeyboardInputManager
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private IntPtr gameWindowHandle;

        public KeyboardInputManager(IntPtr windowHandle)
        {
            gameWindowHandle = windowHandle;
        }

        public bool FocusGameWindow()
        {
            if (gameWindowHandle == IntPtr.Zero)
                return false;

            return SetForegroundWindow(gameWindowHandle);
        }

        public void SendKey(char key)
        {
            if (!FocusGameWindow())
            {
                Console.WriteLine("Failed to focus game window.");
                return;
            }

            // Convert character to virtual key code
            short vkKeyScan = VkKeyScan(key);
            byte virtualKeyCode = (byte)(vkKeyScan & 0xff);
            bool isShiftRequired = (vkKeyScan & 0x100) != 0;

            try
            {
                // Press shift if required (for uppercase or symbols)
                if (isShiftRequired)
                {
                    keybd_event(0x10, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // SHIFT down
                }

                // Press and release the key
                keybd_event(virtualKeyCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // Key down
                Thread.Sleep(30); // Small delay for reliability
                keybd_event(virtualKeyCode, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero); // Key up

                // Release shift if it was pressed
                if (isShiftRequired)
                {
                    keybd_event(0x10, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero); // SHIFT up
                }

                // Small delay to avoid overwhelming the game
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending key '{key}': {ex.Message}");
            }
        }

        public void SendText(string text)
        {
            foreach (char c in text)
            {
                SendKey(c);
            }
        }

        public void SendEnter()
        {
            if (!FocusGameWindow())
            {
                Console.WriteLine("Failed to focus game window.");
                return;
            }

            try
            {
                // Press and release Enter
                keybd_event(0x0D, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // ENTER down
                Thread.Sleep(30);
                keybd_event(0x0D, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero); // ENTER up
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending Enter key: {ex.Message}");
            }
        }

        public void SendArrowKey(ArrowDirection direction)
        {
            if (!FocusGameWindow())
            {
                Console.WriteLine("Failed to focus game window.");
                return;
            }

            byte keyCode;
            switch (direction)
            {
                case ArrowDirection.Up:
                    keyCode = 0x26; // VK_UP
                    break;
                case ArrowDirection.Down:
                    keyCode = 0x28; // VK_DOWN
                    break;
                case ArrowDirection.Left:
                    keyCode = 0x25; // VK_LEFT
                    break;
                case ArrowDirection.Right:
                    keyCode = 0x27; // VK_RIGHT
                    break;
                default:
                    return;
            }

            try
            {
                keybd_event(keyCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // Key down
                Thread.Sleep(30);
                keybd_event(keyCode, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero); // Key up
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending arrow key: {ex.Message}");
            }
        }
    }

    public enum ArrowDirection
    {
        Up,
        Down,
        Left,
        Right
    }
}
