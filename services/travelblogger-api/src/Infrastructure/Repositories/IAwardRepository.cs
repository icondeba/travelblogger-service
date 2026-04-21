using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Repositories;

public interface IAwardRepository
{
    Task<IReadOnlyList<Award>> GetAllAsync(CancellationToken ct);
    Task<Award?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Award item, CancellationToken ct);
    Task UpdateAsync(Award item, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
