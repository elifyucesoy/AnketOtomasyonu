using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.ViewModels
{
    /// <summary>Anket detay sayfası - Sorular + Sonuçlar</summary>
    public class SurveyDetailViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusBadgeClass { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsAnonymous { get; set; }
        public string TargetRoles { get; set; } = string.Empty;
        public int ResponseCount { get; set; }

        public List<QuestionDetailViewModel> Questions { get; set; } = new();
    }

    public class QuestionDetailViewModel
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public bool IsRequired { get; set; }
        public int OrderIndex { get; set; }
        public List<OptionDetailViewModel> Options { get; set; } = new();
    }

    public class OptionDetailViewModel
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public int? Value { get; set; }
        public int OrderIndex { get; set; }
    }
}