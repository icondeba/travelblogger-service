using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Repositories;

public interface IMilestoneRepository
{
    Task<IReadOnlyList<Milestone>> GetAllAsync(CancellationToken ct);
    Task<Milestone?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Milestone item, CancellationToken ct);
    Task UpdateAsync(Milestone item, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
