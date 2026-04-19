using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Infrastructure.Repositories;

public sealed class ArticleRepository : IArticleRepository
{
    private readonly AppDbContext _db;

    public ArticleRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Article>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Articles.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Article>> GetPageAsync(int offset, int limit, bool publishedOnly, CancellationToken ct)
    {
        var query = _db.Articles.AsNoTracking().AsQueryable();
        if (publishedOnly)
        {
            query = query.Where(a => a.Status == ArticleStatus.Published);
        }

        return await query
            .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt)
            .ThenByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<Article?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.Articles.FindAsync(new object[] { id }, ct);
    }

    public async Task<Article?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        return await _db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug, ct);
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken ct)
    {
        var query = _db.Articles.AsNoTracking().Where(a => a.Slug == slug);
        if (excludeId.HasValue)
        {
            query = query.Where(a => a.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task AddAsync(Article article, CancellationToken ct)
    {
        _db.Articles.Add(article);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Article article, CancellationToken ct)
    {
        _db.Articles.Update(article);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Articles.FindAsync(new object[] { id }, ct);
        if (entity is null)
        {
            return false;
        }

        _db.Articles.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
