using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Repositories;

public interface IEventRepository
{
    Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Event>> GetPageAsync(int offset, int limit, CancellationToken ct);
    Task<Event?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Event item, CancellationToken ct);
    Task UpdateAsync(Event item, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
