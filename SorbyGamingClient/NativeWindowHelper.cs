using System;
using System.Runtime.InteropServices;

namespace SorbyGamingClient
{
    // Windows lader normalt kun det program, brugeren sidst brugte, tage
    // fokus. Ved at koble sig kortvarigt på forgrundsvinduets tråd kan
    // låseskærmen alligevel lægge sig forrest (fx efter en opdatering).
    internal static class NativeWindowHelper
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        // Lægger vinduet øverst uden at tage fokus fra det aktive program.
        public static void SetTopMostNoActivate(IntPtr handle)
        {
            const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        // Ctrl+Shift+<tast> for hele Windows, så genvejen virker, selv om
        // låseskærmen er skjult (under en session) eller ikke har fokus.
        public static bool RegisterGlobalHotkey(IntPtr handle, int id, System.Windows.Forms.Keys key)
        {
            return RegisterHotKey(handle, id, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)key);
        }

        public static void UnregisterGlobalHotkey(IntPtr handle, int id)
        {
            UnregisterHotKey(handle, id);
        }

        public static void ForceForeground(IntPtr handle)
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();

                if (foreground == handle)
                {
                    return;
                }

                uint currentThread = GetCurrentThreadId();
                uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
                bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                    AttachThreadInput(currentThread, foregroundThread, true);

                try
                {
                    BringWindowToTop(handle);
                    SetForegroundWindow(handle);
                }
                finally
                {
                    if (attached)
                    {
                        AttachThreadInput(currentThread, foregroundThread, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke lægge låseskærmen forrest: {ex.Message}");
            }
        }
    }
}
