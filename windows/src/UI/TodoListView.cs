using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TodoWidget.Core;

namespace TodoWidget.UI
{
    /// <summary>날짜별로 묶어서 직접 그리는 할 일 목록.</summary>
    sealed class TodoListView : Control
    {
        enum RowKind { Header, Item, DoneToggle, Note }

        sealed class Row
        {
            public RowKind Kind;
            public Section Section;
            public TodoItem Item;
            public string Note;
            public int Top, Height;
            public Rectangle Check, Text, Time, Delete; // 콘텐츠 좌표 (스크롤 미적용)
        }

        const TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                                          TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;
        const TextFormatFlags LineFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        readonly Font textFont = Theme.Font(10f);
        readonly Font doneFont = Theme.Font(10f, FontStyle.Strikeout);
        readonly Font headerFont = Theme.Font(8.5f, FontStyle.Bold);
        readonly Font headerSubFont = Theme.Font(8.5f);
        readonly Font timeFont = Theme.Num(10.5f);
        readonly Font smallFont = Theme.Font(8f);
        readonly Font emptyFont = Theme.Font(9.5f);
        readonly Font emptyTitleFont = Theme.Font(10.5f, FontStyle.Bold);

        List<TodoItem> items = new List<TodoItem>();
        List<Row> rows = new List<Row>();
        int contentHeight, scroll;
        int hoverIndex = -1;
        bool hoverCheck, hoverDelete, hoverThumb, hoverClear;
        bool draggingThumb;
        int dragStartY, dragStartScroll;

        public int ReminderMinutes { get; set; }
        public bool ShowDone { get; set; }
        public Func<DateTime> Clock { get; set; }

        public event Action<TodoItem> ToggleRequested;
        public event Action<TodoItem> DeleteRequested;
        public event Action<TodoItem> EditRequested;
        public event Action<TodoItem, Point> ContextRequested;
        public event Action DoneToggleRequested;
        public event Action ClearDoneRequested;

        const string ClearDoneText = "완료한 일 모두 지우기";

        public TodoListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Back;
            ReminderMinutes = 30;
            Clock = () => DateTime.Now;
        }

        public void SetItems(IEnumerable<TodoItem> list)
        {
            items = list.ToList();
            Relayout();
            Invalidate();
        }

        static int Px(float v) { return Theme.Px(v); }
        int PadX { get { return Px(10); } }
        int ScrollBarWidth { get { return Px(6); } }
        int MaxScroll { get { return Math.Max(0, contentHeight - Height); } }

        // ── 배치 ──

        void Relayout()
        {
            var now = Clock();
            rows = new List<Row>();
            int y = 0;
            int padY = Px(7);
            int box = Px(18);
            int right = Width - ScrollBarWidth - Px(2);
            int textX = PadX + box + Px(11);
            int lineH = TextRenderer.MeasureText("가", textFont, new Size(int.MaxValue, int.MaxValue), TextFlags).Height;
            int timeH = TextRenderer.MeasureText("00:00", timeFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Height;
            int smallH = TextRenderer.MeasureText("가", smallFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Height;
            int timeW = Math.Max(Math.Max(
                TextRenderer.MeasureText("00:00", timeFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Width,
                TextRenderer.MeasureText("12/30 (수)", smallFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Width),
                TextRenderer.MeasureText("매주 금", smallFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Width + Px(14)) + Px(10);

            var sections = Reminders.Group(items, now);
            bool anyOpen = sections.Any(s => s.Kind != SectionKind.Done);
            if (!anyOpen && sections.Count > 0)
            {
                int h = Px(64);
                rows.Add(new Row { Kind = RowKind.Note, Note = "남은 할 일이 없어요.", Top = y, Height = h });
                y += h;
            }

            foreach (var section in sections)
            {
                if (section.Kind == SectionKind.Done)
                {
                    int th = Px(34);
                    // 줄 오른쪽 끝: "완료한 일 모두 지우기"
                    int clearW = TextRenderer.MeasureText(ClearDoneText, headerSubFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Width + Px(16);
                    rows.Add(new Row
                    {
                        Kind = RowKind.DoneToggle, Section = section, Top = y + Px(4), Height = th,
                        Delete = new Rectangle(right - clearW, y + Px(4), clearW, th),
                    });
                    y += th + Px(4);
                    if (!ShowDone) continue;
                }
                else
                {
                    int hh = Px(28);
                    rows.Add(new Row { Kind = RowKind.Header, Section = section, Top = y + (rows.Count == 0 ? 0 : Px(6)), Height = hh });
                    y += hh + (rows.Count == 1 ? 0 : Px(6));
                }

                foreach (var item in section.Items)
                {
                    bool hasTime = item.Due.HasValue;
                    int colW = hasTime ? timeW : 0;
                    int textW = Math.Max(Px(40), right - Px(6) - colW - textX);
                    var font = item.Done ? doneFont : textFont;
                    int textH = Math.Max(lineH, TextRenderer.MeasureText(item.Text ?? "", font, new Size(textW, int.MaxValue), TextFlags).Height);
                    int colH = hasTime ? timeH + (HasSecondLine(item, section, now) ? smallH + Px(1) : 0) : 0;
                    int h = padY * 2 + Math.Max(textH, colH);

                    var row = new Row { Kind = RowKind.Item, Section = section, Item = item, Top = y, Height = h };
                    row.Text = new Rectangle(textX, y + padY, textW, textH);
                    row.Check = new Rectangle(PadX, y + padY + (lineH - box) / 2, box, box);
                    row.Time = new Rectangle(right - Px(6) - timeW, y + padY + (lineH - timeH) / 2, timeW, colH);
                    int ds = Px(24);
                    row.Delete = new Rectangle(right - Px(4) - ds, y + padY + (lineH - ds) / 2, ds, ds);
                    rows.Add(row);
                    y += h;
                }
            }
            contentHeight = y + Px(8);
            scroll = Math.Max(0, Math.Min(scroll, MaxScroll));
        }

        bool HasSecondLine(TodoItem item, Section section, DateTime now)
        {
            if (!item.Due.HasValue) return false;
            if (item.Done) return item.Due.Value.Date != now.Date;
            if (item.IsRecurring) return true;
            if (section.Kind == SectionKind.Later) return true;
            if (section.Kind == SectionKind.Overdue) return item.Due.Value.Date != now.Date;
            return TimeText.State(item, now, ReminderMinutes) == DueState.Soon;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
            Invalidate();
        }

        // ── 그리기 ──

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var now = Clock();

            if (rows.Count == 0)
            {
                DrawEmpty(g);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int top = row.Top - scroll;
                if (top + row.Height < 0 || top > Height) continue;
                switch (row.Kind)
                {
                    case RowKind.Header: DrawHeader(g, row, top); break;
                    case RowKind.DoneToggle: DrawDoneToggle(g, row, top, i == hoverIndex); break;
                    case RowKind.Note:
                        TextRenderer.DrawText(g, row.Note, emptyFont, new Rectangle(0, top, Width - ScrollBarWidth, row.Height), Theme.SubText,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        break;
                    case RowKind.Item: DrawItem(g, row, top, i == hoverIndex, now); break;
                }
            }

            if (MaxScroll > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var thumb = ThumbRect();
                Theme.FillRound(g, hoverThumb || draggingThumb ? Theme.SubText : Theme.Border, thumb, thumb.Width / 2f);
            }
        }

        void DrawEmpty(Graphics g)
        {
            var r = new Rectangle(PadX, Px(28), Width - PadX * 2, Px(24));
            TextRenderer.DrawText(g, "할 일이 없어요", emptyTitleFont, r, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            r.Y += Px(28); r.Height = Px(60);
            TextRenderer.DrawText(g, "위 칸에 적고 Enter를 누르면 추가돼요.\n\"내일 오후 3시 회의\"처럼 쓰면\n시간도 함께 정해져요.", emptyFont, r, Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        void DrawHeader(Graphics g, Row row, int top)
        {
            var s = row.Section;
            var color = s.Kind == SectionKind.Overdue ? Theme.Danger : Theme.SubText;
            int x = PadX;
            int baseY = top + row.Height - Px(6);
            var titleSize = TextRenderer.MeasureText(s.Title, headerFont, new Size(int.MaxValue, int.MaxValue), LineFlags);
            int ty = baseY - titleSize.Height;
            TextRenderer.DrawText(g, s.Title, headerFont, new Point(x, ty), color, LineFlags);
            x += titleSize.Width + Px(6);

            if (s.Date.HasValue)
            {
                var d = s.Date.Value;
                string md = d.Month + "/" + d.Day + " ";
                TextRenderer.DrawText(g, md, headerSubFont, new Point(x, ty), Theme.Faint, LineFlags);
                x += TextRenderer.MeasureText(md, headerSubFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Width + Px(3);
                var dayColor = d.DayOfWeek == DayOfWeek.Sunday ? Theme.Danger : d.DayOfWeek == DayOfWeek.Saturday ? Theme.Saturday : Theme.Faint;
                string wd = TimeText.DayName(d);
                TextRenderer.DrawText(g, wd, headerSubFont, new Point(x, ty), dayColor, LineFlags);
                x += TextRenderer.MeasureText(wd, headerSubFont, new Size(int.MaxValue, int.MaxValue), LineFlags).Width + Px(6);
            }

            string count = s.Items.Count.ToString();
            TextRenderer.DrawText(g, count, headerSubFont, new Point(x, ty), Theme.Faint, LineFlags);
        }

        void DrawDoneToggle(Graphics g, Row row, int top, bool hovered)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var clear = Offset(row.Delete);
            bool overClear = hovered && hoverClear;
            bool overToggle = hovered && !hoverClear;
            var r = new RectangleF(Px(2), top, clear.Left - Px(6), row.Height);
            if (overToggle) Theme.FillRound(g, Theme.Hover, r, Px(7));
            if (overClear) Theme.FillRound(g, Theme.Hover, clear, Px(7));
            TextRenderer.DrawText(g, ClearDoneText, headerSubFont, clear, overClear ? Theme.Danger : Theme.SubText,
                LineFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            string label = (ShowDone ? "완료 숨기기" : "완료한 일 보기") + "  " + row.Section.Items.Count;
            var color = overToggle ? Theme.Text : Theme.SubText;
            int cx = PadX + Px(9), cy = top + row.Height / 2;
            using (var pen = new Pen(color, Math.Max(1.4f, Theme.Scale * 1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float a = Px(3.5f);
                if (ShowDone) g.DrawLines(pen, new[] { new PointF(cx - a, cy - a / 2), new PointF(cx, cy + a / 2), new PointF(cx + a, cy - a / 2) });
                else g.DrawLines(pen, new[] { new PointF(cx - a / 2, cy - a), new PointF(cx + a / 2, cy), new PointF(cx - a / 2, cy + a) });
            }
            TextRenderer.DrawText(g, label, headerSubFont, new Rectangle(PadX + Px(28), top, Width, row.Height), color,
                LineFlags | TextFormatFlags.VerticalCenter);
        }

        void DrawItem(Graphics g, Row row, int top, bool hovered, DateTime now)
        {
            var item = row.Item;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hovered)
                Theme.FillRound(g, Theme.Hover, new RectangleF(Px(2), top, Width - ScrollBarWidth - Px(4), row.Height), Px(7));

            var state = TimeText.State(item, now, ReminderMinutes);
            DrawCheck(g, Offset(row.Check), item.Done, hovered && hoverCheck, state);

            TextRenderer.DrawText(g, item.Text ?? "", item.Done ? doneFont : textFont, Offset(row.Text),
                item.Done ? Theme.Faint : Theme.Text, TextFlags);

            if (hovered)
            {
                var dr = Offset(row.Delete);
                if (hoverDelete) Theme.FillRound(g, Theme.Pressed, dr, Px(6));
                FlatButton.DrawGlyph(g, Glyph.Close, dr, hoverDelete ? Theme.Danger : Theme.SubText);
                return; // hover 중에는 시간 대신 삭제 버튼
            }

            if (!item.Due.HasValue) return;
            var due = item.Due.Value;
            var tr = Offset(row.Time);
            string time = TimeText.HourMinute(due);
            var timeSize = TextRenderer.MeasureText(time, timeFont, new Size(int.MaxValue, int.MaxValue), LineFlags);
            var timeRect = new Rectangle(tr.Right - timeSize.Width, tr.Y, timeSize.Width, timeSize.Height);

            Color timeColor = item.Done ? Theme.Faint : Theme.Text;
            if (!item.Done && state == DueState.Overdue) timeColor = Theme.Danger;
            if (!item.Done && state == DueState.Soon)
            {
                var mark = new RectangleF(timeRect.X - Px(4), timeRect.Y + timeRect.Height * 0.18f, timeRect.Width + Px(8), timeRect.Height * 0.78f);
                Theme.FillRound(g, Theme.Marker, mark, Px(3));
                timeColor = Theme.MarkerText;
            }
            TextRenderer.DrawText(g, time, timeFont, timeRect.Location, timeColor, LineFlags);

            if (HasSecondLine(item, row.Section, now))
            {
                string sub;
                Color subColor = Theme.SubText;
                bool repeatIcon = false;
                if (!item.Done && state == DueState.Soon) { sub = TimeText.Relative(due, now); subColor = Theme.Warn; }
                else if (item.IsRecurring && !item.Done) { sub = Recurrence.Label(item.Repeat, due); repeatIcon = true; }
                else sub = due.Month + "/" + due.Day + " (" + TimeText.DayName(due) + ")";
                if (item.Done) subColor = Theme.Faint;
                else if (state == DueState.Overdue) subColor = Theme.Danger;
                var subSize = TextRenderer.MeasureText(sub, smallFont, new Size(int.MaxValue, int.MaxValue), LineFlags);
                var subPos = new Point(tr.Right - subSize.Width, timeRect.Bottom + Px(1));
                TextRenderer.DrawText(g, sub, smallFont, subPos, subColor, LineFlags);
                if (repeatIcon)
                {
                    int s = subSize.Height;
                    FlatButton.DrawGlyph(g, Glyph.Repeat, new Rectangle(subPos.X - s - Px(1), subPos.Y, s, s), subColor);
                }
            }
        }

        static void DrawCheck(Graphics g, Rectangle r, bool done, bool hover, DueState state)
        {
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            float radius = r.Width / 2f;
            float stroke = Math.Max(1.4f, Theme.Scale * 1.5f);
            if (done)
            {
                Theme.FillRound(g, Theme.Accent, rf, radius);
                using (var pen = new Pen(Theme.OnAccent, Math.Max(1.6f, Theme.Scale * 1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    float x = rf.X, y = rf.Y, w = rf.Width;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(x + w * 0.28f, y + w * 0.52f),
                        new PointF(x + w * 0.44f, y + w * 0.68f),
                        new PointF(x + w * 0.73f, y + w * 0.36f),
                    });
                }
                return;
            }
            Color border = state == DueState.Overdue ? Theme.Danger : Theme.Faint;
            if (hover)
            {
                Theme.FillRound(g, Color.FromArgb(Theme.IsDark ? 70 : 40, Theme.Accent), rf, radius);
                border = Theme.Accent;
            }
            Theme.DrawRound(g, border, rf, radius, stroke);
        }

        Rectangle Offset(Rectangle r) { return new Rectangle(r.X, r.Y - scroll, r.Width, r.Height); }

        Rectangle ThumbRect()
        {
            int w = Px(4);
            int h = Math.Max(Px(28), (int)((long)Height * Height / Math.Max(1, contentHeight)));
            int track = Height - h;
            int y = MaxScroll == 0 ? 0 : (int)((long)track * scroll / MaxScroll);
            return new Rectangle(Width - w - Px(1), y, w, h);
        }

        // ── 마우스 ──

        int HitRow(Point p)
        {
            int y = p.Y + scroll;
            for (int i = 0; i < rows.Count; i++)
            {
                var k = rows[i].Kind;
                if ((k == RowKind.Item || k == RowKind.DoneToggle) && y >= rows[i].Top && y < rows[i].Top + rows[i].Height) return i;
            }
            return -1;
        }

        void UpdateHover(Point p)
        {
            int idx = HitRow(p);
            bool onThumb = MaxScroll > 0 && ThumbRect().Contains(p);
            bool onCheck = false, onDelete = false;
            if (idx >= 0 && !onThumb && rows[idx].Kind == RowKind.Item)
            {
                var r = rows[idx];
                var check = Offset(r.Check);
                check.Inflate(Px(6), Px(6));
                onCheck = check.Contains(p);
                onDelete = Offset(r.Delete).Contains(p);
            }
            bool onClear = idx >= 0 && !onThumb && rows[idx].Kind == RowKind.DoneToggle && Offset(rows[idx].Delete).Contains(p);
            if (idx != hoverIndex || onCheck != hoverCheck || onDelete != hoverDelete || onThumb != hoverThumb || onClear != hoverClear)
            {
                hoverIndex = idx;
                hoverCheck = onCheck;
                hoverDelete = onDelete;
                hoverThumb = onThumb;
                hoverClear = onClear;
                bool toggleRow = idx >= 0 && rows[idx].Kind == RowKind.DoneToggle;
                Cursor = onCheck || onDelete || toggleRow ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (draggingThumb)
            {
                var thumb = ThumbRect();
                int track = Math.Max(1, Height - thumb.Height);
                SetScroll(dragStartScroll + (int)((long)(e.Y - dragStartY) * MaxScroll / track));
                return;
            }
            UpdateHover(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (draggingThumb) return;
            hoverIndex = -1;
            hoverCheck = hoverDelete = hoverThumb = hoverClear = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            UpdateHover(e.Location);
            if (e.Button == MouseButtons.Left && hoverThumb)
            {
                draggingThumb = true;
                dragStartY = e.Y;
                dragStartScroll = scroll;
                Capture = true;
                return;
            }
            if (hoverIndex < 0) return;
            var row = rows[hoverIndex];

            if (row.Kind == RowKind.DoneToggle)
            {
                if (e.Button != MouseButtons.Left || e.Clicks != 1) return;
                if (hoverClear) { if (ClearDoneRequested != null) ClearDoneRequested(); }
                else if (DoneToggleRequested != null) DoneToggleRequested();
                return;
            }

            var item = row.Item;
            if (e.Button == MouseButtons.Right)
            {
                if (ContextRequested != null) ContextRequested(item, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // 더블클릭의 두 번째 클릭은 무시: 정렬/삭제로 다른 항목이 커서 아래로 올 수 있음.
            if (e.Clicks == 1 && hoverCheck) { if (ToggleRequested != null) ToggleRequested(item); }
            else if (e.Clicks == 1 && hoverDelete) { if (DeleteRequested != null) DeleteRequested(item); }
            else if (e.Clicks == 2 && !hoverCheck && !hoverDelete) { if (EditRequested != null) EditRequested(item); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggingThumb)
            {
                draggingThumb = false;
                Capture = false;
                UpdateHover(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SetScroll(scroll - e.Delta * Px(48) / 120);
            UpdateHover(PointToClient(MousePosition));
        }

        void SetScroll(int value)
        {
            value = Math.Max(0, Math.Min(value, MaxScroll));
            if (value == scroll) return;
            scroll = value;
            Invalidate();
        }

        public void ScrollToItem(TodoItem item)
        {
            var row = rows.FirstOrDefault(r => r.Item == item);
            if (row == null) return;
            int top = row.Top - Px(30);
            if (top < scroll) SetScroll(top);
            else if (row.Top + row.Height > scroll + Height) SetScroll(row.Top + row.Height - Height);
        }
    }
}
