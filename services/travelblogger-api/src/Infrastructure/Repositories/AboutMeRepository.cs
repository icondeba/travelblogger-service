using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Infrastructure.Repositories;

public sealed class AboutMeRepository : IAboutMeRepository
{
    private readonly AppDbContext _db;

    public AboutMeRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AboutMe?> GetAsync(CancellationToken ct)
    {
        return await _db.AboutMe.AsNoTracking()
            .OrderByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(AboutMe aboutMe, CancellationToken ct)
    {
        _db.AboutMe.Add(aboutMe);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AboutMe aboutMe, CancellationToken ct)
    {
        _db.AboutMe.Update(aboutMe);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.AboutMe.FindAsync(new object[] { id }, ct);
        if (entity is null)
        {
            return false;
        }

        _db.AboutMe.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
