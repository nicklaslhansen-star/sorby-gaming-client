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
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

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
