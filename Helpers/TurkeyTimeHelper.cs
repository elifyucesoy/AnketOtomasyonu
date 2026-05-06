namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// Türkiye saatine (UTC+3) çevirme yardımcısı
    /// </summary>
    public static class TurkeyTimeHelper
    {
        private static readonly TimeZoneInfo _turkeyZone;

        static TurkeyTimeHelper()
        {
            try
            {
                // Windows: "Turkey Standard Time", Linux/Mac: "Europe/Istanbul"
                _turkeyZone = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
            }
            catch
            {
                try { _turkeyZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
                catch { _turkeyZone = TimeZoneInfo.Utc; }
            }
        }

        /// <summary>UTC DateTime'ı Türkiye saatine çevirir</summary>
        public static DateTime ToTurkey(this DateTime utcDateTime)
        {
            if (utcDateTime.Kind == DateTimeKind.Local)
                return utcDateTime; // Zaten local ise dokunma
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), _turkeyZone);
        }

        /// <summary>Türkiye saatine çevirip formatlar: dd.MM.yyyy</summary>
        public static string ToTurkeyDate(this DateTime utcDateTime)
            => utcDateTime.ToTurkey().ToString("dd.MM.yyyy");

        /// <summary>Türkiye saatine çevirip formatlar: dd.MM.yyyy HH:mm</summary>
        public static string ToTurkeyDateTime(this DateTime utcDateTime)
            => utcDateTime.ToTurkey().ToString("dd.MM.yyyy HH:mm");

        /// <summary>Nullable DateTime için</summary>
        public static string ToTurkeyDate(this DateTime? utcDateTime, string fallback = "-")
            => utcDateTime.HasValue ? utcDateTime.Value.ToTurkeyDate() : fallback;

        public static string ToTurkeyDateTime(this DateTime? utcDateTime, string fallback = "-")
            => utcDateTime.HasValue ? utcDateTime.Value.ToTurkeyDateTime() : fallback;
    }
}
