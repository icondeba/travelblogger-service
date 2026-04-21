using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Infrastructure.Repositories;

public sealed class AwardRepository : IAwardRepository
{
    private readonly AppDbContext _db;

    public AwardRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Award>> GetAllAsync(CancellationToken ct) =>
        await _db.Awards.AsNoTracking().OrderByDescending(a => a.Year).ThenByDescending(a => a.CreatedAt).ToListAsync(ct);

    public async Task<Award?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Awards.FindAsync(new object[] { id }, ct);

    public async Task AddAsync(Award item, CancellationToken ct)
    {
        _db.Awards.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Award item, CancellationToken ct)
    {
        _db.Awards.Update(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Awards.FindAsync(new object[] { id }, ct);
        if (entity is null) return false;
        _db.Awards.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
