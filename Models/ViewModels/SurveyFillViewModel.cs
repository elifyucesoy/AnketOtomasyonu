using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.ViewModels
{
    /// <summary>Anket doldurma sayfası</summary>
    public class SurveyFillViewModel
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsAnonymous { get; set; }

        public List<FillQuestionViewModel> Questions { get; set; } = new();
    }

    public class FillQuestionViewModel
    {
        public int QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public bool IsRequired { get; set; }
        public int OrderIndex { get; set; }

        /// <summary>Likert ve çoktan seçmeli için seçenekler</summary>
        public List<FillOptionViewModel> Options { get; set; } = new();

        /// <summary>Seçilen şık ID (form post için)</summary>
        public int? SelectedOptionId { get; set; }

        /// <summary>Açık uçlu cevap (form post için)</summary>
        public string? OpenEndedAnswer { get; set; }
    }

    public class FillOptionViewModel
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public int? Value { get; set; }
        public int OrderIndex { get; set; }
    }
}