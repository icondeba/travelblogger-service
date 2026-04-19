# TravelBlogger API (Azure Functions, .NET 10)

## Local development

1. Install prerequisites
- .NET 10 SDK
- Azure Functions Core Tools v4
- SQL Server LocalDB or a reachable SQL Server instance

2. Configure settings
- Update `local.settings.json` (for Functions) or `appsettings.Development.json` with a valid SQL connection string and JWT secret.

3. Run EF Core migrations
- Install the EF CLI if needed: `dotnet tool install --global dotnet-ef`
- Create initial migration:
  - `dotnet ef migrations add InitialCreate --output-dir src/Infrastructure/Migrations`
- Apply migrations:
  - `dotnet ef database update`

4. Run the Functions host
- `func start`

## Swagger / OpenAPI

- Swagger UI: `/api/swagger/ui`
- OpenAPI JSON: `/api/swagger.json`

## Authentication

- POST `/api/auth/login` with `email` and `password`.
- The response returns a JWT token. Include it in `Authorization: Bearer <token>` for admin endpoints (POST/PUT/DELETE).

## Create admin user

- Insert an admin user into the `Users` table with a BCrypt-hashed password.
- `Role` should be `0` (Admin).

## Notes

- Public endpoints allow anonymous access.
- Admin endpoints validate JWT and require `Admin` role.
- CORS allowed origins are configured in `appsettings.json` and `appsettings.Development.json` under `Cors:AllowedOrigins`.
- Configure YouTube access in `YouTube:ApiKey`, `YouTube:ChannelId`, and `YouTube:MaxResults` to enable `/api/videos`.

