using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Infrastructure.Repositories;

public sealed class EventRepository : IEventRepository
{
    private readonly AppDbContext _db;

    public EventRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Events.AsNoTracking().OrderByDescending(e => e.EventDate).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Event>> GetPageAsync(int offset, int limit, CancellationToken ct)
    {
        return await _db.Events
            .AsNoTracking()
            .OrderByDescending(e => e.EventDate)
            .ThenByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<Event?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.Events.FindAsync(new object[] { id }, ct);
    }

    public async Task AddAsync(Event item, CancellationToken ct)
    {
        _db.Events.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Event item, CancellationToken ct)
    {
        _db.Events.Update(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Events.FindAsync(new object[] { id }, ct);
        if (entity is null)
        {
            return false;
        }

        _db.Events.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
