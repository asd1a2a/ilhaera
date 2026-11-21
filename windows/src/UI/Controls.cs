using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TodoWidget.Core;

namespace TodoWidget.UI
{
    enum Glyph { None, More, Minimize, Clock, Plus, Close, Bell, Calendar, Repeat, Up, Down, Left, Right }

    /// <summary>아이콘/텍스트를 직접 그리는 납작한 버튼.</summary>
    sealed class FlatButton : Control
    {
        bool hover, pressed;
        public Glyph Glyph { get; set; }
        public bool Primary { get; set; }      // 강조색 채움 버튼
        public bool Active { get; set; }       // 토글 상태
        public float Radius { get; set; }

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            ForeColor = Theme.SubText;
            BackColor = Theme.Back;
            Radius = Theme.Px(6);
            Font = Theme.Font(9f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);

            Color fg;
            if (Primary)
            {
                Theme.FillRound(g, pressed ? Theme.Accent : hover ? Theme.AccentHover : Theme.Accent, r, Radius);
                fg = Theme.OnAccent;
            }
            else
            {
                if (pressed) Theme.FillRound(g, Theme.Pressed, r, Radius);
                else if (hover || Active) Theme.FillRound(g, Theme.Hover, r, Radius);
                fg = Active ? Theme.Accent : hover ? Theme.Text : ForeColor;
            }

            if (Glyph != Glyph.None)
                DrawGlyph(g, Glyph, ClientRectangle, fg);
            else
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        public static void DrawGlyph(Graphics g, Glyph glyph, Rectangle bounds, Color color)
        {
            float s = Math.Min(bounds.Width, bounds.Height) * 0.5f;
            float cx = bounds.X + bounds.Width / 2f, cy = bounds.Y + bounds.Height / 2f;
            float w = Math.Max(1.5f, Theme.Scale * 1.6f);
            using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case Glyph.More:
                        float dot = Math.Max(3f, s * 0.22f), gap = s * 0.5f;
                        for (int i = -1; i <= 1; i++)
                            g.FillEllipse(brush, cx + i * gap - dot / 2, cy - dot / 2, dot, dot);
                        break;
                    case Glyph.Minimize:
                        g.DrawLine(pen, cx - s * 0.45f, cy, cx + s * 0.45f, cy);
                        break;
                    case Glyph.Plus:
                        g.DrawLine(pen, cx - s * 0.45f, cy, cx + s * 0.45f, cy);
                        g.DrawLine(pen, cx, cy - s * 0.45f, cx, cy + s * 0.45f);
                        break;
                    case Glyph.Close:
                        g.DrawLine(pen, cx - s * 0.32f, cy - s * 0.32f, cx + s * 0.32f, cy + s * 0.32f);
                        g.DrawLine(pen, cx + s * 0.32f, cy - s * 0.32f, cx - s * 0.32f, cy + s * 0.32f);
                        break;
                    case Glyph.Up:
                    case Glyph.Down:
                    case Glyph.Left:
                    case Glyph.Right:
                    {
                        float a = s * 0.32f;
                        PointF[] pts;
                        if (glyph == Glyph.Up) pts = new[] { new PointF(cx - a, cy + a / 2), new PointF(cx, cy - a / 2), new PointF(cx + a, cy + a / 2) };
                        else if (glyph == Glyph.Down) pts = new[] { new PointF(cx - a, cy - a / 2), new PointF(cx, cy + a / 2), new PointF(cx + a, cy - a / 2) };
                        else if (glyph == Glyph.Left) pts = new[] { new PointF(cx + a / 2, cy - a), new PointF(cx - a / 2, cy), new PointF(cx + a / 2, cy + a) };
                        else pts = new[] { new PointF(cx - a / 2, cy - a), new PointF(cx + a / 2, cy), new PointF(cx - a / 2, cy + a) };
                        pen.LineJoin = LineJoin.Round;
                        g.DrawLines(pen, pts);
                        break;
                    }
                    case Glyph.Bell:
                    {
                        float bw = s * 0.42f, top = cy - s * 0.5f, bottom = cy + s * 0.3f;
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(cx - bw, top, bw * 2, bw * 2, 180, 180);
                            path.AddLine(cx + bw, top + bw, cx + bw * 1.1f, bottom);
                            path.AddLine(cx - bw * 1.1f, bottom, cx - bw, top + bw);
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        g.DrawLine(pen, cx - bw * 0.35f, bottom + s * 0.18f, cx + bw * 0.35f, bottom + s * 0.18f);
                        break;
                    }
                    case Glyph.Calendar:
                    {
                        float cw = s * 0.62f, ch = s * 0.56f;
                        g.DrawRectangle(pen, cx - cw, cy - ch + s * 0.1f, cw * 2, ch * 2 - s * 0.1f);
                        g.DrawLine(pen, cx - cw, cy - ch + s * 0.45f, cx + cw, cy - ch + s * 0.45f);
                        g.DrawLine(pen, cx - cw * 0.5f, cy - ch - s * 0.1f, cx - cw * 0.5f, cy - ch + s * 0.2f);
                        g.DrawLine(pen, cx + cw * 0.5f, cy - ch - s * 0.1f, cx + cw * 0.5f, cy - ch + s * 0.2f);
                        break;
                    }
                    case Glyph.Repeat:
                    {
                        float rq = s * 0.5f;
                        g.DrawArc(pen, cx - rq, cy - rq, rq * 2, rq * 2, 200, 250);
                        float ax = cx - rq * 0.94f, ay = cy - rq * 0.34f;
                        g.DrawLine(pen, ax, ay, ax - rq * 0.1f, ay - rq * 0.55f);
                        g.DrawLine(pen, ax, ay, ax + rq * 0.5f, ay - rq * 0.2f);
                        break;
                    }
                    case Glyph.Clock:
                        float rr = s * 0.55f;
                        g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                        g.DrawLine(pen, cx, cy, cx, cy - rr * 0.6f);
                        g.DrawLine(pen, cx, cy, cx + rr * 0.45f, cy + rr * 0.2f);
                        break;
                }
            }
        }
    }

    /// <summary>둥근 배경 패널.</summary>
    sealed class RoundPanel : Panel
    {
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public float Radius { get; set; }

        public RoundPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FillColor = Theme.Surface;
            BorderColor = Color.Empty;
            Radius = Theme.Px(8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, FillColor, r, Radius);
            if (!BorderColor.IsEmpty) Theme.DrawRound(g, BorderColor, r, Radius);
        }
    }
}
