using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BgClicker
{
    internal static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_SHOW_APP = 0x8001;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const uint GA_ROOT = 2;
        public const int MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint milliseconds);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        [DllImport("advapi32.dll")]
        public static extern IntPtr GetSidSubAuthority(IntPtr sid, int index);

        [DllImport("advapi32.dll")]
        public static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        public static void SetDarkTitleBar(IntPtr hWnd, bool dark)
        {
            int value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hWnd, 20, ref value, 4) != 0)
            {
                DwmSetWindowAttribute(hWnd, 19, ref value, 4);
            }

            const uint flags = 0x0002 | 0x0001 | 0x0004 | 0x0020; // NOMOVE | NOSIZE | NOZORDER | FRAMECHANGED
            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, flags);
        }

        public static int GetProcessIntegrity(int processId)
        {
            IntPtr process = OpenProcess(0x1000, false, processId);
            if (process == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr token = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                if (!OpenProcessToken(process, 0x0008, out token))
                {
                    return 0;
                }

                buffer = Marshal.AllocHGlobal(256);
                int length;
                if (!GetTokenInformation(token, 25, buffer, 256, out length))
                {
                    return 0;
                }

                IntPtr sid = Marshal.ReadIntPtr(buffer);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return Marshal.ReadInt32(GetSidSubAuthority(sid, count - 1));
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }

                CloseHandle(process);
            }
        }

        public static bool IsLowIntegrity()
        {
            IntPtr token = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token))
                {
                    return false;
                }

                buffer = Marshal.AllocHGlobal(256);
                int length;
                if (!GetTokenInformation(token, 25, buffer, 256, out length))
                {
                    return false;
                }

                IntPtr sid = Marshal.ReadIntPtr(buffer);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                int rid = Marshal.ReadInt32(GetSidSubAuthority(sid, count - 1));
                return rid < 0x2000;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static string GetWindowTitle(IntPtr hWnd)
        {
            var buffer = new StringBuilder(512);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public static string GetWindowClass(IntPtr hWnd)
        {
            var buffer = new StringBuilder(256);
            GetClassName(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        /// <summary>
        /// Posts a full left click (move + down + up) to a window without moving the
        /// real cursor or changing the foreground window.
        /// </summary>
        public static bool PostClick(IntPtr hWnd, int x, int y)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            {
                return false;
            }

            IntPtr lParam = MakeLParam(x, y);
            PostMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
            PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
            PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
            return true;
        }
    }
}
