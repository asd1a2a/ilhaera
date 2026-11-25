using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace TodoWidget.Core
{
    [DataContract]
    public sealed class TodoItem
    {
        const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";

        [DataMember(Name = "id", Order = 0)] public string Id { get; set; }
        [DataMember(Name = "text", Order = 1)] public string Text { get; set; }
        [DataMember(Name = "done", Order = 2)] public bool Done { get; set; }
        [DataMember(Name = "due", Order = 3, EmitDefaultValue = false)] public string DueRaw { get; set; }
        [DataMember(Name = "notified", Order = 4)] public bool Notified { get; set; }
        [DataMember(Name = "created", Order = 5)] public string CreatedRaw { get; set; }
        [DataMember(Name = "completed", Order = 6, EmitDefaultValue = false)] public string CompletedRaw { get; set; }
        [DataMember(Name = "snooze", Order = 7, EmitDefaultValue = false)] public string SnoozeRaw { get; set; }
        /// <summary>null | "daily" | "weekdays" | "weekly"</summary>
        [DataMember(Name = "repeat", Order = 8, EmitDefaultValue = false)] public string RepeatRaw { get; set; }
        [DataMember(Name = "lastDone", Order = 9, EmitDefaultValue = false)] public string LastDoneRaw { get; set; }

        /// <summary>로컬 시각 기준 마감/일정 시각 (분 단위).</summary>
        public DateTime? Due
        {
            get { return Parse(DueRaw); }
            set { DueRaw = Format(value); }
        }

        public DateTime Created
        {
            get { return Parse(CreatedRaw) ?? DateTime.MinValue; }
            set { CreatedRaw = Format(value); }
        }

        public DateTime? Completed
        {
            get { return Parse(CompletedRaw); }
            set { CompletedRaw = Format(value); }
        }

        /// <summary>"다시 알림" 시각. 이 시각이 지나면 한 번 더 알린다.</summary>
        public DateTime? Snooze
        {
            get { return Parse(SnoozeRaw); }
            set { SnoozeRaw = Format(value); }
        }

        public Repeat Repeat
        {
            get { return Recurrence.Parse(RepeatRaw); }
            set { RepeatRaw = Recurrence.ToRaw(value); }
        }

        /// <summary>반복 일정이 마지막으로 완료된 시각.</summary>
        public DateTime? LastDone
        {
            get { return Parse(LastDoneRaw); }
            set { LastDoneRaw = Format(value); }
        }

        public bool IsRecurring { get { return Repeat != Repeat.None && Due.HasValue; } }

        /// <summary>반복 일정의 이번 회차를 완료하고 다음 회차로 넘긴다.</summary>
        public void CompleteOccurrence(DateTime now)
        {
            if (!IsRecurring) return;
            LastDone = now;
            var next = Recurrence.NextAfter(Repeat, Due.Value);
            if (next.Date < now.Date)
                next = Recurrence.FirstOnOrAfterDate(Repeat, now.Date, next.TimeOfDay, next.DayOfWeek);
            SetDue(next, now);
        }

        public static TodoItem Create(string text, DateTime? due, DateTime now, Repeat repeat)
        {
            var item = Create(text, due, now);
            if (due.HasValue && repeat != Repeat.None) item.Repeat = repeat;
            return item;
        }

        public static TodoItem Create(string text, DateTime? due, DateTime now)
        {
            var item = new TodoItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                Created = now,
            };
            item.SetDue(due, now);
            return item;
        }

        /// <summary>일정 시각을 바꾸고 알림 상태를 다시 계산한다. 이미 지난 시각이면 알림을 띄우지 않는다.</summary>
        public void SetDue(DateTime? due, DateTime now)
        {
            if (due.HasValue)
                due = new DateTime(due.Value.Year, due.Value.Month, due.Value.Day, due.Value.Hour, due.Value.Minute, 0);
            Due = due;
            Snooze = null;
            Notified = due.HasValue && due.Value <= now;
        }

        public void SetDone(bool done, DateTime now)
        {
            Done = done;
            Completed = done ? now : (DateTime?)null;
            if (done) Snooze = null;
        }

        static DateTime? Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            DateTime dt;
            if (DateTime.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;
            return null;
        }

        static string Format(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : null;
        }
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Name = "x", Order = 0)] public int? X { get; set; }
        [DataMember(Name = "y", Order = 1)] public int? Y { get; set; }
        [DataMember(Name = "width", Order = 2)] public int? Width { get; set; }
        [DataMember(Name = "height", Order = 3)] public int? Height { get; set; }
        [DataMember(Name = "alwaysOnTop", Order = 4)] public bool AlwaysOnTop { get; set; }
        [DataMember(Name = "opacity", Order = 5)] public int OpacityPercent { get; set; }
        [DataMember(Name = "reminderMinutes", Order = 6)] public int ReminderMinutes { get; set; }
        [DataMember(Name = "autoStartInitialized", Order = 7)] public bool AutoStartInitialized { get; set; }
        [DataMember(Name = "trayHintShown", Order = 8)] public bool TrayHintShown { get; set; }
        /// <summary>"system" | "light" | "dark" | "glass"(리퀴드 글래스)</summary>
        [DataMember(Name = "theme", Order = 9)] public string Theme { get; set; }
        [DataMember(Name = "showDone", Order = 10)] public bool ShowDone { get; set; }
        /// <summary>강조 색 (체크·버튼·진행률), "#RRGGBB"</summary>
        [DataMember(Name = "accentColor", Order = 11)] public string AccentColor { get; set; }
        /// <summary>형광펜 색 (곧 시작할 일), "#RRGGBB"</summary>
        [DataMember(Name = "markerColor", Order = 12)] public string MarkerColor { get; set; }
        /// <summary>리퀴드 글래스의 유리 투명도: 0(아주 투명) ~ 100(불투명). 정한 적이 없으면 null.</summary>
        [DataMember(Name = "glassTint", Order = 13, EmitDefaultValue = false)] public int? GlassTint { get; set; }

        public const int DefaultGlassTint = 35;
        public int GlassTintOrDefault { get { return GlassTint ?? DefaultGlassTint; } }

        public AppSettings() { SetDefaults(); }

        [OnDeserializing]
        void OnDeserializing(StreamingContext context) { SetDefaults(); }

        void SetDefaults()
        {
            OpacityPercent = 97;
            ReminderMinutes = 30;
            Theme = "system";
            AccentColor = ColorHex.DefaultAccent;
            MarkerColor = ColorHex.DefaultMarker;
        }

        public void Normalize()
        {
            if (OpacityPercent < 30 || OpacityPercent > 100) OpacityPercent = 97;
            if (Theme != "light" && Theme != "dark" && Theme != "glass") Theme = "system";
            if (GlassTint.HasValue && (GlassTint.Value < 0 || GlassTint.Value > 100)) GlassTint = null;
            int rgb;
            if (!ColorHex.TryParse(AccentColor, out rgb)) AccentColor = ColorHex.DefaultAccent;
            if (!ColorHex.TryParse(MarkerColor, out rgb)) MarkerColor = ColorHex.DefaultMarker;
            if (ReminderMinutes < 1 || ReminderMinutes > 24 * 60) ReminderMinutes = 30;
        }
    }

    [DataContract]
    public sealed class AppData
    {
        [DataMember(Name = "version", Order = 0)] public int Version { get; set; }
        [DataMember(Name = "items", Order = 1)] public List<TodoItem> Items { get; set; }
        [DataMember(Name = "settings", Order = 2)] public AppSettings Settings { get; set; }

        public AppData()
        {
            Version = 1;
            Items = new List<TodoItem>();
            Settings = new AppSettings();
        }

        [OnDeserialized]
        void OnDeserialized(StreamingContext context) { Normalize(); }

        public void Normalize()
        {
            if (Version <= 0) Version = 1;
            if (Items == null) Items = new List<TodoItem>();
            Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Text));
            foreach (var item in Items)
            {
                if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrEmpty(item.CreatedRaw)) item.Created = DateTime.Now;
            }
            if (Settings == null) Settings = new AppSettings();
            Settings.Normalize();
        }
    }
}
