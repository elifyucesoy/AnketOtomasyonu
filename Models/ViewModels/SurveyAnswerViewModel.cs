namespace AnketOtomasyonu.Models.ViewModels
{
    /// <summary>Anket sonuçları görüntüleme sayfası</summary>
    public class SurveyAnswerViewModel
    {
        public int SurveyId { get; set; }
        public string SurveyTitle { get; set; } = string.Empty;
        public int TotalResponses { get; set; }

        public List<QuestionResultViewModel> QuestionResults { get; set; } = new();
    }

    public class QuestionResultViewModel
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string QuestionType { get; set; } = string.Empty;

        /// <summary>Likert soruları için ortalama puan (1-5)</summary>
        public double? AverageScore { get; set; }

        public List<OptionResultViewModel> OptionResults { get; set; } = new();
        public List<string> OpenEndedAnswers { get; set; } = new();
    }

    public class OptionResultViewModel
    {
        public string OptionText { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}