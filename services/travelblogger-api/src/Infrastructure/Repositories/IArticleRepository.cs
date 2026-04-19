using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Repositories;

public interface IArticleRepository
{
    Task<IReadOnlyList<Article>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Article>> GetPageAsync(int offset, int limit, bool publishedOnly, CancellationToken ct);
    Task<Article?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Article?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken ct);
    Task AddAsync(Article article, CancellationToken ct);
    Task UpdateAsync(Article article, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
