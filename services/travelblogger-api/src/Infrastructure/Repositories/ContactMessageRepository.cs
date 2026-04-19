using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Infrastructure.Repositories;

public sealed class ContactMessageRepository : IContactMessageRepository
{
    private readonly AppDbContext _db;

    public ContactMessageRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ContactMessage>> GetAllAsync(CancellationToken ct)
    {
        return await _db.ContactMessages
            .AsNoTracking()
            .OrderByDescending(m => m.SubmittedAt)
            .ToListAsync(ct);
    }

    public async Task<ContactMessage?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.ContactMessages.FindAsync(new object[] { id }, ct);
    }

    public async Task AddAsync(ContactMessage message, CancellationToken ct)
    {
        _db.ContactMessages.Add(message);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ContactMessage message, CancellationToken ct)
    {
        _db.ContactMessages.Update(message);
        await _db.SaveChangesAsync(ct);
    }
}
