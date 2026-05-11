using System.Globalization;
using System.Linq;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.Obis;

namespace AnketOtomasyonu.Configuration
{
    /// <summary>
    /// appsettings.json → "CourseEvaluation" bölümü.
    /// Hangi anketlerin OBIS kimlik doğrulama + ders seçim akışını kullanacağını belirler.
    /// </summary>
    public class CourseEvaluationOptions
    {
        public const string Section = "CourseEvaluation";

        /// <summary>
        /// true ise OBIS ders listesi yalnızca yapılandırılan veya otomatik hesaplanan yıl/dönem ile eşleşen satırları gösterir.
        /// </summary>
        public bool ObisOnlyCurrentTerm { get; set; } = true;

        /// <summary>OBIS <c>YIL</c> ile birebir eşleşme (örn. 2026). Boşsa tarihe göre tahmin edilir.</summary>
        public string? ObisCourseYear { get; set; }

        /// <summary>OBIS <c>DONEM</c> ile eşleşme (çoğu kurumda 1=Güz, 2=Bahar). Boşsa tarihe göre tahmin edilir.</summary>
        public string? ObisCourseDonem { get; set; }

        /// <summary>
        /// (Geriye dönük uyumluluk) Anket başlığı bu anahtar kelimeyi içeriyorsa
        /// ve SurveyType hâlâ Normal ise OBIS akışına yönlendirilir.
        /// Boş bırakılırsa yalnızca SurveyType kontrolü yapılır.
        /// </summary>
        public string? ObisFlowTitleKeyword { get; set; }

        /// <summary>
        /// Bu ankete OBIS kimlik + ders listesi akışı uygulanmalı mı?
        /// Öncelikle SurveyType'a bakar; Normal ise eski keyword kontrolü devreye girer.
        /// </summary>
        public bool UseObisParticipantFlow(Survey survey)
        {
            // SurveyType açıkça CourseEvaluation ise direkt OBIS akışı
            if (survey.SurveyType == SurveyType.CourseEvaluation)
                return true;

            // Geriye dönük uyumluluk: eski anketlerde başlık keyword kontrolü
            if (!string.IsNullOrWhiteSpace(ObisFlowTitleKeyword)
                && survey.Title.Contains(ObisFlowTitleKeyword, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>OBIS’ten gelen dersleri güncel dönem filtresinden geçirir.</summary>
        public List<ObisCourseRow> FilterObisCoursesForCurrentTerm(IReadOnlyList<ObisCourseRow> courses)
        {
            if (!ObisOnlyCurrentTerm || courses == null || courses.Count == 0)
                return courses?.ToList() ?? new List<ObisCourseRow>();

            var targetYear = !string.IsNullOrWhiteSpace(ObisCourseYear)
                ? ObisCourseYear!.Trim()
                : SuggestObisYearForCurrentDate();
            var targetDonem = !string.IsNullOrWhiteSpace(ObisCourseDonem)
                ? ObisCourseDonem!.Trim()
                : SuggestObisDonemForCurrentDate();

            return courses.Where(c => MatchesObisTermRow(c, targetYear, targetDonem)).ToList();
        }

        /// <summary>Türkiye tipik akademik takvime göre tahmin: Eylül–Aralık ve Ocak → Güz (yıl kurallı); Şubat–Ağustos → Bahar.</summary>
        public static string SuggestObisDonemForCurrentDate()
        {
            var m = DateTime.Now.Month;
            return m >= 9 || m == 1 ? "1" : "2";
        }

        public static string SuggestObisYearForCurrentDate()
        {
            var now = DateTime.Now;
            // Güz (Eylül–Aralık): çoğu OBIS kaydında yıl = akademik yılın başlangıcı
            if (now.Month >= 9)
                return now.Year.ToString(CultureInfo.InvariantCulture);
            // Ocak: aynı güz döneminin devamı → genelde bir önceki yıl etiketi
            if (now.Month == 1)
                return (now.Year - 1).ToString(CultureInfo.InvariantCulture);
            // Bahar (Şubat–Ağustos): kuruma göre değişir; varsayılan takvim yılı (appsettings ile ezin)
            return now.Year.ToString(CultureInfo.InvariantCulture);
        }

        private static bool MatchesObisTermRow(ObisCourseRow c, string targetYear, string targetDonem)
        {
            if (string.IsNullOrWhiteSpace(c.Yil))
                return false;

            if (!YearEquals(c.Yil.Trim(), targetYear))
                return false;

            if (string.IsNullOrWhiteSpace(c.Donem))
                return true;

            return DonemEquals(c.Donem.Trim(), targetDonem);
        }

        private static bool YearEquals(string courseYil, string targetYear)
        {
            if (string.Equals(courseYil, targetYear, StringComparison.Ordinal))
                return true;
            return int.TryParse(courseYil, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yc)
                   && int.TryParse(targetYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yt)
                   && yc == yt;
        }

        private static bool DonemEquals(string courseDonem, string targetDonem)
        {
            if (string.Equals(courseDonem, targetDonem, StringComparison.OrdinalIgnoreCase))
                return true;
            if (int.TryParse(courseDonem, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cd)
                && int.TryParse(targetDonem, NumberStyles.Integer, CultureInfo.InvariantCulture, out var td)
                && cd == td)
                return true;

            var cNorm = NormalizeDonemLabel(courseDonem);
            var tNorm = NormalizeDonemLabel(targetDonem);
            return cNorm != null && tNorm != null && string.Equals(cNorm, tNorm, StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeDonemLabel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (s == "1" || s.Contains("güz", StringComparison.OrdinalIgnoreCase)
                        || s.Contains("guz", StringComparison.OrdinalIgnoreCase))
                return "guz";
            if (s == "2" || s.Contains("bahar", StringComparison.OrdinalIgnoreCase))
                return "bahar";
            return s.Trim().ToLowerInvariant();
        }
    }
}
