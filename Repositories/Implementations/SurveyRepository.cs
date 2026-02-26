using Microsoft.EntityFrameworkCore;
using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Repositories.Interfaces;

namespace AnketOtomasyonu.Repositories.Implementations
{
    public class SurveyRepository : GenericRepository<Survey>, ISurveyRepository
    {
        public SurveyRepository(ApplicationDbContext context) : base(context) { }

        public async Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId)
        {
            return await _context.Surveys
                .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                    .ThenInclude(q => q.Options.OrderBy(o => o.OrderIndex))
                .Include(s => s.Responses)
                .FirstOrDefaultAsync(s => s.Id == surveyId);
        }

        public async Task<IEnumerable<Survey>> GetSurveysByStatusAsync(SurveyStatus status)
        {
            return await _context.Surveys
                .Where(s => s.Status == status)
                .Include(s => s.Questions)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetSurveysForRoleAsync(string roleName)
        {
            return await _context.Surveys
                .Where(s => s.Status == SurveyStatus.Active
                    && s.TargetRoles.Contains(roleName))
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetSurveysByCreatorAsync(string userId)
        {
            return await _context.Surveys
                .Where(s => s.CreatedByUserId == userId)
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
    }
}