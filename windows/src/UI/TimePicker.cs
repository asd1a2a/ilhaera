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
    /// 날짜 칩(오늘/내일/모레/달력) + 휴대폰 알람식 시간(오전·오후, 시, 분) + 빠른 선택 + 반복 칩 + 알림 시각 요약.
    /// 마우스 휠·▲▼·키보드(숫자, ↑↓←→, Enter, Esc) 모두 지원.
    /// </summary>
    sealed class TimePicker : Control
    {
        enum Seg { None, AmPm, Hour, Minute }
        enum ChipKind { Date, Calendar, Preset, Repeat }

        sealed class Chip
        {
            public ChipKind Kind;
            public string Text;
            public Glyph Icon;
            public int Value;
            public Rectangle Rect;
        }

        readonly Font chipFont = Theme.Font(9f);
        readonly Font chipBold = Theme.Font(9f, FontStyle.Bold);
        readonly Font digitFont = Theme.Num(22f, true);
        readonly Font ampmFont = Theme.Font(9f, FontStyle.Bold);
        readonly Font summaryFont = Theme.Font(8.5f);

        DateTime date;
        int hour, minute;
        Repeat repeat;
        int lead = 30;

        readonly List<Chip> chips = new List<Chip>();
        Rectangle amRect, pmRect, hourRect, minuteRect, colonRect;
        Rectangle hourUp, hourDown, minuteUp, minuteDown;
        Rectangle summaryRect;
        object hover;
        Seg focusSeg = Seg.Minute;
        string typed = "";
        CalendarPopup calendar;

        public event Action ValueChanged;
        public event Action Commit;
        public event Action Cancel;

        static int Px(float v) { return Theme.Px(v); }
        static readonly TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        public TimePicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Back;
            TabStop = true;
            var now = DateTime.Now;
            SetValue(TimeText.DefaultDue(now), Repeat.None);
        }

        int ChipH { get { return Px(28); } }
        int TimeRowH { get { return Px(64); } }
        public int PreferredHeight { get { return ChipH + Px(8) + TimeRowH + Px(6) + ChipH + Px(10) + ChipH + Px(12) + Px(18); } }

        public int ReminderMinutes { get { return lead; } set { lead = value; Invalidate(); } }

        public void SetValue(DateTime value, Repeat rep)
        {
            date = value.Date;
            hour = value.Hour;
            minute = value.Minute;
            repeat = rep;
            typed = "";
            Relayout();
            Invalidate();
        }

        /// <summary>선택한 값. 반복이면 첫 회차로 맞춘다.</summary>
        public DateTime GetValue(out Repeat rep)
        {
            rep = repeat;
            var due = date.AddHours(hour).AddMinutes(minute);
            if (repeat != Repeat.None)
            {
                due = Recurrence.FirstOnOrAfter(repeat, due, due.DayOfWeek);
                if (due <= DateTime.Now) due = Recurrence.NextAfter(repeat, due);
            }
            return due;
        }

        void Changed()
        {
            typed = "";
            Relayout();
            Invalidate();
            if (ValueChanged != null) ValueChanged();
        }

        // ── 배치 ──

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        void Relayout()
        {
            chips.Clear();
            var now = DateTime.Now;
            int y = 0, gap = Px(6);

            // 날짜
            int x = 0;
            for (int d = 0; d < 3; d++)
                x = AddChip(ChipKind.Date, TimeText.DayWord(now.Date.AddDays(d), now), d, Glyph.None, x, y) + gap;
            int offset = (date - now.Date).Days;
            string calText = offset >= 0 && offset <= 2 ? "날짜 고르기" : TimeText.ShortDate(date, now);
            AddChip(ChipKind.Calendar, calText, 0, Glyph.Calendar, x, y);
            y += ChipH + Px(8);

            // 시간: [오전|오후]  3 : 00
            int ampmW = Px(46), ampmH = Px(30);
            int ty = y + (TimeRowH - ampmH) / 2;
            amRect = new Rectangle(0, ty, ampmW, ampmH);
            pmRect = new Rectangle(ampmW, ty, ampmW, ampmH);
            int digitW = TextRenderer.MeasureText("00", digitFont, new Size(int.MaxValue, int.MaxValue), Flags).Width + Px(16);
            int digitH = TextRenderer.MeasureText("00", digitFont, new Size(int.MaxValue, int.MaxValue), Flags).Height;
            int arrowH = Px(14);
            int dx = pmRect.Right + Px(18);
            int dy = y + (TimeRowH - digitH) / 2;
            hourRect = new Rectangle(dx, dy, digitW, digitH);
            colonRect = new Rectangle(hourRect.Right, dy, Px(12), digitH);
            minuteRect = new Rectangle(colonRect.Right, dy, digitW, digitH);
            hourUp = new Rectangle(hourRect.X, y, digitW, arrowH);
            hourDown = new Rectangle(hourRect.X, y + TimeRowH - arrowH, digitW, arrowH);
            minuteUp = new Rectangle(minuteRect.X, y, digitW, arrowH);
            minuteDown = new Rectangle(minuteRect.X, y + TimeRowH - arrowH, digitW, arrowH);
            y += TimeRowH + Px(6);

            // 빠른 선택
            x = 0;
            x = AddChip(ChipKind.Preset, "30분 뒤", 30, Glyph.None, x, y) + gap;
            x = AddChip(ChipKind.Preset, "1시간 뒤", 60, Glyph.None, x, y) + gap;
            x = AddChip(ChipKind.Preset, "오전 9시", -9, Glyph.None, x, y) + gap;
            AddChip(ChipKind.Preset, "오후 6시", -18, Glyph.None, x, y);
            y += ChipH + Px(10);

            // 반복
            x = 0;
            x = AddChip(ChipKind.Repeat, "반복 안 함", (int)Repeat.None, Glyph.None, x, y) + gap;
            x = AddChip(ChipKind.Repeat, "매일", (int)Repeat.Daily, Glyph.Repeat, x, y) + gap;
            x = AddChip(ChipKind.Repeat, "평일", (int)Repeat.Weekdays, Glyph.Repeat, x, y) + gap;
            AddChip(ChipKind.Repeat, "매주 " + TimeText.DayName(date), (int)Repeat.Weekly, Glyph.Repeat, x, y);
            y += ChipH + Px(12);

            summaryRect = new Rectangle(0, y, Width, Px(18));
        }

        int AddChip(ChipKind kind, string text, int value, Glyph icon, int x, int y)
        {
            var size = TextRenderer.MeasureText(text, chipBold, new Size(int.MaxValue, int.MaxValue), Flags);
            int w = size.Width + Px(icon == Glyph.None ? 22 : 40);
            var chip = new Chip { Kind = kind, Text = text, Value = value, Icon = icon, Rect = new Rectangle(x, y, w, ChipH) };
            chips.Add(chip);
            return chip.Rect.Right;
        }

        bool IsSelected(Chip c)
        {
            var now = DateTime.Now;
            int offset = (date - now.Date).Days;
            switch (c.Kind)
            {
                case ChipKind.Date: return offset == c.Value;
                case ChipKind.Calendar: return offset < 0 || offset > 2;
                case ChipKind.Repeat: return (int)repeat == c.Value;
                default: return false;
            }
        }

        // ── 그리기 ──

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (var c in chips) DrawChip(g, c);

            // 오전/오후 토글
            var pill = Rectangle.Union(amRect, pmRect);
            Theme.FillRound(g, Theme.Surface, pill, pill.Height / 2f);
            var sel = hour < 12 ? amRect : pmRect;
            var selInner = Rectangle.Inflate(sel, -Px(3), -Px(3));
            Theme.FillRound(g, Theme.Raised, selInner, selInner.Height / 2f);
            if (Focused && focusSeg == Seg.AmPm) Theme.DrawRound(g, Theme.Accent, selInner, selInner.Height / 2f, Math.Max(1.5f, Theme.Scale * 1.5f));
            DrawCentered(g, "오전", ampmFont, amRect, hour < 12 ? Theme.Text : (Equals(hover, amRect) ? Theme.Text : Theme.Faint));
            DrawCentered(g, "오후", ampmFont, pmRect, hour >= 12 ? Theme.Text : (Equals(hover, pmRect) ? Theme.Text : Theme.Faint));

            // 시 : 분
            int h12 = hour % 12 == 0 ? 12 : hour % 12;
            string hText = Focused && focusSeg == Seg.Hour && typed.Length > 0 ? typed : h12.ToString();
            string mText = Focused && focusSeg == Seg.Minute && typed.Length > 0 ? typed.PadLeft(2, ' ') : minute.ToString("00");
            DrawDigit(g, hText, hourRect, Seg.Hour);
            DrawCentered(g, ":", digitFont, colonRect, Theme.Faint);
            DrawDigit(g, mText, minuteRect, Seg.Minute);
            DrawArrow(g, hourUp, Glyph.Up);
            DrawArrow(g, hourDown, Glyph.Down);
            DrawArrow(g, minuteUp, Glyph.Up);
            DrawArrow(g, minuteDown, Glyph.Down);

            // 요약: 실제로 언제 알리는지
            Color color;
            string summary = Summary(out color);
            int icon = Px(15);
            FlatButton.DrawGlyph(g, Glyph.Bell, new Rectangle(summaryRect.X, summaryRect.Y + (summaryRect.Height - icon) / 2, icon, icon), color);
            TextRenderer.DrawText(g, summary, summaryFont,
                new Rectangle(summaryRect.X + icon + Px(5), summaryRect.Y, summaryRect.Width - icon - Px(5), summaryRect.Height),
                color, Flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void DrawChip(Graphics g, Chip c)
        {
            bool selected = IsSelected(c);
            bool hovered = ReferenceEquals(hover, c);
            Color fill = selected ? Theme.Accent : hovered ? Theme.Pressed : Theme.Surface;
            Color fg = selected ? Theme.OnAccent : Theme.Text;
            Theme.FillRound(g, fill, c.Rect, c.Rect.Height / 2f);
            int x = c.Rect.X + Px(10);
            if (c.Icon != Glyph.None)
            {
                int s = Px(14);
                FlatButton.DrawGlyph(g, c.Icon, new Rectangle(x, c.Rect.Y + (c.Rect.Height - s) / 2, s, s), selected ? Theme.OnAccent : Theme.SubText);
                x += s + Px(4);
            }
            TextRenderer.DrawText(g, c.Text, selected ? chipBold : chipFont,
                new Rectangle(x, c.Rect.Y, c.Rect.Right - x - Px(8), c.Rect.Height), fg,
                Flags | TextFormatFlags.VerticalCenter);
        }

        void DrawDigit(Graphics g, string text, Rectangle r, Seg seg)
        {
            bool hovered = Equals(hover, r);
            bool focused = Focused && focusSeg == seg;
            if (hovered || focused)
                Theme.FillRound(g, focused ? Theme.Surface : Theme.Hover, Rectangle.Inflate(r, Px(2), Px(1)), Px(8));
            DrawCentered(g, text, digitFont, r, Theme.Text);
            if (focused)
            {
                int w = Px(18), h = Px(3);
                Theme.FillRound(g, Theme.Accent, new Rectangle(r.X + (r.Width - w) / 2, r.Bottom - h + Px(2), w, h), h / 2f);
            }
        }

        void DrawArrow(Graphics g, Rectangle r, Glyph glyph)
        {
            bool hovered = Equals(hover, r);
            FlatButton.DrawGlyph(g, glyph, r, hovered ? Theme.Accent : Theme.Faint);
        }

        static void DrawCentered(Graphics g, string text, Font font, Rectangle r, Color color)
        {
            TextRenderer.DrawText(g, text, font, r, color, Flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        string Summary(out Color color)
        {
            var now = DateTime.Now;
            Repeat rep;
            var due = GetValue(out rep);
            var alert = due.AddMinutes(-lead);
            color = Theme.SubText;
            if (rep == Repeat.None)
            {
                if (due <= now)
                {
                    color = Theme.Danger;
                    return "이미 지난 시간이에요. 알림 없이 추가돼요.";
                }
                if (alert <= now)
                {
                    color = Theme.Warn;
                    return Day(due, now) + " " + TimeText.Korean12(due) + " 일정, 추가하면 바로 알려요";
                }
                return Day(alert, now) + " " + TimeText.Korean12(alert) + "에 알려요 (" + LeadText() + " 전)";
            }
            string first = Day(alert > now ? alert : due, now) + " " + TimeText.Korean12(alert > now ? alert : due);
            return first + " 첫 알림, 이후 " + Recurrence.Label(rep, due) + " 반복";
        }

        static string Day(DateTime d, DateTime now)
        {
            return TimeText.DayWord(d, now) ?? TimeText.ShortDate(d, now);
        }

        string LeadText()
        {
            return lead % 60 == 0 ? (lead / 60) + "시간" : lead + "분";
        }

        // ── 값 바꾸기 ──

        void StepHour(int delta)
        {
            int baseH = hour < 12 ? 0 : 12;
            int h12 = hour % 12;
            h12 = ((h12 + delta) % 12 + 12) % 12;
            hour = baseH + h12;
            Changed();
        }

        void StepMinute(int delta)
        {
            if (delta > 0) minute = (minute / 5 + 1) * 5 % 60;
            else minute = minute % 5 != 0 ? minute - minute % 5 : (minute + 55) % 60;
            Changed();
        }

        void SetAmPm(bool pm)
        {
            if (pm && hour < 12) hour += 12;
            else if (!pm && hour >= 12) hour -= 12;
            Changed();
        }

        void StepDate(int days)
        {
            var today = DateTime.Now.Date;
            var d = date.AddDays(days);
            date = d < today ? today : d;
            Changed();
        }

        void ApplyPreset(int value)
        {
            var now = DateTime.Now;
            DateTime t;
            if (value > 0)
            {
                t = now.AddMinutes(value);
                int m = (t.Minute + 4) / 5 * 5;
                t = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0).AddMinutes(m);
            }
            else
            {
                t = date.AddHours(-value);
                if (t <= now) t = now.Date.AddHours(-value);
                if (t <= now) t = t.AddDays(1);
            }
            date = t.Date;
            hour = t.Hour;
            minute = t.Minute;
            Changed();
        }

        void ClickChip(Chip c)
        {
            switch (c.Kind)
            {
                case ChipKind.Date: date = DateTime.Now.Date.AddDays(c.Value); Changed(); break;
                case ChipKind.Repeat: repeat = (Repeat)c.Value; Changed(); break;
                case ChipKind.Preset: ApplyPreset(c.Value); break;
                case ChipKind.Calendar: OpenCalendar(c.Rect); break;
            }
        }

        void OpenCalendar(Rectangle anchor)
        {
            if (calendar == null)
            {
                calendar = new CalendarPopup();
                calendar.DateSelected += d => { date = d; Changed(); };
            }
            calendar.Show(this, date, new Point(Math.Max(0, Math.Min(anchor.X, Width - calendar.Width)), anchor.Bottom + Px(4)));
        }

        // ── 마우스 ──

        object HitTest(Point p)
        {
            foreach (var c in chips) if (c.Rect.Contains(p)) return c;
            foreach (var r in new[] { amRect, pmRect, hourUp, hourDown, minuteUp, minuteDown, hourRect, minuteRect })
                if (r.Contains(p)) return r;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (!Equals(h, hover))
            {
                hover = h;
                Cursor = h != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (WidgetForm.StillOver(this)) return;
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            var h = HitTest(e.Location);
            var chip = h as Chip;
            if (chip != null) { ClickChip(chip); return; }
            if (!(h is Rectangle)) return;
            var r = (Rectangle)h;
            if (r == amRect) { focusSeg = Seg.AmPm; SetAmPm(false); }
            else if (r == pmRect) { focusSeg = Seg.AmPm; SetAmPm(true); }
            else if (r == hourUp) { focusSeg = Seg.Hour; StepHour(1); }
            else if (r == hourDown) { focusSeg = Seg.Hour; StepHour(-1); }
            else if (r == minuteUp) { focusSeg = Seg.Minute; StepMinute(1); }
            else if (r == minuteDown) { focusSeg = Seg.Minute; StepMinute(-1); }
            else if (r == hourRect) { focusSeg = Seg.Hour; typed = ""; Invalidate(); }
            else if (r == minuteRect) { focusSeg = Seg.Minute; typed = ""; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int dir = e.Delta > 0 ? 1 : -1;
            var p = e.Location;
            var timeRow = Rectangle.Union(hourUp, minuteDown);
            if (Rectangle.Union(amRect, pmRect).Contains(p)) SetAmPm(hour < 12);
            else if (Rectangle.Union(hourUp, hourDown).Contains(p)) StepHour(dir);
            else if (Rectangle.Union(minuteUp, minuteDown).Contains(p)) StepMinute(dir);
            else if (chips.Count > 0 && p.Y < chips[0].Rect.Bottom) StepDate(-dir);
        }

        // ── 키보드 ──

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: case Keys.Enter: case Keys.Escape:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); typed = ""; Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                    int d = e.KeyCode == Keys.Up ? 1 : -1;
                    if (focusSeg == Seg.Hour) StepHour(d);
                    else if (focusSeg == Seg.AmPm) SetAmPm(hour < 12);
                    else StepMinute(d);
                    e.Handled = true;
                    break;
                case Keys.Left:
                    focusSeg = focusSeg == Seg.Minute ? Seg.Hour : Seg.AmPm; typed = ""; Invalidate(); e.Handled = true;
                    break;
                case Keys.Right:
                    focusSeg = focusSeg == Seg.AmPm ? Seg.Hour : Seg.Minute; typed = ""; Invalidate(); e.Handled = true;
                    break;
                case Keys.Enter:
                    e.Handled = e.SuppressKeyPress = true;
                    if (Commit != null) Commit();
                    break;
                case Keys.Escape:
                    e.Handled = e.SuppressKeyPress = true;
                    if (Cancel != null) Cancel();
                    break;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            char c = e.KeyChar;
            if (focusSeg == Seg.AmPm || focusSeg == Seg.None)
            {
                if (c == 'a' || c == 'A' || c == 'ㅁ') SetAmPm(false);
                else if (c == 'p' || c == 'P' || c == 'ㅔ') SetAmPm(true);
                return;
            }
            if (c < '0' || c > '9') return;
            e.Handled = true;
            typed += c;
            int v = int.Parse(typed);
            if (focusSeg == Seg.Hour)
            {
                if (typed.Length == 1 && v <= 2) { Invalidate(); return; } // 두 번째 숫자 대기 (10, 11, 12, 13~23)
                if (v > 23) { typed = ""; Invalidate(); return; }
                if (v == 0) hour = 0;
                else if (v >= 13) hour = v;
                else hour = (hour < 12 ? 0 : 12) + v % 12;
                focusSeg = Seg.Minute;
                Changed();
            }
            else
            {
                if (typed.Length == 1) { if (v > 5) { minute = v; Changed(); } else Invalidate(); return; }
                if (v > 59) { typed = ""; Invalidate(); return; }
                minute = v;
                Changed();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && calendar != null) calendar.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>달력 팝업 (일요일 빨강, 토요일 파랑).</summary>
    sealed class CalendarPopup : ToolStripDropDown
    {
        readonly CalendarView view;
        public event Action<DateTime> DateSelected;

        public CalendarPopup()
        {
            view = new CalendarView();
            var host = new ToolStripControlHost(view) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = view.Size };
            Padding = Padding.Empty;
            AutoSize = false;
            Size = view.Size;
            BackColor = Theme.Back;
            DropShadowEnabled = true;
            Items.Add(host);
            view.Picked += d => { Close(); if (DateSelected != null) DateSelected(d); };
        }

        public void Show(Control owner, DateTime selected, Point location)
        {
            view.Selected = selected.Date;
            view.Month = new DateTime(selected.Year, selected.Month, 1);
            Show(owner, location);
        }
    }

    sealed class CalendarView : Control
    {
        readonly Font titleFont = Theme.Font(10f, FontStyle.Bold);
        readonly Font weekFont = Theme.Font(8.5f);
        readonly Font dayFont = Theme.Num(10f);
        readonly Font dayBold = Theme.Num(10f, true);
        Rectangle prevRect, nextRect;
        DateTime hoverDay = DateTime.MinValue;
        int hoverNav;

        public DateTime Month { get; set; }
        public DateTime Selected { get; set; }
        public event Action<DateTime> Picked;

        static int Px(float v) { return Theme.Px(v); }
        int Cell { get { return Px(34); } }
        int Pad { get { return Px(10); } }
        int HeaderH { get { return Px(36); } }
        int WeekH { get { return Px(22); } }

        public CalendarView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            Size = new Size(Pad * 2 + Cell * 7, Pad + HeaderH + WeekH + Cell * 6 + Pad);
            Month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            Selected = DateTime.Now.Date;
        }

        DateTime FirstCell { get { return Month.AddDays(-(int)Month.DayOfWeek); } }

        Rectangle CellRect(int index)
        {
            return new Rectangle(Pad + index % 7 * Cell, Pad + HeaderH + WeekH + index / 7 * Cell, Cell, Cell);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            var today = DateTime.Now.Date;
            TextRenderer.DrawText(g, Month.Year + "년 " + Month.Month + "월", titleFont,
                new Rectangle(Pad, Pad, Cell * 7, HeaderH), Theme.Text, flags);
            prevRect = new Rectangle(Pad, Pad + (HeaderH - Px(28)) / 2, Px(28), Px(28));
            nextRect = new Rectangle(Pad + Cell * 7 - Px(28), prevRect.Y, Px(28), Px(28));
            if (hoverNav == -1) Theme.FillRound(g, Theme.Hover, prevRect, Px(6));
            if (hoverNav == 1) Theme.FillRound(g, Theme.Hover, nextRect, Px(6));
            FlatButton.DrawGlyph(g, Glyph.Left, prevRect, Theme.SubText);
            FlatButton.DrawGlyph(g, Glyph.Right, nextRect, Theme.SubText);

            string names = "일월화수목금토";
            for (int i = 0; i < 7; i++)
            {
                var c = i == 0 ? Theme.Danger : i == 6 ? Theme.Saturday : Theme.SubText;
                TextRenderer.DrawText(g, names[i].ToString(), weekFont,
                    new Rectangle(Pad + i * Cell, Pad + HeaderH, Cell, WeekH), c, flags);
            }

            var first = FirstCell;
            for (int i = 0; i < 42; i++)
            {
                var d = first.AddDays(i);
                var r = CellRect(i);
                var dot = Rectangle.Inflate(r, -Px(3), -Px(3));
                bool inMonth = d.Month == Month.Month;
                bool past = d < today;
                bool selected = d == Selected;
                Color fg = d.DayOfWeek == DayOfWeek.Sunday ? Theme.Danger : d.DayOfWeek == DayOfWeek.Saturday ? Theme.Saturday : Theme.Text;
                if (!inMonth || past) fg = Theme.Faint;

                if (selected)
                {
                    Theme.FillRound(g, Theme.Accent, dot, dot.Width / 2f);
                    fg = Theme.OnAccent;
                }
                else if (d == hoverDay && !past) Theme.FillRound(g, Theme.Hover, dot, dot.Width / 2f);
                if (d == today && !selected)
                    Theme.DrawRound(g, Theme.Accent, dot, dot.Width / 2f, Math.Max(1.2f, Theme.Scale * 1.3f));

                TextRenderer.DrawText(g, d.Day.ToString(), selected || d == today ? dayBold : dayFont, r, fg, flags);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int nav = prevRect.Contains(e.Location) ? -1 : nextRect.Contains(e.Location) ? 1 : 0;
            var day = DayAt(e.Location);
            if (nav != hoverNav || day != hoverDay)
            {
                hoverNav = nav;
                hoverDay = day;
                Cursor = nav != 0 || (day != DateTime.MinValue && day >= DateTime.Now.Date) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverNav = 0;
            hoverDay = DateTime.MinValue;
            Invalidate();
        }

        DateTime DayAt(Point p)
        {
            for (int i = 0; i < 42; i++)
                if (CellRect(i).Contains(p)) return FirstCell.AddDays(i);
            return DateTime.MinValue;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (prevRect.Contains(e.Location)) { Month = Month.AddMonths(-1); Invalidate(); return; }
            if (nextRect.Contains(e.Location)) { Month = Month.AddMonths(1); Invalidate(); return; }
            var d = DayAt(e.Location);
            if (d == DateTime.MinValue || d < DateTime.Now.Date) return;
            Selected = d;
            if (Picked != null) Picked(d);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Month = Month.AddMonths(e.Delta > 0 ? -1 : 1);
            Invalidate();
        }
    }
}
