using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Repositories;

public interface IContactMessageRepository
{
    Task<IReadOnlyList<ContactMessage>> GetAllAsync(CancellationToken ct);
    Task<ContactMessage?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(ContactMessage message, CancellationToken ct);
    Task UpdateAsync(ContactMessage message, CancellationToken ct);
}
