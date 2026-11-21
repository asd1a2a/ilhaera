using System;
using System.Drawing;
using System.Windows.Forms;
using TodoWidget.Core;

namespace TodoWidget.UI
{
    /// <summary>할 일 수정 대화상자.</summary>
    sealed class EditDialog : Form
    {
        readonly RoundPanel textPanel;
        readonly TextBox textBox;
        readonly CheckBox dueCheck;
        readonly TimePicker picker;
        readonly FlatButton okButton, cancelButton;

        public string ResultText { get; private set; }
        public DateTime? ResultDue { get; private set; }
        public Repeat ResultRepeat { get; private set; }

        static int Px(float v) { return Theme.Px(v); }

        public EditDialog(string text, DateTime? due, Repeat repeat, int leadMinutes)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "할 일 수정";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            KeyPreview = true;

            textPanel = new RoundPanel { BackColor = Theme.Back, FillColor = Theme.Surface, Radius = Px(8) };
            textBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Multiline = true,
                WordWrap = true,
                AcceptsReturn = true,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Font(10.5f),
                MaxLength = 500,
                Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
            };
            textPanel.Controls.Add(textBox);

            dueCheck = new CheckBox
            {
                Text = "시간 정하기",
                AutoSize = true,
                Checked = due.HasValue,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Text,
                BackColor = Theme.Back,
                Font = Theme.Font(9.5f),
                Cursor = Cursors.Hand,
            };
            dueCheck.FlatAppearance.BorderColor = Theme.SubText;
            dueCheck.FlatAppearance.CheckedBackColor = Theme.Accent;

            picker = new TimePicker { Visible = due.HasValue, ReminderMinutes = leadMinutes };
            picker.SetValue(due ?? TimeText.DefaultDue(DateTime.Now), due.HasValue ? repeat : Repeat.None);
            picker.Commit += () => BeginInvoke((Action)Save);
            picker.Cancel += () => { DialogResult = DialogResult.Cancel; };

            okButton = new FlatButton { Text = "저장", Primary = true, Font = Theme.Font(9.5f, FontStyle.Bold), ForeColor = Theme.OnAccent };
            cancelButton = new FlatButton { Text = "취소", ForeColor = Theme.SubText, Font = Theme.Font(9.5f) };

            Controls.AddRange(new Control[] { textPanel, dueCheck, picker, okButton, cancelButton });

            dueCheck.CheckedChanged += (s, e) =>
            {
                picker.Visible = dueCheck.Checked;
                UpdateSize();
                if (dueCheck.Checked) picker.Focus();
            };
            okButton.Click += (s, e) => Save();
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            UpdateSize();
        }

        int Margin16 { get { return Px(18); } }

        void UpdateSize()
        {
            int m = Margin16;
            int h = m + Px(78) + Px(12) + Px(24) + (picker.Visible ? Px(10) + picker.PreferredHeight : 0) + Px(18) + Px(34) + m;
            ClientSize = new Size(Px(380), h);
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateSize();
            if (Owner != null)
            {
                var o = Owner.Bounds;
                var wa = Screen.FromControl(Owner).WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(o.Left + (o.Width - Width) / 2, wa.Right - Width));
                int y = Math.Max(wa.Top, Math.Min(o.Top + Theme.Px(60), wa.Bottom - Height));
                StartPosition = FormStartPosition.Manual;
                Location = new Point(x, y);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            textBox.Focus();
            textBox.SelectionStart = textBox.TextLength;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (okButton == null) return;
            int m = Margin16, w = ClientSize.Width - m * 2, y = m;
            textPanel.SetBounds(m, y, w, Px(78));
            textBox.SetBounds(Px(10), Px(8), w - Px(20), Px(62));
            y = textPanel.Bottom + Px(12);
            dueCheck.Location = new Point(m, y);
            y = dueCheck.Bottom + Px(10);
            picker.SetBounds(m, y, w, picker.PreferredHeight);
            int bh = Px(34), bw = Px(84);
            okButton.SetBounds(ClientSize.Width - m - bw, ClientSize.Height - m - bh, bw, bh);
            cancelButton.SetBounds(okButton.Left - bw - Px(8), okButton.Top, bw, bh);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (picker.Focused) return; // 시간 선택기가 직접 처리
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.Cancel;
            }
            else if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                BeginInvoke((Action)Save);
            }
        }

        void Save()
        {
            var text = (textBox.Text ?? "").Trim();
            if (text.Length == 0)
            {
                textPanel.BorderColor = Theme.Danger;
                textPanel.Invalidate();
                textBox.Focus();
                return;
            }
            DateTime? due = null;
            Repeat repeat = Repeat.None;
            if (dueCheck.Checked) due = picker.GetValue(out repeat);
            ResultText = text;
            ResultDue = due;
            ResultRepeat = repeat;
            DialogResult = DialogResult.OK;
        }
    }
}
