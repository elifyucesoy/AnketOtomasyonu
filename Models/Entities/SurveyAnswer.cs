namespace AnketOtomasyonu.Models.Entities
{
    public class SurveyAnswer
    {
        public int Id { get; set; }
        public int SurveyResponseId { get; set; }
        public int QuestionId { get; set; }
        public int? SelectedOptionId { get; set; }
        public string? OpenEndedAnswer { get; set; }

        public SurveyResponse SurveyResponse { get; set; } = null!;
        public Question Question { get; set; } = null!;
        public QuestionOption? SelectedOption { get; set; }
    }
}