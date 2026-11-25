using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace TodoWidget.UI
{
    /// <summary>
    /// 디자인 토큰. "종이(밝게)"와 "먹(어둡게)" 두 팔레트.
    /// 형광펜 = 곧 시작, 빨간펜 = 지난 일정, 초록 잉크 = 완료·강조, 토요일 파랑 / 일요일 빨강 (한국 달력 관례).
    /// </summary>
    static class Theme
    {
        public static bool IsDark { get; private set; }
        /// <summary>리퀴드 글래스: 위젯 바탕(Back)이 뚫려서 뒤에 깔린 유리판이 비친다.</summary>
        public static bool IsGlass { get; private set; }

        public static Color Back { get; private set; }        // 위젯 바탕
        public static Color Surface { get; private set; }     // 입력칸
        public static Color Hover { get; private set; }       // 줄 hover
        public static Color Pressed { get; private set; }
        public static Color Raised { get; private set; }      // 판 위에 떠 있는 조각 (선택된 오전/오후)
        public static Color Border { get; private set; }
        public static Color Text { get; private set; }
        public static Color SubText { get; private set; }
        public static Color Faint { get; private set; }
        public static Color Accent { get; private set; }      // 초록 잉크
        public static Color AccentHover { get; private set; }
        public static Color OnAccent { get; private set; }
        public static Color Marker { get; private set; }      // 형광펜 바탕
        public static Color MarkerText { get; private set; }  // 형광펜 위 글자
        public static Color Warn { get; private set; }
        public static Color Danger { get; private set; }      // 빨간펜, 일요일
        public static Color Saturday { get; private set; }

        public static string UiFont { get; private set; }
        public static string NumFont { get; private set; }     // 숫자용 (Bahnschrift)
        public static string NumFontBold { get; private set; }

        static Theme() { Apply(false); }

        public static void Apply(bool dark)
        {
            Apply(dark, Core.ColorHex.DefaultAccent, Core.ColorHex.DefaultMarker);
        }

        /// <summary>테마와 사용자가 고른 강조 색·형광펜 색으로 색 한 벌을 만든다.</summary>
        public static void Apply(bool dark, string accentHex, string markerHex, bool glass = false)
        {
            ApplyBase(dark);
            IsGlass = glass;
            if (glass) ApplyGlass(dark);

            // 강조 색: 어두운 바탕에서는 조금 밝게
            int accentBase = Core.ColorHex.ParseOr(accentHex, 0x1E7A57);
            int accent = accentBase;
            if (dark) accent = accentBase == 0x1E7A57 ? 0x46C68A : Core.ColorHex.Mix(accentBase, 0xFFFFFF, 0.28);
            Accent = Rgb(accent);
            AccentHover = Rgb(Core.ColorHex.Mix(accent, 0xFFFFFF, 0.14));
            OnAccent = Core.ColorHex.Luminance(accent) > 0.6 ? Rgb(0x0E1A14) : Color.White;

            // 형광펜: 밝은 바탕에서는 그 색 그대로, 어두운 바탕에서는 어둡게 깔고 글자를 그 색으로
            int markerBase = Core.ColorHex.ParseOr(markerHex, 0xFFEC85);
            if (dark)
            {
                Marker = Rgb(Core.ColorHex.Mix(markerBase, 0x1E2023, 0.72));
                MarkerText = Rgb(Core.ColorHex.Mix(markerBase, 0xFFFFFF, 0.15));
            }
            else
            {
                Marker = Rgb(markerBase);
                MarkerText = Core.ColorHex.Luminance(markerBase) > 0.5 ? Rgb(Core.ColorHex.Mix(markerBase, 0x000000, 0.74)) : Color.White;
            }
        }

        static void ApplyBase(bool dark)
        {
            IsDark = dark;
            if (!dark)
            {
                Back = Rgb(0xFFFFFF); Surface = Rgb(0xF1F3F5); Hover = Rgb(0xF5F6F8); Pressed = Rgb(0xE6E9ED); Raised = Back;
                Border = Rgb(0xDDE1E6); Text = Rgb(0x1D2127); SubText = Rgb(0x5F6774); Faint = Rgb(0x9AA1AC);
                Accent = Rgb(0x1E7A57); AccentHover = Rgb(0x248F66); OnAccent = Color.White;
                Marker = Rgb(0xFFEC85); MarkerText = Rgb(0x4D3B00);
                Warn = Rgb(0xA86200); Danger = Rgb(0xD63B3B); Saturday = Rgb(0x2F6BD8);
            }
            else
            {
                Back = Rgb(0x1E2023); Surface = Rgb(0x2A2D31); Hover = Rgb(0x26292D); Pressed = Rgb(0x34383D); Raised = Back;
                Border = Rgb(0x383C42); Text = Rgb(0xECEEF0); SubText = Rgb(0x9EA5AF); Faint = Rgb(0x6C737D);
                Accent = Rgb(0x46C68A); AccentHover = Rgb(0x5DD49C); OnAccent = Rgb(0x0E1A14);
                Marker = Rgb(0x4C4320); MarkerText = Rgb(0xFFE27A);
                Warn = Rgb(0xFFC24D); Danger = Rgb(0xFF6B6B); Saturday = Rgb(0x74A3FF);
            }
        }

        /// <summary>
        /// 유리 테마: Back 은 "뚫는 색"이라 그 색으로 칠한 곳은 화면에 나오지 않고 유리가 비친다.
        /// 다른 색과 겹치면 안 되므로 흰색/검정에서 살짝 비킨 색을 쓴다. 판(Surface)과 글자는 불투명하게 남는다.
        /// </summary>
        static void ApplyGlass(bool dark)
        {
            if (!dark)
            {
                Back = Rgb(0xFAFBFC);
                Surface = Rgb(0xFFFFFF); Hover = Rgb(0xF1F4F8); Pressed = Rgb(0xE2E7ED); Raised = Rgb(0xE9EEF4);
                Border = Rgb(0xCDD4DD); Text = Rgb(0x14171C); SubText = Rgb(0x3B424D); Faint = Rgb(0x687080);
                Warn = Rgb(0x8F5200); Danger = Rgb(0xC62F2F); Saturday = Rgb(0x2459C4);
            }
            else
            {
                Back = Rgb(0x1C1D1F);
                Surface = Rgb(0x2B2E33); Hover = Rgb(0x34383E); Pressed = Rgb(0x41464D); Raised = Rgb(0x454A52);
                Border = Rgb(0x4D525A); Text = Rgb(0xF4F5F7); SubText = Rgb(0xC9CED6); Faint = Rgb(0x9299A4);
                Warn = Rgb(0xFFC24D); Danger = Rgb(0xFF7A7A); Saturday = Rgb(0x8AB2FF);
            }
        }

        /// <summary>
        /// 그리기 전에 부른다. 유리 위 글자는 색 번짐(ClearType) 없이 회색조로 다듬어야 가장자리가 깨끗하다.
        /// </summary>
        public static void Prepare(Graphics g)
        {
            if (IsGlass) g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        static Color Rgb(int v) { return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF); }

        /// <summary>Windows "앱 모드" (설정 → 개인 설정 → 색) 가 어둡게인지.</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v == 0;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>이 PC 에서 유리 테마를 쓸 수 있는지 (Windows 10 1803 이상).</summary>
        public static bool GlassSupported { get { return Native.IsWindows && Native.Build >= 17134; } }

        public static bool ResolveGlass(string setting) { return setting == "glass" && GlassSupported; }

        public static bool ResolveDark(string setting)
        {
            if (setting == "dark") return true;
            if (setting == "light") return false;
            return SystemPrefersDark();
        }

        public static Color WeekdayColor(DateTime d)
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) return Danger;
            if (d.DayOfWeek == DayOfWeek.Saturday) return Saturday;
            return Text;
        }

        public static float Scale { get; private set; } = 1f;

        public static void Init()
        {
            UiFont = "Malgun Gothic";
            NumFont = FirstInstalled("Bahnschrift", "Segoe UI", UiFont);
            NumFontBold = FirstInstalled("Bahnschrift SemiBold", NumFont);
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch (Exception) { Scale = 1f; }
#if SCREENSHOT
            Scale = 2f; // 소개 페이지용 2배 스크린샷 빌드 전용
#endif
        }

        public static int Px(float v) { return (int)Math.Round(v * Scale); }

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
#if SCREENSHOT
            size *= 2f;
#endif
            return new Font(UiFont ?? "Malgun Gothic", size, style, GraphicsUnit.Point);
        }

        /// <summary>날짜·시각 숫자용 글꼴.</summary>
        public static Font Num(float size, bool bold = false)
        {
            var name = bold ? NumFontBold : NumFont;
            var style = bold && name == NumFont ? FontStyle.Bold : FontStyle.Regular;
#if SCREENSHOT
            size *= 2f;
#endif
            return new Font(name ?? "Segoe UI", size, style, GraphicsUnit.Point);
        }

        static string FirstInstalled(params string[] names)
        {
            try
            {
                using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                {
                    var set = new System.Collections.Generic.HashSet<string>(
                        fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                    foreach (var n in names)
                        if (set.Contains(n)) return n;
                }
            }
            catch (Exception) { }
            return names[names.Length - 1];
        }

        public static Icon LoadIcon(Size size)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TodoWidget.app.ico"))
                {
                    if (s != null) return new Icon(s, size);
                }
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = RoundRect(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
        {
            using (var path = RoundRect(r, radius))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, path);
        }

        public static ContextMenuStrip CreateMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                BackColor = Surface,
                ForeColor = Text,
                Font = Font(9.5f),
            };
            return menu;
        }
    }

    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.SubText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Math.Min(r.Width, r.Height) / 2f;
            using (var pen = new Pen(Theme.Accent, Math.Max(1.6f, s / 3.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - s * 0.6f, cy),
                    new PointF(cx - s * 0.15f, cy + s * 0.45f),
                    new PointF(cx + s * 0.65f, cy - s * 0.5f),
                });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var r = e.Item.ContentRectangle;
            int y = r.Y + r.Height / 2;
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawLine(pen, r.X + Theme.Px(6), y, r.Right - Theme.Px(6), y);
        }

        sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.Pressed; } }
            public override Color MenuItemSelected { get { return Theme.Pressed; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.Pressed; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.Pressed; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.Pressed; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.Pressed; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
            public override Color CheckBackground { get { return Theme.Surface; } }
            public override Color CheckSelectedBackground { get { return Theme.Pressed; } }
            public override Color CheckPressedBackground { get { return Theme.Pressed; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
        }
    }
}
