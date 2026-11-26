using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using TodoWidget.Core;
using TodoWidget.UI;
using Timer = System.Windows.Forms.Timer;

namespace TodoWidget
{
    /// <summary>트레이 아이콘·데이터·알림을 관리하는 앱 본체.</summary>
    sealed class TrayApp : ApplicationContext
    {
        static readonly int[] OpacityChoices = { 100, 95, 90, 80, 70, 60 };
        static readonly int[] LeadChoices = { 5, 10, 15, 30, 60, 120 };
        // 유리 투명도: 값이 작을수록 뒤가 많이 비친다
        static readonly int[] GlassChoices = { 0, 10, 20, 35, 50, 65, 80 };
        static readonly string[] GlassNames = { "완전 투명", "아주 투명", "투명", "보통", "조금 진하게", "진하게", "아주 진하게" };

        readonly Storage storage;
        readonly NotifyIcon tray;
        readonly Timer timer;
        readonly Control invoker;
        WidgetForm widget;
        bool wantVisible = true;
        bool saveErrorShown;
        Action undoAction;

        ToolStripMenuItem miToggle, miTop, miAuto, miClearDone, miOpacity, miGlass, miGlassBlur, miLead, miTheme, miAccent, miMarker;
        string appliedLook;

        static readonly string[][] AccentChoices =
        {
            new[] { "#1E7A57", "초록" }, new[] { "#2F6BD8", "파랑" }, new[] { "#7B4FD6", "보라" }, new[] { "#D6457B", "분홍" },
            new[] { "#D9771E", "주황" }, new[] { "#1A8F99", "청록" }, new[] { "#3A3F47", "흑연" },
        };
        static readonly string[][] MarkerChoices =
        {
            new[] { "#FFEC85", "노랑" }, new[] { "#C7F089", "연두" }, new[] { "#A9DDFF", "하늘" }, new[] { "#FFC2DA", "분홍" }, new[] { "#FFD3A8", "살구" },
        };

        public AppData Data { get; private set; }
        public AppSettings Settings { get { return Data.Settings; } }
        public ContextMenuStrip Menu { get; private set; }
        public bool Exiting { get; private set; }

        public TrayApp(Storage storage, AppData data, EventWaitHandle showEvent)
        {
            this.storage = storage;
            Data = data;

            invoker = new Control();
            invoker.CreateControl();

            appliedLook = LookKey();
            ApplyColors();
            BuildMenu();
            tray = new NotifyIcon
            {
                Icon = Theme.LoadIcon(SystemInformation.SmallIconSize),
                Text = "일해라",
                ContextMenuStrip = Menu,
                Visible = true,
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWidget(); };
            tray.BalloonTipClicked += (s, e) => ShowWidget();

            CreateWidget();
            ShowWidget();

            if (!Settings.AutoStartInitialized)
            {
                bool ok = true;
                try { AutoStart.SetEnabled(true); } catch (Exception) { ok = false; }
                Settings.AutoStartInitialized = true;
                Save();
                tray.ShowBalloonTip(8000, "일해라",
                    ok ? "바탕화면 위젯이 실행되었어요.\nWindows를 켤 때 자동으로 시작됩니다."
                       : "바탕화면 위젯이 실행되었어요.", ToolTipIcon.Info);
            }
            else
            {
                AutoStart.RefreshPath();
            }

            timer = new Timer { Interval = 15000 };
            timer.Tick += (s, e) => OnTick();
            timer.Start();

            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                SystemEvents.TimeChanged += OnTimeChanged;
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            catch (Exception) { }

            // 두 번째 실행 시 기존 위젯을 띄운다.
            var listener = new Thread(() =>
            {
                try
                {
                    while (showEvent.WaitOne())
                    {
                        if (Exiting) break;
                        invoker.BeginInvoke((Action)ShowWidget);
                    }
                }
                catch (Exception) { }
            }) { IsBackground = true, Name = "ShowListener" };
            listener.Start();

            // 자동 확인(CI): 눌러 볼 자리를 파일로 적어 둔다
            string ciDir = Environment.GetEnvironmentVariable("ILHAERA_CI_DIR");
            if (!string.IsNullOrEmpty(ciDir))
            {
                var ciTimer = new Timer { Interval = 500 };
                ciTimer.Tick += (s, e) =>
                {
                    try { if (WidgetAlive) System.IO.File.WriteAllText(System.IO.Path.Combine(ciDir, "layout.json"), widget.DescribeLayout()); }
                    catch (Exception) { }
                };
                ciTimer.Start();
            }

            // 메시지 루프가 돈 직후 첫 알림 확인 (꺼져 있는 동안 놓친 일정 포함)
            invoker.BeginInvoke((Action)CheckReminders);
        }

        // ── 위젯 ──

        void CreateWidget()
        {
            widget = new WidgetForm(this);
        }

        bool WidgetAlive { get { return widget != null && !widget.IsDisposed; } }

        void EnsureWidget()
        {
            if (!WidgetAlive || (wantVisible && !widget.IsHandleCreated))
            {
                try { if (widget != null) widget.Dispose(); } catch (Exception) { }
                CreateWidget();
                if (wantVisible) ShowWidget();
            }
            else
            {
                widget.EnsurePinned();
            }
        }

        public void ShowWidget()
        {
            if (Exiting) return;
            wantVisible = true;
            if (!WidgetAlive) CreateWidget();
            if (!widget.Visible) widget.Show();
            if (widget.WindowState == FormWindowState.Minimized) widget.WindowState = FormWindowState.Normal;
            widget.Activate();
            widget.FocusInput();
        }

        public void HideWidget()
        {
            wantVisible = false;
            if (WidgetAlive) widget.Hide();
            if (!Settings.TrayHintShown)
            {
                Settings.TrayHintShown = true;
                Save();
                tray.ShowBalloonTip(6000, "일해라",
                    "작업 표시줄 오른쪽 트레이의 아이콘을 클릭하면 다시 열 수 있어요.", ToolTipIcon.Info);
            }
        }

        void ToggleWidget()
        {
            if (WidgetAlive && widget.Visible) HideWidget();
            else ShowWidget();
        }

        public void SaveBounds(Rectangle bounds)
        {
            Settings.X = bounds.X;
            Settings.Y = bounds.Y;
            Settings.Width = bounds.Width;
            Settings.Height = bounds.Height;
            Save();
        }

        // ── 데이터 조작 ──

        public List<TodoItem> SortedItems()
        {
            return Reminders.Sort(Data.Items);
        }

        public TodoItem Add(string text, DateTime? due, Repeat repeat)
        {
            var item = TodoItem.Create(text, due, DateTime.Now, repeat);
            Data.Items.Add(item);
            Commit();
            CheckReminders();
            return item;
        }

        public void Toggle(TodoItem item)
        {
            var now = DateTime.Now;
            if (item.IsRecurring && !item.Done)
            {
                // 반복 일정: 이번 회차만 완료하고 다음 회차로
                var dueRaw = item.DueRaw; var notified = item.Notified; var lastDone = item.LastDoneRaw; var snooze = item.SnoozeRaw;
                item.CompleteOccurrence(now);
                undoAction = () => { item.DueRaw = dueRaw; item.Notified = notified; item.LastDoneRaw = lastDone; item.SnoozeRaw = snooze; };
                Commit();
                var next = item.Due.Value;
                if (WidgetAlive)
                    widget.ShowUndo("'" + OneLine(item.Text) + "' 완료. 다음: " +
                        (TimeText.DayWord(next, now) ?? TimeText.ShortDate(next, now)) + " " + TimeText.HourMinute(next));
                return;
            }
            item.SetDone(!item.Done, now);
            Commit();
        }

        static string OneLine(string text)
        {
            return (text ?? "").Replace("\r", " ").Replace("\n", " ");
        }

        public void Update(TodoItem item, string text, DateTime? due, Repeat repeat)
        {
            item.Text = text;
            if (!due.HasValue) repeat = Repeat.None;
            if (item.Due != due || item.Repeat != repeat)
            {
                item.SetDue(due, DateTime.Now);
                item.Repeat = repeat;
            }
            Commit();
            CheckReminders();
        }

        public void Remove(TodoItem item)
        {
            int index = Data.Items.IndexOf(item);
            if (index < 0) return;
            Data.Items.RemoveAt(index);
            undoAction = () => { if (!Data.Items.Contains(item)) Data.Items.Insert(Math.Min(index, Data.Items.Count), item); };
            Commit();
            if (WidgetAlive) widget.ShowUndo("'" + OneLine(item.Text) + "' 지움");
        }

        public void ClearDone()
        {
            var done = Data.Items.Where(i => i.Done).ToList();
            if (done.Count == 0) return;
            Data.Items.RemoveAll(i => i.Done);
            undoAction = () => { foreach (var i in done) if (!Data.Items.Contains(i)) Data.Items.Add(i); };
            Commit();
            if (WidgetAlive) widget.ShowUndo("완료한 일 " + done.Count + "개를 지웠어요");
        }

        public void ToggleShowDone()
        {
            Settings.ShowDone = !Settings.ShowDone;
            Save();
            if (WidgetAlive) widget.RefreshList();
        }

        void Snooze(IList<TodoItem> items)
        {
            var at = DateTime.Now.AddMinutes(AlertForm.SnoozeMinutes);
            foreach (var item in items)
                if (!item.Done && Data.Items.Contains(item)) item.Snooze = at;
            Save();
        }

        // ── 테마 ──

        void SetTheme(string theme)
        {
            Settings.Theme = theme;
            Save();
            ApplyTheme();
        }

        /// <summary>지금 적용해야 할 모양(밝기 + 유리 + 강조 색 + 형광펜 색)을 한 줄로.</summary>
        string LookKey()
        {
            return Theme.ResolveDark(Settings.Theme) + "|" + Theme.ResolveGlass(Settings.Theme) + "|" + Settings.AccentColor + "|" + Settings.MarkerColor;
        }

        void ApplyColors()
        {
            Theme.Apply(Theme.ResolveDark(Settings.Theme), Settings.AccentColor, Settings.MarkerColor, Theme.ResolveGlass(Settings.Theme));
        }

        void SetColor(bool accent, string hex)
        {
            if (accent) Settings.AccentColor = hex; else Settings.MarkerColor = hex;
            Settings.Normalize();
            Save();
            ApplyTheme();
        }

        void PickColor(bool accent)
        {
            int current = ColorHex.ParseOr(accent ? Settings.AccentColor : Settings.MarkerColor, 0x1E7A57);
            using (var dialog = new ColorDialog
            {
                FullOpen = true,
                Color = Color.FromArgb((current >> 16) & 0xFF, (current >> 8) & 0xFF, current & 0xFF),
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                var c = dialog.Color;
                SetColor(accent, ColorHex.Format((c.R << 16) | (c.G << 8) | c.B));
            }
        }

        void ApplyTheme()
        {
            string key = LookKey();
            if (key == appliedLook) return;
            appliedLook = key;
            ApplyColors();
            Menu.BackColor = Theme.Surface;
            Menu.ForeColor = Theme.Text;
            foreach (var mi in new[] { miOpacity, miGlass, miLead, miTheme, miAccent, miMarker })
            {
                mi.DropDown.BackColor = Theme.Surface;
                mi.DropDown.ForeColor = Theme.Text;
            }
            // 색이 컨트롤마다 들어가 있으므로 위젯을 새로 만든다.
            if (WidgetAlive)
            {
                bool visible = widget.Visible;
                var old = widget;
                CreateWidget();
                if (visible) { widget.Show(); }
                old.Dispose();
            }
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General && (Settings.Theme == "system" || Settings.Theme == "glass"))
                invoker.BeginInvoke((Action)ApplyTheme);
        }

        public void Undo()
        {
            if (undoAction == null) return;
            var action = undoAction;
            undoAction = null;
            action();
            Commit();
        }

        void Commit()
        {
            Save();
            if (WidgetAlive) widget.RefreshList();
        }

        void Save()
        {
            try
            {
                storage.Save(Data);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                if (!saveErrorShown)
                {
                    saveErrorShown = true;
                    tray.ShowBalloonTip(8000, "저장 실패", "할 일을 저장하지 못했어요.\n" + ex.Message, ToolTipIcon.Error);
                }
            }
        }

        // ── 알림 ──

        void OnTick()
        {
            if (Exiting) return;
            EnsureWidget();
            CheckReminders();
            if (WidgetAlive) widget.Tick();
        }

        void CheckReminders()
        {
            if (Exiting) return;
            var now = DateTime.Now;
            if (Recurrence.RollForward(Data.Items, now))
            {
                Save();
                if (WidgetAlive) widget.RefreshList();
            }
            var alerts = Reminders.Collect(Data.Items, now, Settings.ReminderMinutes);
            if (alerts.Count == 0) return;
            Save();
            if (WidgetAlive) widget.RefreshList();

            try { SystemSounds.Exclamation.Play(); } catch (Exception) { }
            try
            {
                var form = new AlertForm(alerts, DateTime.Now);
                form.OpenRequested += ShowWidget;
                form.SnoozeRequested += Snooze;
                form.Show();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                var a = alerts[0];
                tray.ShowBalloonTip(15000, "할 일 알림", a.Item.Text, ToolTipIcon.Info);
            }
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                invoker.BeginInvoke((Action)OnTick);
        }

        void OnTimeChanged(object sender, EventArgs e)
        {
            invoker.BeginInvoke((Action)OnTick);
        }

        // ── 메뉴 ──

        void BuildMenu()
        {
            ToolStripManager.Renderer = new DarkMenuRenderer();
            Menu = Theme.CreateMenu();

            miToggle = AddItem(Menu.Items, "위젯 숨기기", ToggleWidget);
            Menu.Items.Add(new ToolStripSeparator());

            miTop = AddItem(Menu.Items, "항상 위에 표시", () =>
            {
                Settings.AlwaysOnTop = !Settings.AlwaysOnTop;
                Save();
                if (WidgetAlive) widget.ApplySettings();
            });

            miOpacity = SubMenu("투명도");
            foreach (var p in OpacityChoices)
            {
                int value = p;
                var mi = AddItem(miOpacity.DropDownItems, p == 100 ? "불투명" : p + "%", () =>
                {
                    Settings.OpacityPercent = value;
                    Save();
                    if (WidgetAlive) widget.ApplySettings();
                });
                mi.Tag = value;
            }
            Menu.Items.Add(miOpacity);

            // 리퀴드 글래스 테마에서만 보인다: 유리 뒤로 바탕화면이 얼마나 비칠지
            miGlass = SubMenu("유리 투명도");
            for (int i = 0; i < GlassChoices.Length; i++)
            {
                int value = GlassChoices[i];
                var mi = AddItem(miGlass.DropDownItems, GlassNames[i], () =>
                {
                    Settings.GlassTint = value;
                    Save();
                    if (WidgetAlive) widget.ApplySettings();
                });
                mi.Tag = value;
            }
            miGlass.DropDownItems.Add(new ToolStripSeparator());
            miGlassBlur = AddItem(miGlass.DropDownItems, "뒤 화면 흐리게", () =>
            {
                Settings.GlassBlur = !Settings.GlassBlur;
                Save();
                if (WidgetAlive) widget.ApplySettings();
            });
            Menu.Items.Add(miGlass);

            miLead = SubMenu("알림 시점");
            foreach (var m in LeadChoices)
            {
                int value = m;
                var mi = AddItem(miLead.DropDownItems, m < 60 ? m + "분 전" : (m / 60) + "시간 전", () =>
                {
                    Settings.ReminderMinutes = value;
                    Save();
                    if (WidgetAlive) { widget.ApplySettings(); widget.RefreshList(); }
                    CheckReminders();
                });
                mi.Tag = value;
            }
            Menu.Items.Add(miLead);

            miTheme = SubMenu("테마");
            var themes = new List<string[]> { new[] { "system", "Windows 설정 따르기" }, new[] { "light", "밝게" }, new[] { "dark", "어둡게" } };
            if (Theme.GlassSupported) themes.Add(new[] { "glass", "리퀴드 글래스" });
            foreach (var t in themes)
            {
                string value = t[0];
                var mi = AddItem(miTheme.DropDownItems, t[1], () => SetTheme(value));
                mi.Tag = value;
            }
            Menu.Items.Add(miTheme);

            miAccent = ColorMenu("강조 색", AccentChoices, true);
            miMarker = ColorMenu("형광펜 색", MarkerChoices, false);
            Menu.Items.Add(miAccent);
            Menu.Items.Add(miMarker);

            miAuto = AddItem(Menu.Items, "Windows 시작 시 자동 실행", () =>
            {
                try { AutoStart.SetEnabled(!AutoStart.IsEnabled()); }
                catch (Exception ex) { MessageBox.Show("자동 실행 설정을 바꾸지 못했어요.\n" + ex.Message, "일해라"); }
            });
            Menu.Items.Add(new ToolStripSeparator());

            miClearDone = AddItem(Menu.Items, "완료한 일 모두 지우기", ClearDone);
            AddItem(Menu.Items, "위치·크기 초기화", () => { ShowWidget(); widget.ResetPosition(); });
            AddItem(Menu.Items, "데이터 폴더 열기", () =>
            {
                try { Process.Start("explorer.exe", "\"" + storage.Directory + "\""); } catch (Exception) { }
            });
            Menu.Items.Add(new ToolStripSeparator());
            AddItem(Menu.Items, "종료", ExitApp);

            Menu.Opening += (s, e) =>
            {
                miToggle.Text = WidgetAlive && widget.Visible ? "위젯 숨기기" : "위젯 보이기";
                miTop.Checked = Settings.AlwaysOnTop;
                miAuto.Checked = AutoStart.IsEnabled();
                miClearDone.Enabled = Data.Items.Any(i => i.Done);
                foreach (ToolStripMenuItem mi in miOpacity.DropDownItems) mi.Checked = (int)mi.Tag == Settings.OpacityPercent;
                foreach (ToolStripMenuItem mi in miLead.DropDownItems) mi.Checked = (int)mi.Tag == Settings.ReminderMinutes;
                // 유리를 쓸 수 없는 PC 에서 "glass" 로 저장돼 있으면 시스템 설정처럼 보인다
                bool glass = Theme.ResolveGlass(Settings.Theme);
                string shown = Settings.Theme == "glass" && !glass ? "system" : Settings.Theme;
                foreach (ToolStripMenuItem mi in miTheme.DropDownItems) mi.Checked = (string)mi.Tag == shown;
                miGlass.Visible = glass;
                miOpacity.Visible = !glass; // 유리 테마에서는 유리 투명도가 대신한다
                int tint = Nearest(GlassChoices, Settings.GlassTintOrDefault);
                foreach (var mi in miGlass.DropDownItems.OfType<ToolStripMenuItem>()) mi.Checked = mi.Tag is int && (int)mi.Tag == tint;
                miGlassBlur.Checked = Settings.GlassBlur;
                CheckColor(miAccent, Settings.AccentColor);
                CheckColor(miMarker, Settings.MarkerColor);
            };
        }

        ToolStripMenuItem ColorMenu(string title, string[][] choices, bool accent)
        {
            var menu = SubMenu(title);
            foreach (var choice in choices)
            {
                string hex = choice[0];
                var mi = AddItem(menu.DropDownItems, choice[1], () => SetColor(accent, hex));
                mi.Tag = hex;
            }
            menu.DropDownItems.Add(new ToolStripSeparator());
            var custom = AddItem(menu.DropDownItems, "직접 고르기…", () => PickColor(accent));
            custom.Tag = "custom";
            return menu;
        }

        /// <summary>목록에서 값에 가장 가까운 것 (Mac 에서 슬라이더로 정한 값도 가까운 단계에 체크).</summary>
        static int Nearest(int[] choices, int value)
        {
            int best = choices[0];
            foreach (int c in choices)
                if (Math.Abs(c - value) < Math.Abs(best - value)) best = c;
            return best;
        }

        /// <summary>고른 색에 체크. 목록에 없는 색이면 "직접 고르기"에 체크.</summary>
        static void CheckColor(ToolStripMenuItem menu, string current)
        {
            bool matched = false;
            ToolStripMenuItem custom = null;
            foreach (ToolStripItem item in menu.DropDownItems)
            {
                var mi = item as ToolStripMenuItem;
                if (mi == null) continue;
                if ((string)mi.Tag == "custom") { custom = mi; continue; }
                mi.Checked = string.Equals((string)mi.Tag, current, StringComparison.OrdinalIgnoreCase);
                matched |= mi.Checked;
            }
            if (custom != null) custom.Checked = !matched;
        }

        ToolStripMenuItem SubMenu(string text)
        {
            var mi = new ToolStripMenuItem(text);
            var dd = (ToolStripDropDownMenu)mi.DropDown;
            dd.ShowImageMargin = false;
            dd.ShowCheckMargin = true;
            dd.BackColor = Theme.Surface;
            dd.ForeColor = Theme.Text;
            dd.Font = Menu.Font;
            return mi;
        }

        static ToolStripMenuItem AddItem(ToolStripItemCollection items, string text, Action action)
        {
            var mi = new ToolStripMenuItem(text);
            mi.Click += (s, e) => action();
            items.Add(mi);
            return mi;
        }

        // ── 종료 ──

        public void ExitApp()
        {
            if (Exiting) return;
            Exiting = true;
            timer.Stop();
            Save();
            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.TimeChanged -= OnTimeChanged;
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }
            catch (Exception) { }
            AlertForm.CloseAll();
            tray.Visible = false;
            tray.Dispose();
            if (WidgetAlive) widget.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tray.Dispose();
                invoker.Dispose();
                if (WidgetAlive) widget.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
