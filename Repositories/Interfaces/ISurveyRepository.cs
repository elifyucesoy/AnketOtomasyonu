using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Repositories.Interfaces
{
    public interface ISurveyRepository : IGenericRepository<Survey>
    {
        /// <summary>Anketi soruları ve seçenekleriyle birlikte getirir</summary>
        Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId);

        /// <summary>Belirli durumdaki anketleri getirir</summary>
        Task<IEnumerable<Survey>> GetSurveysByStatusAsync(SurveyStatus status);

        /// <summary>Belirli role atanmış aktif anketleri getirir</summary>
        Task<IEnumerable<Survey>> GetSurveysForRoleAsync(string roleName);

        /// <summary>Belirli kullanıcının oluşturduğu anketleri getirir</summary>
        Task<IEnumerable<Survey>> GetSurveysByCreatorAsync(string userId);
    }
}