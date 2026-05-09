using System.Text.Json;
using AnketOtomasyonu.Models.ViewModels;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// Ders değerlendirme anketi akışındaki öğrenci oturumunu HTTP Session üzerinden yönetir.
    /// </summary>
    public static class CourseEvalSessionHelper
    {
        private const string SessionKey = "CourseEvalState";

        // ── Okuma / yazma / temizleme ─────────────────────────────────────────

        public static CourseEvalSessionState? Get(ISession session)
        {
            var json = session.GetString(SessionKey);
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<CourseEvalSessionState>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void Set(ISession session, CourseEvalSessionState state)
        {
            var json = JsonSerializer.Serialize(state);
            session.SetString(SessionKey, json);
        }

        public static void Clear(ISession session)
            => session.Remove(SessionKey);

        // ── Yardımcı: UserId üretimi ─────────────────────────────────────────

        /// <summary>
        /// Öğrenci numarası + ders anahtarından benzersiz bir UserId oluşturur.
        /// Aynı öğrencinin farklı dersler için ayrı yanıt verebilmesi sağlanır.
        /// Örnek: "OBIS|12345|MAT101|2025|"
        /// </summary>
        public static string BuildResponseUserId(string ogrNo, string courseKey)
            => $"OBIS|{ogrNo}|{courseKey}";
    }
}
