using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelBlogger.Common;
using TravelBlogger.Infrastructure.Data;
using TravelBlogger.Infrastructure.Notifications;
using TravelBlogger.Infrastructure.Repositories;
using TravelBlogger.Infrastructure.Storage;
using TravelBlogger.Infrastructure.YouTube;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;

namespace TravelBlogger.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null)));

        services.Configure<BlobStorageOptions>(configuration.GetSection("BlobStorage"));
        services.Configure<ContactNotificationOptions>(configuration.GetSection("Notifications"));
        services.AddSingleton<IOpenApiConfigurationOptions, OpenApiConfigurationOptions>();
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        services.AddSingleton<IContactNotificationService, AzureContactNotificationService>();

        services.AddScoped<IAboutMeRepository, AboutMeRepository>();
        services.AddScoped<IArticleRepository, ArticleRepository>();
        services.AddScoped<IContactMessageRepository, ContactMessageRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAwardRepository, AwardRepository>();
        services.AddScoped<IMilestoneRepository, MilestoneRepository>();
        services.Configure<YouTubeOptions>(configuration.GetSection("YouTube"));
        services.AddHttpClient<IYouTubeVideoService, YouTubeVideoService>();

        return services;
    }
}
