using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TodoWidget
{
    /// <summary>HKCU\...\Run 레지스트리로 Windows 로그인 시 자동 실행 (관리자 권한 불필요).</summary>
    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "TodoWidget";

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(ValueName) != null;
            }
            catch (Exception) { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, Command);
                else key.DeleteValue(ValueName, false);
            }
        }

        /// <summary>exe를 다른 폴더로 옮긴 뒤 실행했을 때 등록 경로를 갱신.</summary>
        public static void RefreshPath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    var current = key.GetValue(ValueName) as string;
                    if (current != null && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(ValueName, Command);
                }
            }
            catch (Exception) { }
        }
    }
}
