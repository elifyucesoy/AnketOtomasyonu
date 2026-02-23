using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Repositories.Interfaces
{
    public interface ISurveyResponseRepository : IGenericRepository<SurveyResponse>
    {
        Task<bool> HasUserRespondedAsync(int surveyId, string userId);
        Task<SurveyResponse?> GetResponseWithAnswersAsync(int responseId);
        Task<IEnumerable<SurveyResponse>> GetResponsesBySurveyAsync(int surveyId);
        Task<int> GetResponseCountAsync(int surveyId);
    }
}