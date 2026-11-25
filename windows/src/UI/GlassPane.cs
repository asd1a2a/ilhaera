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
        /// <summary>유리를 그리는 방식. 좋은 것부터 쓰고, 이 PC 에서 불투명하게만 나오면 다음 것으로 내려간다.</summary>
        enum GlassMode
        {
            Acrylic, // 짙은 흐림
            Blur,    // 가벼운 흐림
            Plain,   // 흐림 없이 색을 입힌 반투명
        }

        // 이 PC 에서 되는 가장 좋은 방식. 처음 한 번 재 보고(Probe) 그 뒤로는 그대로 쓴다.
        static GlassMode best = GlassMode.Acrylic;
        static bool probed;
        static readonly GlassMode? forced = ForcedMode();

        readonly WidgetForm widget;
        readonly bool dark;
        readonly Timer probeTimer;
        ProbeDot probe;
        int probeStep;
        GlassMode mode;
        int tint;
        bool moving;
        Control lastTarget;

        /// <summary>확인용: 이 창을 거쳐 위젯으로 넘긴 마우스 누름 횟수.</summary>
        public static int ForwardedClicks;

        public GlassPane(WidgetForm widget, bool dark, int tintPercent)
        {
            this.widget = widget;
            this.dark = dark;
            tint = tintPercent;
            mode = forced ?? best;
            probeTimer = new Timer { Interval = 350 };
            probeTimer.Tick += (s, e) => ProbeTick();
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

        /// <summary>위젯을 끌거나 늘리는 동안.</summary>
        public void SetMoving(bool value)
        {
            if (moving == value) return;
            moving = value;
            if (IsHandleCreated && mode == GlassMode.Acrylic && Native.Build < 22000) ApplyGlass();
        }

        /// <summary>자동 확인용: 지금 쓰는 방식.</summary>
        public string ModeName { get { return mode + (probed ? "" : "?"); } }

        void ApplyGlass()
        {
            int percent = Math.Max(0, Math.Min(100, tint));
            int rgb = TintRgb;
            if (mode != GlassMode.Plain)
            {
                // Windows 10 의 아크릴은 창을 옮길 때 심하게 끊기므로 옮기는 동안만 가벼운 흐림으로 바꿔 둔다.
                int state = mode == GlassMode.Blur || (moving && Native.Build < 22000) ? 3 : 4;
                if (Native.SetGlass(Handle, state, rgb, (int)Math.Round(Math.Max(3, percent) * 2.55)))
                {
                    if (BackColor != Color.Black) BackColor = Color.Black;
                    if (Opacity != 1.0) Opacity = 1.0;
                    return;
                }
                mode = GlassMode.Plain;
            }
            // 흐림을 쓸 수 없는 PC: 색을 입힌 반투명 창으로 (뒤가 흐려지지는 않는다)
            Native.SetGlass(Handle, 0, 0, 0);
            BackColor = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            Opacity = 0.30 + 0.65 * percent / 100.0;
        }

        // ── 이 PC 에서 유리가 실제로 비치는지 재 보기 ──
        //
        // "투명 효과"가 꺼져 있거나 그래픽 가속이 없는 PC 에서는 Windows 가 유리 대신 불투명한 색판을 그린다.
        // 그러면 유리 테마가 그냥 흰 창이 되므로, 작은 점 하나를 띄워 실제 화면에 어떻게 나오는지 보고 방식을 고른다.

        static GlassMode? ForcedMode()
        {
            // 자동 확인(CI)에서 방식을 하나로 못 박을 때
            switch (Environment.GetEnvironmentVariable("ILHAERA_GLASS_MODE"))
            {
                case "acrylic": return GlassMode.Acrylic;
                case "blur": return GlassMode.Blur;
                case "plain": return GlassMode.Plain;
                default: return null;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !probed && forced == null && probe == null && mode != GlassMode.Plain)
            {
                probeStep = 0;
                probeTimer.Start();
            }
        }

        void ProbeTick()
        {
            if (IsDisposed || !Visible) { EndProbe(); return; }
            if (probeStep == 0)
            {
                // 아크릴부터: 거의 투명한 자홍색을 입힌다. 유리가 되면 뒤 화면이 보이고, 안 되면 자홍색 점이 된다.
                probe = new ProbeDot(new Point(Left + Width / 2, Top + 1), 4);
                probe.Show();
                probeStep = 1;
                return;
            }
            bool solid = probe.LooksSolid();
            if (probeStep == 1 && solid)
            {
                probe.Use(3);
                probeStep = 2;
                return;
            }
            best = probeStep == 1 ? GlassMode.Acrylic : solid ? GlassMode.Plain : GlassMode.Blur;
            probed = true;
            EndProbe();
            if (mode != best)
            {
                mode = best;
                ApplyGlass();
            }
        }

        void EndProbe()
        {
            probeTimer.Stop();
            if (probe != null) { probe.Dispose(); probe = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                EndProbe();
                probeTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>유리가 되는지 재 보는 작은 점 (잠깐 떴다 사라진다).</summary>
        sealed class ProbeDot : Form
        {
            const int Magenta = 0xFF00FF;

            public ProbeDot(Point location, int state)
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                ControlBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Black;
                TopMost = true;
                Bounds = new Rectangle(location.X, location.Y, 6, 6);
                Use(state);
            }

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

            public void Use(int state) { Native.SetGlass(Handle, state, Magenta, 1); }

            /// <summary>화면에 불투명한 자홍색으로 나왔으면 유리가 안 되는 것.</summary>
            public bool LooksSolid()
            {
                try
                {
                    using (var bmp = new Bitmap(1, 1))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(Left + Width / 2, Top + Height / 2, 0, 0, bmp.Size);
                        var c = bmp.GetPixel(0, 0);
                        return c.R >= 245 && c.G <= 10 && c.B >= 245;
                    }
                }
                catch (Exception) { return false; }
            }
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
