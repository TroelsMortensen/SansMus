using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SansMouse
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
        
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        
        public event KeyEventHandler? KeyDown;
        
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
                if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
                {
                    KBDLLHOOKSTRUCT kbStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT))!;
                    Keys key = (Keys)kbStruct.vkCode;
                    
                    KeyEventArgs e = new KeyEventArgs(key);
                    KeyDown?.Invoke(this, e);
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
        
        public void Dispose()
        {
            UnhookWindowsHookEx(_hookID);
        }
    }
}

