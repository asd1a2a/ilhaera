using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TodoWidget.Core;

namespace TodoWidget.UI
{
    /// <summary>
    /// 일력(日曆)처럼 큰 날짜 숫자 + 요일, 오늘 진행률, 다음 일정 카운트다운.
    /// </summary>
    sealed class HeaderView : Control
    {
        readonly Font dayFont = Theme.Num(27f, true);
        readonly Font monthFont = Theme.Font(9f);
        readonly Font weekdayFont = Theme.Font(10.5f, FontStyle.Bold);
        readonly Font progressFont = Theme.Font(8.5f);
        readonly Font nextLabelFont = Theme.Font(8.5f);
        readonly Font nextTimeFont = Theme.Num(11f, true);
        readonly Font nextTextFont = Theme.Font(9.5f);
        readonly Font nextRelFont = Theme.Font(8.5f);

        public readonly FlatButton MenuButton;
        public readonly FlatButton HideButton;

        IList<TodoItem> items = new List<TodoItem>();
        TodoItem next;
        bool hoverStrip;
        Rectangle stripRect;
        int reminderMinutes = 30;

        public event Action<TodoItem> NextClicked;
        public event MouseEventHandler DragStart;

        static int Px(float v) { return Theme.Px(v); }

        public HeaderView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Back;
            MenuButton = new FlatButton { Glyph = Glyph.More, Size = new Size(Px(28), Px(28)) };
            HideButton = new FlatButton { Glyph = Glyph.Minimize, Size = new Size(Px(28), Px(28)) };
            Controls.Add(MenuButton);
            Controls.Add(HideButton);
        }

        int TopRowHeight { get { return Px(50); } }
        int ProgressHeight { get { return Px(20); } }
        int StripHeight { get { return Px(40); } }

        public int PreferredHeight
        {
            get { return TopRowHeight + ProgressHeight + (next != null ? Px(8) + StripHeight : 0); }
        }

        public void SetData(IList<TodoItem> list, int leadMinutes)
        {
            items = list;
            reminderMinutes = leadMinutes;
            next = Reminders.NextUp(items, DateTime.Now);
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (HideButton == null) return;
            int bw = HideButton.Width;
            HideButton.Location = new Point(Width - bw, Px(6));
            MenuButton.Location = new Point(HideButton.Left - bw - Px(2), Px(6));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var now = DateTime.Now;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            // ── 일력: 큰 날짜 + 월 / 요일 ──
            string day = now.Day.ToString();
            var daySize = TextRenderer.MeasureText(g, day, dayFont, new Size(int.MaxValue, int.MaxValue), flags);
            int dayY = (TopRowHeight - daySize.Height) / 2;
            TextRenderer.DrawText(g, day, dayFont, new Point(0, dayY), Theme.WeekdayColor(now), flags);

            int sx = daySize.Width + Px(9);
            string month = now.Month + "월";
            string weekday = TimeText.DayName(now) + "요일";
            var mSize = TextRenderer.MeasureText(g, month, monthFont, new Size(int.MaxValue, int.MaxValue), flags);
            var wSize = TextRenderer.MeasureText(g, weekday, weekdayFont, new Size(int.MaxValue, int.MaxValue), flags);
            int stackH = mSize.Height + wSize.Height;
            int sy = (TopRowHeight - stackH) / 2 + Px(1);
            TextRenderer.DrawText(g, month, monthFont, new Point(sx, sy), Theme.SubText, flags);
            TextRenderer.DrawText(g, weekday, weekdayFont, new Point(sx, sy + mSize.Height), Theme.WeekdayColor(now), flags);

            // ── 오늘 진행률 ──
            int py = TopRowHeight;
            // 반복 일정은 오늘 회차를 끝내면 다음 회차로 넘어가므로 "오늘 완료"로 함께 센다.
            Func<TodoItem, bool> doneToday = i => i.Done ||
                (i.IsRecurring && i.LastDone.HasValue && i.LastDone.Value.Date == now.Date && i.Due.Value.Date != now.Date);
            var todays = items.Where(i => i.Due.HasValue && (i.Due.Value.Date == now.Date || doneToday(i) && !i.Done)).ToList();
            int open = items.Count(i => !i.Done);
            string label;
            float ratio = -1f;
            if (todays.Count > 0)
            {
                int doneCount = todays.Count(doneToday);
                label = "오늘 " + doneCount + "/" + todays.Count;
                ratio = (float)doneCount / todays.Count;
            }
            else if (items.Count > 0)
            {
                label = open > 0 ? "남은 일 " + open + "개" : "모두 끝냈어요";
            }
            else label = "";

            var lSize = TextRenderer.MeasureText(g, label, progressFont, new Size(int.MaxValue, int.MaxValue), flags);
            int ly = py + (ProgressHeight - lSize.Height) / 2;
            TextRenderer.DrawText(g, label, progressFont, new Point(0, ly), Theme.SubText, flags);
            if (ratio >= 0)
            {
                int bx = lSize.Width + Px(10);
                int bh = Px(4);
                var track = new RectangleF(bx, py + (ProgressHeight - bh) / 2f, Width - bx, bh);
                Theme.FillRound(g, Theme.Surface, track, bh / 2f);
                if (ratio > 0)
                    Theme.FillRound(g, Theme.Accent, new RectangleF(track.X, track.Y, Math.Max(bh, track.Width * ratio), bh), bh / 2f);
            }

            // ── 다음 일정 ──
            stripRect = Rectangle.Empty;
            if (next == null) return;
            var due = next.Due.Value;
            bool soon = TimeText.State(next, now, reminderMinutes) == DueState.Soon;
            stripRect = new Rectangle(0, py + ProgressHeight + Px(8), Width, StripHeight);
            var fill = soon ? Theme.Marker : (hoverStrip ? Theme.Pressed : Theme.Surface);
            Theme.FillRound(g, fill, stripRect, Px(9));

            int x = stripRect.X + Px(12);
            int cy = stripRect.Y + stripRect.Height / 2;
            var textColor = soon ? Theme.MarkerText : Theme.Text;
            var subColor = soon ? Theme.MarkerText : Theme.SubText;

            string nextLabel = "다음";
            var nl = TextRenderer.MeasureText(g, nextLabel, nextLabelFont, new Size(int.MaxValue, int.MaxValue), flags);
            TextRenderer.DrawText(g, nextLabel, nextLabelFont, new Point(x, cy - nl.Height / 2), subColor, flags);
            x += nl.Width + Px(8);

            string time = (due.Date == now.Date ? "" : (TimeText.DayWord(due, now) ?? (due.Month + "/" + due.Day)) + " ") + TimeText.HourMinute(due);
            var ts = TextRenderer.MeasureText(g, time, nextTimeFont, new Size(int.MaxValue, int.MaxValue), flags);
            TextRenderer.DrawText(g, time, nextTimeFont, new Point(x, cy - ts.Height / 2), textColor, flags);
            x += ts.Width + Px(8);

            string rel = TimeText.Relative(due, now);
            var rs = TextRenderer.MeasureText(g, rel, nextRelFont, new Size(int.MaxValue, int.MaxValue), flags);
            int rx = stripRect.Right - Px(12) - rs.Width;
            TextRenderer.DrawText(g, rel, nextRelFont, new Point(rx, cy - rs.Height / 2), soon ? Theme.MarkerText : Theme.SubText, flags);

            var titleRect = new Rectangle(x, stripRect.Y, Math.Max(0, rx - Px(8) - x), stripRect.Height);
            TextRenderer.DrawText(g, (next.Text ?? "").Replace("\r", " ").Replace("\n", " "), nextTextFont, titleRect, textColor,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = stripRect.Contains(e.Location);
            if (h != hoverStrip)
            {
                hoverStrip = h;
                Cursor = h ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (WidgetForm.StillOver(this)) return;
            if (hoverStrip) { hoverStrip = false; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (stripRect.Contains(e.Location))
            {
                if (next != null && NextClicked != null) NextClicked(next);
                return;
            }
            if (DragStart != null) DragStart(this, e);
        }
    }

    /// <summary>입력칸 아래: "내일 15:00 일정으로 추가" 미리보기.</summary>
    sealed class ParseChip : Control
    {
        readonly Font font = Theme.Font(8.5f);
        readonly Font numFont = Theme.Num(9.5f, true);
        public readonly FlatButton ClearButton;
        DateTime due;
        int lead = 30;
        Repeat repeat;

        public ParseChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Back;
            ClearButton = new FlatButton { Glyph = Glyph.Close, Size = new Size(Theme.Px(22), Theme.Px(22)) };
            Controls.Add(ClearButton);
            Height = Theme.Px(26);
        }

        public void SetValue(DateTime value, int leadMinutes, Repeat rep)
        {
            due = value;
            lead = leadMinutes;
            repeat = rep;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (ClearButton != null) ClearButton.Location = new Point(Width - ClearButton.Width, (Height - ClearButton.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var now = DateTime.Now;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int x = Theme.Px(4), cy = Height / 2;
            int icon = Theme.Px(16);
            FlatButton.DrawGlyph(g, Glyph.Clock, new Rectangle(x, cy - icon / 2, icon, icon), Theme.Accent);
            x += icon + Theme.Px(4);

            string when = (repeat != Repeat.None ? Recurrence.Label(repeat, due) : (TimeText.DayWord(due, now) ?? TimeText.ShortDate(due, now)))
                          + " " + TimeText.HourMinute(due);
            var ws = TextRenderer.MeasureText(g, when, numFont, new Size(int.MaxValue, int.MaxValue), flags);
            TextRenderer.DrawText(g, when, numFont, new Point(x, cy - ws.Height / 2), Theme.Accent, flags);
            x += ws.Width + Theme.Px(6);

            string leadText = lead % 60 == 0 ? (lead / 60) + "시간" : lead + "분";
            string rest = repeat != Repeat.None
                ? "반복 일정으로 추가, 첫 회 " + (TimeText.DayWord(due, now) ?? TimeText.ShortDate(due, now))
                : "일정으로 추가, " + leadText + " 전에 알려요";
            TextRenderer.DrawText(g, rest, font, new Rectangle(x, 0, Math.Max(0, ClearButton.Left - x - Theme.Px(4)), Height), Theme.SubText,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
