using Microsoft.EntityFrameworkCore;
using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Article> Articles => Set<Article>();
    public DbSet<AboutMe> AboutMe => Set<AboutMe>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Article>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Title).HasMaxLength(200).IsRequired();
            entity.Property(a => a.Slug).HasMaxLength(200).IsRequired();
            entity.Property(a => a.Excerpt).HasMaxLength(500);
            entity.Property(a => a.Image).HasMaxLength(2048).IsRequired();
            entity.Property(a => a.ImageBlobName).HasMaxLength(512).IsRequired();
            entity.HasIndex(a => a.Slug).IsUnique();
        });

        modelBuilder.Entity<AboutMe>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Heading).HasMaxLength(250).IsRequired();
            entity.Property(a => a.Content).HasMaxLength(4000).IsRequired();
            entity.Property(a => a.Image).HasMaxLength(2048).IsRequired();
            entity.Property(a => a.ImageBlobName).HasMaxLength(512).IsRequired();
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Location).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Image).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.ImageBlobName).HasMaxLength(512).IsRequired();
        });

        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Email).HasMaxLength(256).IsRequired();
            entity.Property(c => c.PhoneNumber).HasMaxLength(32).IsRequired();
            entity.Property(c => c.Message).HasMaxLength(4000).IsRequired();
            entity.Property(c => c.ReplyMessage).HasMaxLength(4000).IsRequired();
        });

        modelBuilder.Entity<VideoPost>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Title).HasMaxLength(200).IsRequired();
            entity.Property(v => v.YouTubeUrl).HasMaxLength(500).IsRequired();
            entity.Property(v => v.VideoId).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            entity.HasIndex(u => u.Email).IsUnique();
        });
    }
}
