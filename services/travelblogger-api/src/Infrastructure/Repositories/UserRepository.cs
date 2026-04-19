using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct)
    {
        return await GetByLoginIdAsync(email, ct);
    }

    public async Task<User?> GetByLoginIdAsync(string loginId, CancellationToken ct)
    {
        var normalizedLoginId = (loginId ?? string.Empty).Trim().ToLower();
        if (string.IsNullOrWhiteSpace(normalizedLoginId))
        {
            return null;
        }

        var isGuidLoginId = Guid.TryParse(normalizedLoginId, out var userId);
        return await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u =>
                u.Email.ToLower() == normalizedLoginId ||
                (isGuidLoginId && u.Id == userId),
                ct);
    }

    public async Task AddAsync(User user, CancellationToken ct)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
    }
}
