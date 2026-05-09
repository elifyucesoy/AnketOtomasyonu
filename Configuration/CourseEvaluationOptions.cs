using AnketOtomasyonu.Models.Entities;

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
    }
}
