using System;
using System.Globalization;

namespace TodoWidget.Core
{
    /// <summary>"#RRGGBB" 색 문자열과 간단한 색 계산.</summary>
    public static class ColorHex
    {
        public const string DefaultAccent = "#1E7A57";
        public const string DefaultMarker = "#FFEC85";

        public static bool TryParse(string text, out int rgb)
        {
            rgb = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();
            if (t.StartsWith("#", StringComparison.Ordinal)) t = t.Substring(1);
            return t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
        }

        public static int ParseOr(string text, int fallback)
        {
            int rgb;
            return TryParse(text, out rgb) ? rgb : fallback;
        }

        public static string Format(int rgb)
        {
            return "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
        }

        /// <summary>사람 눈에 느껴지는 밝기 (0 어두움 ~ 1 밝음).</summary>
        public static double Luminance(int rgb)
        {
            double r = ((rgb >> 16) & 0xFF) / 255.0, g = ((rgb >> 8) & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        /// <summary>a 에서 b 쪽으로 amount(0~1) 만큼 섞은 색.</summary>
        public static int Mix(int a, int b, double amount)
        {
            Func<int, int> ch = shift =>
            {
                double x = (a >> shift) & 0xFF, y = (b >> shift) & 0xFF;
                return (int)Math.Round(x + (y - x) * amount) & 0xFF;
            };
            return (ch(16) << 16) | (ch(8) << 8) | ch(0);
        }
    }
}
