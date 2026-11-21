using System;
using System.Runtime.InteropServices;

namespace TodoWidget.UI
{
    /// <summary>Win32 호출. 모든 호출은 실패해도 앱이 죽지 않도록 감싼다.</summary>
    static class Native
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int WM_NCHITTEST = 0x84;
        public const int HTCLIENT = 1, HTCAPTION = 2;
        public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        const int EM_SETCUEBANNER = 0x1501;
        const int GWL_HWNDPARENT = -8;

        public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        static extern IntPtr SendMessageStr(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr h, int idx, int v);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        /// <summary>창을 마우스로 끌어 옮기기 (제목표시줄 드래그와 동일).</summary>
        public static void BeginDrag(IntPtr hwnd)
        {
            if (!IsWindows) return;
            try
            {
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            catch (Exception) { }
        }

        public static void SetCueBanner(IntPtr editHwnd, string text)
        {
            if (!IsWindows) return;
            try { SendMessageStr(editHwnd, EM_SETCUEBANNER, (IntPtr)1, text); } catch (Exception) { }
        }

        /// <summary>바탕화면(Progman)을 소유자로 지정해서 Win+D(바탕화면 보기)에도 위젯이 남아 있게 한다.</summary>
        public static IntPtr PinToDesktop(IntPtr hwnd)
        {
            if (!IsWindows) return IntPtr.Zero;
            try
            {
                var progman = FindWindow("Progman", null);
                if (progman == IntPtr.Zero) return IntPtr.Zero;
                if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_HWNDPARENT, progman);
                else SetWindowLong32(hwnd, GWL_HWNDPARENT, progman.ToInt32());
                return progman;
            }
            catch (Exception) { return IntPtr.Zero; }
        }

        public static bool IsAlive(IntPtr hwnd)
        {
            if (!IsWindows || hwnd == IntPtr.Zero) return false;
            try { return IsWindow(hwnd); } catch (Exception) { return false; }
        }

        /// <summary>Windows 11 둥근 모서리.</summary>
        public static void RoundCorners(IntPtr hwnd, bool small = false)
        {
            if (!IsWindows) return;
            try { int v = small ? 3 : 2; DwmSetWindowAttribute(hwnd, 33, ref v, 4); } catch (Exception) { }
        }

        /// <summary>대화상자 제목표시줄을 어둡게 (Windows 10 20H1+/11).</summary>
        public static void DarkTitleBar(IntPtr hwnd)
        {
            if (!IsWindows) return;
            try
            {
                int v = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref v, 4);
            }
            catch (Exception) { }
        }
    }
}
