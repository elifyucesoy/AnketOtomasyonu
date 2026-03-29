namespace AnketOtomasyonu.Models.Entities
{
    public class SurveyResponse
    {
        public int Id { get; set; }
        public int SurveyId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public string? IpAddress { get; set; }

        /// <summary>Katılımcının tam adı</summary>
        public string? UserFullName { get; set; }
        /// <summary>Öğrenci fakültesi / personel birimi</summary>
        public string? FakulteAdi { get; set; }
        /// <summary>Öğrenci bölümü</summary>
        public string? BolumAdi { get; set; }

        public Survey Survey { get; set; } = null!;
        public ICollection<SurveyAnswer> Answers { get; set; } = new List<SurveyAnswer>();
    }
}