using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Infrastructure.Repositories;

public sealed class MilestoneRepository : IMilestoneRepository
{
    private readonly AppDbContext _db;

    public MilestoneRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Milestone>> GetAllAsync(CancellationToken ct) =>
        await _db.Milestones.AsNoTracking().OrderBy(m => m.Year).ThenByDescending(m => m.CreatedAt).ToListAsync(ct);

    public async Task<Milestone?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Milestones.FindAsync(new object[] { id }, ct);

    public async Task AddAsync(Milestone item, CancellationToken ct)
    {
        _db.Milestones.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Milestone item, CancellationToken ct)
    {
        _db.Milestones.Update(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Milestones.FindAsync(new object[] { id }, ct);
        if (entity is null) return false;
        _db.Milestones.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
