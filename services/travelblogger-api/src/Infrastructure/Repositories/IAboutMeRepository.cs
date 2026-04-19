using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Repositories;

public interface IAboutMeRepository
{
    Task<AboutMe?> GetAsync(CancellationToken ct);
    Task AddAsync(AboutMe aboutMe, CancellationToken ct);
    Task UpdateAsync(AboutMe aboutMe, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
