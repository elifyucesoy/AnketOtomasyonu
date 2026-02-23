namespace AnketOtomasyonu.Models.Entities
{
    public enum QuestionType
    {
        Likert = 1,
        MultipleChoice = 2,
        OpenEnded = 3
    }

    public class Question
    {
        public int Id { get; set; }
        public int SurveyId { get; set; }
        public string Text { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public bool IsRequired { get; set; } = true;
        public int OrderIndex { get; set; }

        public Survey Survey { get; set; } = null!;
        public ICollection<QuestionOption> Options { get; set; } = new List<QuestionOption>();
    }
}