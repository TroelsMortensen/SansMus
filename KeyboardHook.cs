using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SansMus
{
    public class GlobalKeyboardHook : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private volatile bool _keyHandled = false; // Thread-safe flag to track if key was handled
        private volatile uint _lastScanCode = 0; // Last scan code from hook (for character translation)
        
        public event KeyEventHandler? KeyDown;
        public event KeyEventHandler? KeyUp;
        
        /// <summary>
        /// Gets the scan code of the last processed key. Used for character translation.
        /// </summary>
        public uint LastScanCode => _lastScanCode;
        
        public GlobalKeyboardHook()
        {
            _proc = HookCallback;
            _hookID = SetHook(_proc);
        }
        
        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule?.ModuleName), 0);
            }
        }
        
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    // Reset handled flag at start of each callback
                    _keyHandled = false;
                    
                    KBDLLHOOKSTRUCT kbStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT))!;
                    Keys key = (Keys)kbStruct.vkCode;
                    
                    // Store scan code for character translation
                    _lastScanCode = kbStruct.scanCode;
                    
                    KeyEventArgs e = new KeyEventArgs(key);
                    
                    if (wParam == (IntPtr)WM_KEYDOWN)
                    {
                        KeyDown?.Invoke(this, e);
                        // If key was handled, consume the event to prevent propagation
                        if (_keyHandled)
                        {
                            return (IntPtr)1; // Consume event - prevent propagation
                        }
                    }
                    else if (wParam == (IntPtr)WM_KEYUP)
                    {
                        KeyUp?.Invoke(this, e);
                        // If key was handled, consume the event to prevent propagation
                        if (_keyHandled)
                        {
                            return (IntPtr)1; // Consume event - prevent propagation
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle exceptions to prevent crash
            }
            
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
        
        /// <summary>
        /// Marks the current key event as handled, which will prevent it from propagating to other applications.
        /// Call this method from KeyDown or KeyUp event handlers when a key is actually handled.
        /// </summary>
        public void MarkKeyHandled()
        {
            _keyHandled = true;
        }
        
        public void Dispose()
        {
            UnhookWindowsHookEx(_hookID);
        }
    }
}

