using System.Net.Http.Headers;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Builder;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;

namespace Tests.Integration;

public sealed class NoOpEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string body) => Task.CompletedTask;
}

public class VulnWatchWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<VulnWatchDbContext>>();
            services.RemoveAll<VulnWatchDbContext>();

            services.AddDbContext<VulnWatchDbContext>(options =>
                options.UseSqlite(_connection));

            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, NoOpEmailService>();

            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();

            services.AddRateLimiter(options =>
            {
                options.AddPolicy("auth-limit", _ =>
                    RateLimitPartition.GetNoLimiter(string.Empty));

                options.AddPolicy("general-limit", _ =>
                    RateLimitPartition.GetNoLimiter(string.Empty));
            });
        });

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "super-secret-test-key-32-chars-min!!",
                ["Jwt:ExpireInMinute"] = "60",
                ["Jwt:RefreshTokenExpiryDays"] = "7",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Redis:Configuration"] = "localhost:6379",
                ["FrontendUrl:Verify"] = "https://test.example.com/verify",
                ["FrontendUrl:ForgotPassword"] = "https://test.example.com/reset",
                ["Cors:AllowedOrigins:0"] = "https://test.example.com",
                ["Dns:Lookup"] = "false",
                ["RateLimit:Auth:PermitLimit"] = "1000",
                ["RateLimit:Auth:WindowSeconds"] = "60",
                ["RateLimit:General:PermitLimit"] = "1000",
                ["RateLimit:General:WindowSeconds"] = "60",
                ["Contact:InternalEmail"] = "support@example.com",
                ["SmtpCredentials:Host"] = "smtp.test.com",
                ["SmtpCredentials:Port"] = "587",
                ["SmtpCredentials:Username"] = "test@test.com",
                ["SmtpCredentials:Password"] = "password",
                ["SmtpCredentials:FromName"] = "VulnWatch Test",
                ["Authentication:Google:ClientId"] = "test-client-id",
            });
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VulnWatchDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    public async Task<(User user, string token)> CreateAuthenticatedUserAsync(
        string email = "tony@vulnwatch.test",
        string password = "P@ssw0rd123!")
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var user = User.Create(email, "Tony", "Dev");
        user.ConfirmEmail();

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var errors = string.Join(
                "; ",
                result.Errors.Select(e => $"{e.Code}: {e.Description}"));

            throw new InvalidOperationException(
                $"Failed to create authenticated test user '{email}'. Errors: {errors}");
        }

        var created = await userManager.FindByEmailAsync(email);

        if (created is null)
        {
            throw new InvalidOperationException(
                $"User '{email}' was created successfully but could not be retrieved.");
        }
        
        var token = jwtService.GenerateToken(created!, sessionId: null);

        return (created!, token);
    }

    /// <summary>Returns an HttpClient pre-loaded with the given bearer token.</summary>
    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Seeds a verified domain owned by the given user and returns its id.</summary>
    public async Task<Guid> CreateVerifiedDomainAsync(Guid userId, string domainName = "example.com")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VulnWatchDbContext>();

        var domain = ScannedDomain.Create(userId, domainName, verificationToken: null);
        domain.Verify();

        db.Domains.Add(domain);
        await db.SaveChangesAsync();

        return domain.Id;
    }
}
