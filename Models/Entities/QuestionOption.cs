namespace AnketOtomasyonu.Models.Entities
{
    public class QuestionOption
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public int? Value { get; set; }

        public Question Question { get; set; } = null!;
    }
}