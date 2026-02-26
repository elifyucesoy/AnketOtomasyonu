using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Services.Interfaces
{
    public interface ISurveyService
    {
        Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId);
        Task<IEnumerable<Survey>> GetAllSurveysAsync();
        Task<IEnumerable<Survey>> GetActiveSurveysAsync();
        Task<IEnumerable<Survey>> GetSurveysByCreatorAsync(string creatorUserId);
        Task<Survey> CreateSurveyAsync(SurveyCreateDto dto, string creatorUserId, string creatorName);
        Task PublishSurveyAsync(int surveyId);
        Task CloseSurveyAsync(int surveyId);
        Task DeleteSurveyAsync(int surveyId);
    }
}