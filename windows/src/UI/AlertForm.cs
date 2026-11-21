using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TodoWidget.Core;

namespace TodoWidget.UI
{
    /// <summary>화면 오른쪽 아래에 뜨는 알림 창. 확인이나 다시 알림을 누를 때까지 남아 있다.</summary>
    sealed class AlertForm : Form
    {
        const int MaxLines = 4;
        public const int SnoozeMinutes = 10;
        static readonly List<AlertForm> OpenForms = new List<AlertForm>();

        sealed class Line
        {
            public string Time, Title, Sub;
            public Color Accent;
            public bool Highlight;
            public int Height, TitleH;
        }

        readonly List<Line> lines = new List<Line>();
        readonly List<TodoItem> alertItems;
        readonly string title, more;
        readonly Font titleFont = Theme.Font(10f, FontStyle.Bold);
        readonly Font timeFont = Theme.Num(15f, true);
        readonly Font itemFont = Theme.Font(10f, FontStyle.Bold);
        readonly Font subFont = Theme.Font(8.5f);
        readonly FlatButton closeButton, okButton, snoozeButton;
        readonly int bodyTop, timeColW, textX, textW;

        public event Action OpenRequested;
        public event Action<IList<TodoItem>> SnoozeRequested;

        static int Px(float v) { return Theme.Px(v); }

        public AlertForm(IList<Alert> alerts, DateTime now)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            Text = "할 일 알림";
            DoubleBuffered = true;
            alertItems = alerts.Select(a => a.Item).ToList();

            bool allUpcoming = alerts.All(a => a.Kind == AlertKind.Upcoming);
            bool allMissed = alerts.All(a => a.Kind == AlertKind.Missed);
            title = allUpcoming ? "곧 시작해요" : allMissed ? "시간이 지난 일정이 있어요" : "할 일 알림";

            int width = Px(340);
            int pad = Px(18);
            timeColW = TextRenderer.MeasureText("00:00", timeFont).Width + Px(6);
            textX = pad + timeColW + Px(8);
            textW = width - textX - pad;
            bodyTop = Px(48);

            foreach (var a in alerts.Take(MaxLines))
            {
                var due = a.Item.Due ?? now;
                var line = new Line
                {
                    Time = TimeText.HourMinute(due),
                    Title = (a.Item.Text ?? "").Replace("\r", " ").Replace("\n", " "),
                };
                if (a.Kind == AlertKind.Upcoming)
                {
                    line.Sub = a.MinutesLeft + "분 후 시작";
                    line.Accent = Theme.MarkerText;
                    line.Highlight = true;
                }
                else
                {
                    var word = TimeText.DayWord(due, now);
                    line.Sub = (word != null && word != "오늘" ? word + " " : "") + "시간이 지났어요";
                    line.Accent = Theme.Danger;
                }
                line.TitleH = Math.Min(
                    TextRenderer.MeasureText(line.Title, itemFont, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height,
                    itemFont.Height * 2 + Px(2));
                line.Height = Math.Max(timeFont.Height + Px(4), line.TitleH + subFont.Height + Px(2));
                lines.Add(line);
            }
            if (alerts.Count > MaxLines) more = "외 " + (alerts.Count - MaxLines) + "개 더 있어요";

            int y = bodyTop;
            foreach (var l in lines) y += l.Height + Px(12);
            if (more != null) y += subFont.Height + Px(8);
            int height = y + Px(46);

            closeButton = new FlatButton { Glyph = Glyph.Close, BackColor = Theme.Back, Bounds = new Rectangle(width - Px(38), Px(12), Px(26), Px(26)) };
            okButton = new FlatButton { Text = "확인", Primary = true, ForeColor = Theme.OnAccent, Font = Theme.Font(9.5f, FontStyle.Bold), Bounds = new Rectangle(width - pad - Px(72), height - Px(46), Px(72), Px(32)) };
            snoozeButton = new FlatButton { Text = SnoozeMinutes + "분 뒤 다시 알림", ForeColor = Theme.SubText, BackColor = Theme.Back, Font = Theme.Font(9f), Bounds = new Rectangle(okButton.Left - Px(126), okButton.Top, Px(120), Px(32)) };
            closeButton.Click += (s, e) => Close();
            okButton.Click += (s, e) => Close();
            snoozeButton.Click += (s, e) => { if (SnoozeRequested != null) SnoozeRequested(alertItems); Close(); };
            Controls.AddRange(new Control[] { closeButton, okButton, snoozeButton });

            ClientSize = new Size(width, height);
            PlaceOnScreen();
        }

        void PlaceOnScreen()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int bottom = wa.Bottom - Px(16);
            foreach (var f in OpenForms)
                if (!f.IsDisposed) bottom = Math.Min(bottom, f.Top - Px(10));
            if (bottom - Height < wa.Top) bottom = wa.Bottom - Px(16);
            Location = new Point(wa.Right - Width - Px(16), bottom - Height);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOPMOST;
                if (Native.IsWindows) cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OpenForms.Add(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OpenForms.Remove(this);
            base.OnFormClosed(e);
        }

        public static void CloseAll()
        {
            foreach (var f in OpenForms.ToList())
                if (!f.IsDisposed) f.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Back);
            if (!(Native.IsWindows && Environment.OSVersion.Version.Build >= 22000))
                using (var pen = new Pen(Theme.Border))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int pad = Px(18);
            FlatButton.DrawGlyph(g, Glyph.Clock, new Rectangle(pad - Px(3), Px(14), Px(20), Px(20)), Theme.Accent);
            TextRenderer.DrawText(g, title, titleFont, new Point(pad + Px(20), Px(16)), Theme.Text, flags);

            int y = bodyTop;
            foreach (var l in lines)
            {
                var ts = TextRenderer.MeasureText(g, l.Time, timeFont, new Size(int.MaxValue, int.MaxValue), flags);
                var timeRect = new Rectangle(pad, y, ts.Width, ts.Height);
                if (l.Highlight)
                    Theme.FillRound(g, Theme.Marker, new RectangleF(timeRect.X - Px(4), timeRect.Y + ts.Height * 0.15f, ts.Width + Px(8), ts.Height * 0.8f), Px(4));
                TextRenderer.DrawText(g, l.Time, timeFont, timeRect.Location, l.Highlight ? Theme.MarkerText : Theme.Danger, flags);

                TextRenderer.DrawText(g, l.Title, itemFont, new Rectangle(textX, y + Px(1), textW, l.TitleH), Theme.Text,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, l.Sub, subFont, new Point(textX, y + Px(1) + l.TitleH + Px(1)),
                    l.Highlight ? Theme.Warn : Theme.Danger, flags);
                y += l.Height + Px(12);
            }
            if (more != null)
                TextRenderer.DrawText(g, more, subFont, new Point(textX, y), Theme.SubText, flags);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && e.Y > bodyTop - Px(6) && e.Y < okButton.Top - Px(4))
            {
                if (OpenRequested != null) OpenRequested();
            }
        }
    }
}
