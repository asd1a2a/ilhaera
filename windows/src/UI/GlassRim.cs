using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TodoWidget.UI
{
    /// <summary>
    /// 리퀴드 글래스 테마에서 위젯 맨 위에 얹는 "유리 가장자리".
    /// 두꺼운 유리는 가장자리에서 빛이 꺾여 밝은 테와 무지갯빛 번짐이 생긴다. 그 빛을 점마다 투명도가 다른 창에 그려 얹는다.
    /// 마우스는 전혀 받지 않고 아래 창으로 그대로 지나간다. 위치·크기는 유리판(GlassPane)을 따라간다.
    /// </summary>
    sealed class GlassRim : Form
    {
        const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

        readonly bool dark;
        Size drawn;

        public GlassRim(bool dark)
        {
            this.dark = dark;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "일해라 유리 가장자리";
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (Native.IsWindows)
                    cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        /// <summary>유리판 자리에 맞춰 놓고, 크기가 바뀌었으면 다시 그린다.</summary>
        public void Follow(Rectangle bounds)
        {
            if (!Native.IsWindows || bounds.Width < 8 || bounds.Height < 8) return;
            if (Bounds != bounds) Bounds = bounds;
            if (IsHandleCreated && drawn != bounds.Size) Render();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            drawn = Size.Empty;
            if (Width >= 8 && Height >= 8) Render();
        }

        void Render()
        {
            try
            {
                using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(bmp)) Draw(g, Width, Height);
                    Push(bmp);
                }
                drawn = Size;
            }
            catch (Exception) { }
        }

        /// <summary>빛이 왼쪽 위에서 들어와 오른쪽 아래로 빠져나가는 유리 가장자리.</summary>
        void Draw(Graphics g, int w, int h)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float radius = Native.Build >= 22000 ? Theme.Px(8) : 0; // Windows 11 은 창 모서리가 둥글다
            float k = dark ? 0.62f : 1f;                           // 어두운 유리는 빛이 덜 돈다
            var box = new RectangleF(0, 0, w, h);

            // 1) 두께감: 가장자리로 갈수록 밝아지는 띠 (빛이 꺾여 모이는 곳)
            int band = Math.Max(6, Theme.Px(13));
            for (int i = band; i >= 1; i--)
            {
                float t = 1f - (float)i / band;            // 0 안쪽 → 1 가장자리
                int a = (int)(54 * k * t * t);
                if (a <= 0) continue;
                Stroke(g, box, i, radius, 1.6f,
                       Color.FromArgb(a, 255, 255, 255), Color.FromArgb(a / 7, 255, 255, 255), Color.FromArgb((int)(a * 0.62f), 255, 255, 255));
            }

            // 2) 무지갯빛 번짐: 빛이 꺾이면서 색이 갈라진다. 들어오는 쪽은 푸른빛, 나가는 쪽은 붉은빛
            Stroke(g, box, 2.6f, radius, 1.2f,
                   Color.FromArgb((int)(120 * k), 110, 205, 255), Color.FromArgb(0, 110, 205, 255), Color.FromArgb((int)(26 * k), 110, 205, 255));
            Stroke(g, box, 3.8f, radius, 1.2f,
                   Color.FromArgb((int)(22 * k), 255, 150, 120), Color.FromArgb(0, 255, 150, 120), Color.FromArgb((int)(105 * k), 255, 150, 120));

            // 3) 맨 바깥의 또렷한 빛 테
            Stroke(g, box, 0.75f, radius, 1.5f,
                   Color.FromArgb((int)(235 * k), 255, 255, 255), Color.FromArgb((int)(34 * k), 255, 255, 255), Color.FromArgb((int)(150 * k), 255, 255, 255));

            // 4) 위쪽에 비스듬히 맺히는 반사광
            using (var path = RoundRect(box, radius))
            using (var sheen = new LinearGradientBrush(new PointF(0, 0), new PointF(w * 0.55f, h * 0.34f),
                       Color.FromArgb((int)(30 * k), 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
            {
                sheen.WrapMode = WrapMode.TileFlipXY;
                var old = g.Clip;
                g.SetClip(new RectangleF(0, 0, w * 0.55f, h * 0.34f));
                g.FillPath(sheen, path);
                g.Clip = old;
            }
        }

        /// <summary>안으로 inset 만큼 들어간 둥근 테를 왼쪽 위 → 가운데 → 오른쪽 아래 색으로 긋는다.</summary>
        static void Stroke(Graphics g, RectangleF box, float inset, float radius, float width, Color from, Color mid, Color to)
        {
            var r = RectangleF.Inflate(box, -inset, -inset);
            if (r.Width < 4 || r.Height < 4) return;
            using (var path = RoundRect(r, Math.Max(0, radius - inset)))
            using (var brush = new LinearGradientBrush(new PointF(box.Left, box.Top), new PointF(box.Right, box.Bottom), from, to))
            {
                brush.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { from, mid, mid, to },
                    Positions = new[] { 0f, 0.42f, 0.62f, 1f },
                };
                using (var pen = new Pen(brush, width)) g.DrawPath(pen, path);
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            if (radius < 0.5f) { path.AddRectangle(r); return path; }
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>그린 그림을 점마다 다른 투명도 그대로 창에 올린다.</summary>
        void Push(Bitmap bmp)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hbmp);
                var pos = new POINT { X = Left, Y = Top };
                var size = new SIZE { W = bmp.Width, H = bmp.Height };
                var src = new POINT();
                var blend = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = 255, Format = 1 }; // AC_SRC_OVER, AC_SRC_ALPHA
                UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2); // ULW_ALPHA
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
