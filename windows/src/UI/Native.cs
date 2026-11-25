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
        public const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        public const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;
        public const int WM_MOUSEWHEEL = 0x20A, WM_MOUSEHWHEEL = 0x20E, WM_MOUSELEAVE = 0x2A3;
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
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, int flags);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }

        /// <summary>Windows 빌드 번호 (Windows 11 은 22000 이상).</summary>
        public static readonly int Build = IsWindows ? Environment.OSVersion.Version.Build : 0;

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
                SetOwner(hwnd, progman);
                return progman;
            }
            catch (Exception) { return IntPtr.Zero; }
        }

        /// <summary>창의 소유자를 바꾼다. 소유된 창은 늘 소유자 바로 위에 놓인다.</summary>
        public static void SetOwner(IntPtr hwnd, IntPtr owner)
        {
            if (!IsWindows) return;
            try
            {
                if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_HWNDPARENT, owner);
                else SetWindowLong32(hwnd, GWL_HWNDPARENT, owner.ToInt32());
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 창 뒤 화면을 흐리게 비추는 유리 바탕을 켠다 (Windows 10 1803 이상).
        /// rgb = 유리에 입힐 색, alpha = 그 색의 진하기 0~255. simple = 옮기는 동안 쓰는 가벼운 흐림.
        /// </summary>
        public static bool SetGlass(IntPtr hwnd, int rgb, int alpha, bool simple)
        {
            if (!IsWindows || Build < 17134) return false;
            IntPtr mem = IntPtr.Zero;
            try
            {
                var accent = new AccentPolicy
                {
                    State = simple ? 3 : 4,   // 3 = 흐림, 4 = 아크릴
                    Flags = 2,                // 색 입히기
                    // 0xAABBGGRR. 아크릴은 진하기가 0 이면 그려지지 않는다.
                    GradientColor = (Math.Max(1, Math.Min(255, alpha)) << 24) | ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF),
                };
                int size = Marshal.SizeOf(typeof(AccentPolicy));
                mem = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, mem, false);
                var data = new CompositionData { Attribute = 19, Data = mem, Size = size };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            catch (Exception) { return false; }
            finally { if (mem != IntPtr.Zero) Marshal.FreeHGlobal(mem); }
        }

        /// <summary>창을 다른 창 바로 아래로 (위치·크기·포커스는 그대로).</summary>
        public static void PlaceBehind(IntPtr hwnd, IntPtr above)
        {
            if (!IsWindows) return;
            try { SetWindowPos(hwnd, above, 0, 0, 0, 0, 0x1 | 0x2 | 0x10); } catch (Exception) { } // NOSIZE | NOMOVE | NOACTIVATE
        }

        /// <summary>화면의 그 자리에서 마우스를 받는 창.</summary>
        public static IntPtr WindowAt(System.Drawing.Point screen)
        {
            if (!IsWindows) return IntPtr.Zero;
            try { return WindowFromPoint(new POINT { X = screen.X, Y = screen.Y }); } catch (Exception) { return IntPtr.Zero; }
        }

        public static IntPtr Send(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (!IsWindows) return IntPtr.Zero;
            try { return SendMessage(hwnd, msg, wParam, lParam); } catch (Exception) { return IntPtr.Zero; }
        }

        public static IntPtr PointParam(System.Drawing.Point p)
        {
            return (IntPtr)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
        }

        public static System.Drawing.Point ParamPoint(IntPtr lParam)
        {
            long v = lParam.ToInt64();
            return new System.Drawing.Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        /// <summary>테두리를 잡고 크기를 바꾸기 시작한다 (edge = HTLEFT 등).</summary>
        public static void BeginSize(IntPtr hwnd, int edge, System.Drawing.Point screen)
        {
            if (!IsWindows) return;
            try
            {
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)edge, PointParam(screen));
            }
            catch (Exception) { }
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
