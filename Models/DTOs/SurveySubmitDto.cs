namespace AnketOtomasyonu.Models.DTOs
{
    /// <summary>Kullanıcının anket cevaplarını gönderdiği DTO</summary>
    public class SurveySubmitDto
    {
        public int SurveyId { get; set; }
        public List<AnswerDto> Answers { get; set; } = new();
    }

    public class AnswerDto
    {
        public int QuestionId { get; set; }
        public int? SelectedOptionId { get; set; }
        public string? OpenEndedAnswer { get; set; }
    }
}