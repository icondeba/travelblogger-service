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

    public async Task<User?> GetByLoginIdAsync(string loginId, CancellationToken ct)
    {
        var normalizedLoginId = (loginId ?? string.Empty).Trim().ToLower();
        if (string.IsNullOrWhiteSpace(normalizedLoginId))
        {
            return null;
        }

        return await _db.Users
            .FirstOrDefaultAsync(u =>
                u.IsActive && u.UserId.ToLower() == normalizedLoginId,
                ct);
    }

    public async Task AddAsync(User user, CancellationToken ct)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(ct);
    }
}
