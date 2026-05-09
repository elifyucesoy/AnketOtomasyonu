using System.Security.Cryptography;
using System.Text;
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
        /// Öğrenci numarası + ders anahtarından benzersiz, kısa bir UserId üretir.
        /// courseKey uzun olabildiğinden (ders adı vb.) doğrudan birleştirilmez; DB'de UserId nvarchar sınırına takılmamak için özet kullanılır.
        /// </summary>
        public static string BuildResponseUserId(string ogrNo, string courseKey)
        {
            var on = (ogrNo ?? "").Trim();
            var ck = courseKey ?? "";
            var payload = Encoding.UTF8.GetBytes($"{on}\u001f{ck}");
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(payload, hash);
            // 12 bayt hex = 24 karakter; ör. OBIS:223301156:... ≈ 36 karakter (< 128)
            var hex = Convert.ToHexString(hash[..12]);
            return $"OBIS:{on}:{hex}";
        }
    }
}
