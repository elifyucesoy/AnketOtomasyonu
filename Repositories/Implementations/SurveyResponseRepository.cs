using Microsoft.EntityFrameworkCore;
using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Repositories.Interfaces;

namespace AnketOtomasyonu.Repositories.Implementations
{
    public class SurveyResponseRepository
        : GenericRepository<SurveyResponse>, ISurveyResponseRepository
    {
        public SurveyResponseRepository(ApplicationDbContext context)
            : base(context) { }

        public async Task<bool> HasUserRespondedAsync(int surveyId, string userId)
        {
            return await _context.SurveyResponses
                .AnyAsync(r => r.SurveyId == surveyId && r.UserId == userId);
        }

        public async Task<SurveyResponse?> GetResponseWithAnswersAsync(int responseId)
        {
            return await _context.SurveyResponses
                .Include(r => r.Answers)
                    .ThenInclude(a => a.SelectedOption)
                .Include(r => r.Answers)
                    .ThenInclude(a => a.Question)
                .FirstOrDefaultAsync(r => r.Id == responseId);
        }

        public async Task<IEnumerable<SurveyResponse>> GetResponsesBySurveyAsync(int surveyId)
        {
            return await _context.SurveyResponses
                .Where(r => r.SurveyId == surveyId)
                .Include(r => r.Answers)
                    .ThenInclude(a => a.SelectedOption)
                .Include(r => r.Answers)
                    .ThenInclude(a => a.Question)
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();
        }

        public async Task<int> GetResponseCountAsync(int surveyId)
        {
            return await _context.SurveyResponses
                .CountAsync(r => r.SurveyId == surveyId);
        }
    }
}