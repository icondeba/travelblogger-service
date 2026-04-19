using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByLoginIdAsync(string loginId, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}
