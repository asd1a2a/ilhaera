using System;
using System.Drawing;
using System.Windows.Forms;

namespace TodoWidget.UI
{
    /// <summary>
    /// 리퀴드 글래스 테마에서 위젯 바로 아래에 깔리는 유리판.
    /// 이 창이 뒤 화면을 흐리게 비추고, 그 위에 바탕이 뚫린 위젯이 글자와 판만 불투명하게 얹힌다.
    /// 위젯의 뚫린 곳을 누르면 마우스가 이 창으로 떨어질 수 있으므로, 받은 마우스는 그 자리의 위젯 컨트롤로 넘긴다.
    /// 위치·크기는 늘 위젯을 따라간다 (옮기고 늘리는 것은 위젯 쪽).
    /// </summary>
    sealed class GlassPane : Form
    {
        readonly WidgetForm widget;
        readonly bool dark;
        int tint;
        bool blurred, moving;
        Control lastTarget;

        /// <summary>확인용: 이 창을 거쳐 위젯으로 넘긴 마우스 누름 횟수.</summary>
        public static int ForwardedClicks;

        public GlassPane(WidgetForm widget, bool dark, int tintPercent)
        {
            this.widget = widget;
            this.dark = dark;
            tint = tintPercent;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "일해라 유리";
            BackColor = Color.Black; // 검정으로 칠한 곳이 유리로 비친다
        }

        /// <summary>유리에 입히는 색: 밝은 테마는 흰색, 어두운 테마는 먹색.</summary>
        int TintRgb { get { return dark ? 0x16181C : 0xFFFFFF; } }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (Native.IsWindows) cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
            ApplyGlass();
        }

        /// <summary>유리 투명도 0(아주 투명)~100(불투명).</summary>
        public void SetTint(int percent)
        {
            if (percent == tint) return;
            tint = percent;
            if (IsHandleCreated) ApplyGlass();
        }

        /// <summary>
        /// 위젯을 끌거나 늘리는 동안: Windows 10 의 아크릴은 창을 옮길 때 심하게 끊기므로 가벼운 흐림으로 바꿔 둔다.
        /// </summary>
        public void SetMoving(bool value)
        {
            if (moving == value) return;
            moving = value;
            if (IsHandleCreated && blurred && Native.Build < 22000) ApplyGlass();
        }

        void ApplyGlass()
        {
            int alpha = (int)Math.Round(Math.Max(3, Math.Min(100, tint)) * 2.55);
            blurred = Native.SetGlass(Handle, TintRgb, alpha, moving && Native.Build < 22000);
            if (blurred) return;
            // 흐림을 쓸 수 없으면 색을 입힌 반투명 창으로
            int rgb = TintRgb;
            BackColor = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            Opacity = 0.35 + 0.65 * Math.Max(0, Math.Min(100, tint)) / 100.0;
        }

        // ── 마우스 넘기기 ──

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }
            if (m.Msg >= Native.WM_MOUSEMOVE && m.Msg <= Native.WM_MOUSEHWHEEL && Forward(ref m)) return;
            base.WndProc(ref m);
        }

        /// <summary>마우스 메시지를 그 자리의 위젯 컨트롤로 넘긴다. 넘겼고 더 처리할 것이 없으면 true.</summary>
        bool Forward(ref Message m)
        {
            if (widget.IsDisposed || !widget.IsHandleCreated || !widget.Visible) return false;
            bool wheel = m.Msg == Native.WM_MOUSEWHEEL || m.Msg == Native.WM_MOUSEHWHEEL;
            var screen = Native.ParamPoint(m.LParam);
            if (!wheel) screen = PointToScreen(screen);
            var target = widget.ControlAt(screen);

            if (m.Msg == Native.WM_MOUSEMOVE)
            {
                if (target != lastTarget)
                {
                    SendLeave(lastTarget);
                    lastTarget = target;
                }
                Native.Send(target.Handle, m.Msg, m.WParam, Native.PointParam(target.PointToClient(screen)));
                var cursor = EdgeCursor(widget.EdgeAt(screen)) ?? target.Cursor;
                if (Cursor != cursor) Cursor = cursor;
                return false; // 기본 처리도 거쳐야 이 창을 벗어날 때 알림을 받는다
            }

            bool down = m.Msg == Native.WM_LBUTTONDOWN || m.Msg == Native.WM_RBUTTONDOWN || m.Msg == Native.WM_MBUTTONDOWN;
            if (down)
            {
                ForwardedClicks++;
                widget.ActivateFromGlass();
            }
            if (m.Msg == Native.WM_LBUTTONDOWN)
            {
                int edge = widget.EdgeAt(screen);
                if (edge != Native.HTCLIENT)
                {
                    Native.BeginSize(widget.Handle, edge, screen);
                    return true;
                }
            }
            Native.Send(target.Handle, m.Msg, m.WParam, wheel ? m.LParam : Native.PointParam(target.PointToClient(screen)));
            return true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SendLeave(lastTarget);
            lastTarget = null;
        }

        static void SendLeave(Control c)
        {
            if (c != null && !c.IsDisposed && c.IsHandleCreated)
                Native.Send(c.Handle, Native.WM_MOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
        }

        static Cursor EdgeCursor(int edge)
        {
            switch (edge)
            {
                case Native.HTLEFT: case Native.HTRIGHT: return Cursors.SizeWE;
                case Native.HTTOP: case Native.HTBOTTOM: return Cursors.SizeNS;
                case Native.HTTOPLEFT: case Native.HTBOTTOMRIGHT: return Cursors.SizeNWSE;
                case Native.HTTOPRIGHT: case Native.HTBOTTOMLEFT: return Cursors.SizeNESW;
                default: return null;
            }
        }
    }
}
