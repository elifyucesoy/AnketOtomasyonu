using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.DTOs
{
    public class SurveyResultDto
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TotalResponses { get; set; }
        public List<RespondentInfoDto> Respondents { get; set; } = new();
        public List<QuestionResultDto> Questions { get; set; } = new();
    }

    public class RespondentInfoDto
    {
        public string? UserFullName { get; set; }
        public string? FakulteAdi { get; set; }
        public string? BolumAdi { get; set; }
        public DateTime SubmittedAt { get; set; }
    }

    public class QuestionResultDto
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public QuestionType QuestionType { get; set; }
        public int AnswerCount { get; set; }
        public List<OptionResultDto> OptionResults { get; set; } = new();
        public List<string> OpenEndedAnswers { get; set; } = new();
    }

    public class OptionResultDto
    {
        public int OptionId { get; set; }
        public string OptionText { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}