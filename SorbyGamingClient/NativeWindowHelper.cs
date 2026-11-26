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

        // Processer, hvis tråde der aldrig kobles på (spillene under en session).
        // Programmet rører aldrig spillets input - kun sine egne vinduer.
        public static Func<uint, bool>? NoAttachProcess { get; set; }

        // Kører et program i "eksklusiv fuldskærm" (Direct3D)? Så tegner spillet
        // uden om Windows' skrivebord, og ingen almindelige vinduer kan ses
        // ovenpå. Vinduer skubbes så ikke frem, da det kan få spillet til at blinke.
        public static bool IsExclusiveFullscreen()
        {
            const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

            try
            {
                return SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;
            }
            catch
            {
                return false;
            }
        }

        // Ligger et andet synligt vindue over (en del af) vinduet?
        public static bool IsCovered(IntPtr handle)
        {
            if (!GetWindowRect(handle, out RECT mine))
            {
                return false;
            }

            for (IntPtr above = GetWindow(handle, GW_HWNDPREV); above != IntPtr.Zero; above = GetWindow(above, GW_HWNDPREV))
            {
                if (!IsWindowVisible(above) || IsCloaked(above) || !GetWindowRect(above, out RECT other))
                {
                    continue;
                }

                bool overlaps = other.Left < mine.Right && other.Right > mine.Left && other.Top < mine.Bottom && other.Bottom > mine.Top;

                if (overlaps)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCloaked(IntPtr handle)
        {
            const int DWMWA_CLOAKED = 14;

            try
            {
                return DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch
            {
                return false;
            }
        }

        private const uint GW_HWNDPREV = 3;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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
                uint foregroundProcess = 0;
                uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out foregroundProcess);
                bool protectedGame = foregroundProcess != 0 && NoAttachProcess?.Invoke(foregroundProcess) == true;
                bool attached = !protectedGame && foregroundThread != 0 && foregroundThread != currentThread &&
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
