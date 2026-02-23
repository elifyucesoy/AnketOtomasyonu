using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.DTOs
{
    public class QuestionCreateDto
    {
        public string Text { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public bool IsRequired { get; set; } = true;
        public int OrderIndex { get; set; }
        public List<OptionCreateDto> Options { get; set; } = new();
    }

    public class OptionCreateDto
    {
        public string Text { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
    }
}