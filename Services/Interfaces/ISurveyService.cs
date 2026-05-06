using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Services.Interfaces
{
    public interface ISurveyService
    {
        Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId);
        Task<IEnumerable<Survey>> GetAllSurveysAsync();
        Task<IEnumerable<Survey>> GetActiveSurveysAsync();
        Task<IEnumerable<Survey>> GetActiveAnonymousSurveysAsync();
        Task<IEnumerable<Survey>> GetSurveysByCreatorAsync(string creatorUserId);
        Task<IEnumerable<Survey>> GetSurveysByBirimAsync(string birim);
        Task<IEnumerable<Survey>> GetSurveysByBirimsAsync(List<string> birims);
        Task<Survey> CreateSurveyAsync(SurveyCreateDto dto, string creatorUserId, string creatorName, string? creatorBirim = null, bool isSuperAdmin = false);
        Task PublishSurveyAsync(int surveyId);
        Task CloseSurveyAsync(int surveyId);
        Task DeleteSurveyAsync(int surveyId);
        /// <summary>
        /// Anketi günceller. resetToApproval=true ise durum Taslak'a çekilir ve
        /// onay durumu Pending'e sıfırlanır (Admin düzenlemelerinde kullanılır).
        /// </summary>
        Task UpdateSurveyAsync(int surveyId, SurveyCreateDto dto, bool resetToApproval = false);
    }
}