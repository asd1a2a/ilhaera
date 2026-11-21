using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TodoWidget.Core;

namespace TodoWidget.UI
{
    /// <summary>바탕화면 위젯 본체.</summary>
    sealed class WidgetForm : Form
    {
        readonly TrayApp app;
        readonly HeaderView header;
        readonly RoundPanel inputPanel;
        readonly TextBox input;
        readonly FlatButton clockButton, addButton;
        readonly ParseChip parseChip;
        readonly Panel dueRow;
        readonly TimePicker picker;
        readonly TodoListView list;
        readonly RoundPanel undoBar;
        readonly Label undoLabel;
        readonly FlatButton undoButton;
        readonly Timer undoTimer;
        readonly ContextMenuStrip itemMenu;
        readonly bool drawOwnBorder;
        IntPtr pinnedOwner;
        bool parseActive, parseDismissed;
        string parsedText;
        DateTime parsedDue;
        Repeat parsedRepeat;

        static int Px(float v) { return Theme.Px(v); }

        public WidgetForm(TrayApp app)
        {
            this.app = app;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Text = "일해라";
            Icon = Theme.LoadIcon(new Size(32, 32));
            DoubleBuffered = true;
            KeyPreview = true;
            MinimumSize = new Size(Px(250), Px(240));
            Padding = new Padding(Px(14), Px(8), Px(12), Px(10));
            drawOwnBorder = !(Native.IsWindows && Environment.OSVersion.Version.Build >= 22000);

            // ── 헤더 (일력) ──
            var tip = new ToolTip();
            header = new HeaderView();
            tip.SetToolTip(header.MenuButton, "메뉴");
            tip.SetToolTip(header.HideButton, "숨기기 (트레이 아이콘으로 다시 열기)");
            header.DragStart += DragMouseDown;
            header.NextClicked += item => list.ScrollToItem(item);
            header.MenuButton.Click += (s, e) => app.Menu.Show(header.MenuButton, new Point(header.MenuButton.Width - app.Menu.Width, header.MenuButton.Height));
            header.HideButton.Click += (s, e) => app.HideWidget();

            // ── 입력 ──
            inputPanel = new RoundPanel { BackColor = Theme.Surface, FillColor = Theme.Surface, Radius = Px(9) };
            input = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Font(10.5f),
                MaxLength = 500,
            };
            clockButton = new FlatButton { Glyph = Glyph.Clock, Size = new Size(Px(28), Px(28)), BackColor = Theme.Surface };
            addButton = new FlatButton { Glyph = Glyph.Plus, Primary = true, Size = new Size(Px(28), Px(28)), Radius = Px(7) };
            tip.SetToolTip(clockButton, "날짜·시간 직접 고르기");
            tip.SetToolTip(addButton, "추가 (Enter)");
            inputPanel.Controls.AddRange(new Control[] { input, clockButton, addButton });
            inputPanel.Layout += (s, e) => LayoutInput();
            inputPanel.Click += (s, e) => input.Focus();
            input.KeyDown += InputKeyDown;
            input.HandleCreated += (s, e) => Native.SetCueBanner(input.Handle, "할 일 추가 (예: 내일 3시 회의)");
            input.TextChanged += (s, e) => UpdateParse();
            clockButton.Click += (s, e) => SetDueVisible(!dueRow.Visible);
            addButton.Click += (s, e) => CommitAdd();

            // ── 자연어 시간 미리보기 ──
            parseChip = new ParseChip { Visible = false };
            tip.SetToolTip(parseChip.ClearButton, "시간 없이 추가");
            parseChip.ClearButton.Click += (s, e) => { parseDismissed = true; UpdateParse(); input.Focus(); };

            // ── 시간 설정 줄 ──
            dueRow = new Panel { BackColor = Theme.Back, Visible = false };
            picker = new TimePicker();
            dueRow.Controls.Add(picker);
            dueRow.Layout += (s, e) => picker.SetBounds(0, 0, dueRow.Width, picker.PreferredHeight);
            picker.Commit += () => BeginInvoke((Action)CommitAdd);
            picker.Cancel += () => { SetDueVisible(false); input.Focus(); };

            // ── 목록 ──
            list = new TodoListView();
            list.ToggleRequested += item => app.Toggle(item);
            list.DeleteRequested += item => app.Remove(item);
            list.EditRequested += EditItem;
            list.ContextRequested += ShowItemMenu;
            list.DoneToggleRequested += () => app.ToggleShowDone();

            // ── 되돌리기 바 ──
            undoBar = new RoundPanel { BackColor = Theme.Back, FillColor = Theme.Surface, Radius = Px(8), Visible = false };
            undoLabel = new Label { AutoSize = false, AutoEllipsis = true, Font = Theme.Font(9f), ForeColor = Theme.SubText, BackColor = Theme.Surface, TextAlign = ContentAlignment.MiddleLeft };
            undoButton = new FlatButton { Text = "되돌리기", ForeColor = Theme.Accent, BackColor = Theme.Surface, Font = Theme.Font(9f, FontStyle.Bold) };
            undoBar.Controls.Add(undoLabel);
            undoBar.Controls.Add(undoButton);
            undoBar.Layout += (s, e) =>
            {
                int bw = Px(74);
                undoButton.SetBounds(undoBar.Width - bw - Px(4), Px(4), bw, undoBar.Height - Px(8));
                undoLabel.SetBounds(Px(10), 0, undoButton.Left - Px(14), undoBar.Height);
            };
            undoButton.Click += (s, e) => { HideUndo(); app.Undo(); };
            undoTimer = new Timer { Interval = 7000 };
            undoTimer.Tick += (s, e) => HideUndo();

            itemMenu = Theme.CreateMenu();

            Controls.AddRange(new Control[] { header, inputPanel, parseChip, dueRow, list, undoBar });
            ResumeLayout(false);

            RestoreBoundsFromSettings();
            ApplySettings();
            RefreshList();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (Native.IsWindows) cp.ExStyle |= Native.WS_EX_TOOLWINDOW; // Alt+Tab·작업표시줄에 안 보이게
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
            pinnedOwner = IntPtr.Zero;
            EnsurePinned();
        }

        /// <summary>바탕화면에 고정 (탐색기가 재시작된 경우 다시 고정).</summary>
        public void EnsurePinned()
        {
            if (!IsHandleCreated || Native.IsAlive(pinnedOwner)) return;
            pinnedOwner = Native.PinToDesktop(Handle);
        }

        public void ApplySettings()
        {
            var s = app.Settings;
            TopMost = s.AlwaysOnTop;
            Opacity = Math.Max(0.3, Math.Min(1.0, s.OpacityPercent / 100.0));
            list.ReminderMinutes = s.ReminderMinutes;
            picker.ReminderMinutes = s.ReminderMinutes;
            list.Invalidate();
        }

        // ── 위치/크기 ──

        void RestoreBoundsFromSettings()
        {
            var s = app.Settings;
            if (s.X.HasValue && s.Y.HasValue && s.Width.HasValue && s.Height.HasValue)
            {
                var r = new Rectangle(s.X.Value, s.Y.Value,
                    Math.Max(MinimumSize.Width, s.Width.Value), Math.Max(MinimumSize.Height, s.Height.Value));
                var titleArea = new Rectangle(r.X + Px(20), r.Y, Math.Max(1, r.Width - Px(40)), Px(36));
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(titleArea)))
                {
                    Bounds = r;
                    return;
                }
            }
            SetDefaultBounds();
        }

        void SetDefaultBounds()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            var size = new Size(Px(320), Math.Min(Px(480), wa.Height - Px(48)));
            Bounds = new Rectangle(wa.Right - size.Width - Px(24), wa.Top + Px(24), size.Width, size.Height);
        }

        public void ResetPosition()
        {
            SetDefaultBounds();
            app.SaveBounds(Bounds);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            app.SaveBounds(Bounds);
        }

        void DragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1) Native.BeginDrag(Handle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Y < inputPanel.Top) DragMouseDown(this, e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != Native.WM_NCHITTEST || (int)m.Result != Native.HTCLIENT) return;

            // 테두리 근처에서는 크기 조절
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int g = Px(7);
            bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g / 2 + 1, b = p.Y >= ClientSize.Height - g;
            if (t && l) m.Result = (IntPtr)Native.HTTOPLEFT;
            else if (t && r) m.Result = (IntPtr)Native.HTTOPRIGHT;
            else if (b && l) m.Result = (IntPtr)Native.HTBOTTOMLEFT;
            else if (b && r) m.Result = (IntPtr)Native.HTBOTTOMRIGHT;
            else if (l) m.Result = (IntPtr)Native.HTLEFT;
            else if (r) m.Result = (IntPtr)Native.HTRIGHT;
            else if (t) m.Result = (IntPtr)Native.HTTOP;
            else if (b) m.Result = (IntPtr)Native.HTBOTTOM;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !app.Exiting)
            {
                e.Cancel = true;
                app.HideWidget();
                return;
            }
            base.OnFormClosing(e);
        }

        // ── 레이아웃 ──

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (list == null) return;
            var pad = Padding;
            int x = pad.Left, w = ClientSize.Width - pad.Horizontal, y = pad.Top;

            header.SetBounds(x, y, w, header.PreferredHeight);
            y += header.PreferredHeight + Px(10);
            inputPanel.SetBounds(x, y, w, Px(40));
            y += Px(40) + Px(6);
            if (parseChip.Visible)
            {
                parseChip.SetBounds(x, y, w, parseChip.Height);
                y += parseChip.Height + Px(4);
            }
            else y += Px(4);
            if (dueRow.Visible)
            {
                int h = picker.PreferredHeight;
                dueRow.SetBounds(x, y, w, h);
                y += h + Px(10);
            }
            int bottom = ClientSize.Height - pad.Bottom;
            if (undoBar.Visible)
            {
                undoBar.SetBounds(x, bottom - Px(36), w, Px(36));
                bottom -= Px(36) + Px(6);
            }
            list.SetBounds(x - Px(4), y, w + Px(8), Math.Max(0, bottom - y));
        }

        void LayoutInput()
        {
            int h = inputPanel.Height, bs = addButton.Width, m = (h - bs) / 2;
            addButton.Location = new Point(inputPanel.Width - bs - m, m);
            clockButton.Location = new Point(addButton.Left - bs - Px(4), m);
            input.SetBounds(Px(12), (h - input.Height) / 2, Math.Max(10, clockButton.Left - Px(16)), input.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (drawOwnBorder)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }

        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        // ── 동작 ──

        void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                // 한글 IME 조합 중인 마지막 글자가 확정된 뒤에 처리
                BeginInvoke((Action)CommitAdd);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                if (dueRow.Visible) SetDueVisible(false);
                else if (parseActive) { parseDismissed = true; UpdateParse(); }
                else input.Clear();
            }
        }

        void CommitAdd()
        {
            var text = (input.Text ?? "").Trim();
            if (text.Length == 0)
            {
                input.Focus();
                return;
            }
            DateTime? due = null;
            Repeat repeat = Repeat.None;
            if (dueRow.Visible)
            {
                due = picker.GetValue(out repeat);
            }
            else
            {
                UpdateParse();
                if (parseActive) { text = parsedText; due = parsedDue; repeat = parsedRepeat; }
            }
            var item = app.Add(text, due, repeat);
            input.Clear();
            parseDismissed = false;
            SetDueVisible(false);
            input.Focus();
            list.ScrollToItem(item);
        }

        void SetDueVisible(bool visible)
        {
            if (visible)
            {
                picker.ReminderMinutes = app.Settings.ReminderMinutes;
                if (parseActive) picker.SetValue(parsedDue, parsedRepeat);
                else picker.SetValue(TimeText.DefaultDue(DateTime.Now), Repeat.None);
            }
            clockButton.Active = visible;
            clockButton.Invalidate();
            dueRow.Visible = visible;
            UpdateParse();
            PerformLayout();
            Invalidate();
            if (visible) picker.Focus();
        }

        void EditItem(TodoItem item)
        {
            using (var dlg = new EditDialog(item.Text, item.Due, item.Repeat, app.Settings.ReminderMinutes))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    app.Update(item, dlg.ResultText, dlg.ResultDue, dlg.ResultRepeat);
            }
        }

        void ShowItemMenu(TodoItem item, Point location)
        {
            itemMenu.Items.Clear();
            itemMenu.Items.Add("수정", null, (s, e) => EditItem(item));
            itemMenu.Items.Add(item.IsRecurring && !item.Done ? "이번 회차 완료" : item.Done ? "완료 취소" : "완료로 표시", null, (s, e) => app.Toggle(item));
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add("삭제", null, (s, e) => app.Remove(item));
            itemMenu.Show(list, location);
        }

        public void RefreshList()
        {
            list.ReminderMinutes = app.Settings.ReminderMinutes;
            list.ShowDone = app.Settings.ShowDone;
            list.SetItems(app.Data.Items);
            int before = header.PreferredHeight;
            header.SetData(app.Data.Items, app.Settings.ReminderMinutes);
            if (header.PreferredHeight != before) PerformLayout();
            Invalidate();
        }

        /// <summary>주기적으로 호출: 묶음(오늘→지난 일정)·남은 시간·날짜 갱신.</summary>
        public void Tick()
        {
            RefreshList();
            if (parseChip.Visible) UpdateParse();
        }

        /// <summary>입력 중인 문장에서 날짜·시간을 찾아 미리 보여 준다.</summary>
        void UpdateParse()
        {
            string text = null; DateTime due = default(DateTime); Repeat repeat = Repeat.None;
            bool found = !dueRow.Visible && !parseDismissed &&
                         QuickParse.TryParse(input.Text ?? "", DateTime.Now, out text, out due, out repeat);
            if (found)
            {
                parsedText = text;
                parsedDue = due;
                parsedRepeat = repeat;
                parseChip.SetValue(due, app.Settings.ReminderMinutes, repeat);
            }
            if (string.IsNullOrWhiteSpace(input.Text)) parseDismissed = false;
            parseActive = found;
            if (parseChip.Visible != found)
            {
                parseChip.Visible = found;
                PerformLayout();
                Invalidate();
            }
        }

        public void ShowUndo(string message)
        {
            undoLabel.Text = message;
            undoBar.Visible = true;
            undoTimer.Stop();
            undoTimer.Start();
            PerformLayout();
            Invalidate();
        }

        void HideUndo()
        {
            undoTimer.Stop();
            if (!undoBar.Visible) return;
            undoBar.Visible = false;
            PerformLayout();
            Invalidate();
        }

        public void FocusInput()
        {
            input.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                undoTimer.Dispose();
                itemMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
