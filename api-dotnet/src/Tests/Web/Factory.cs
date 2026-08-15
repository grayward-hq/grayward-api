using System.Net.Http.Headers;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Builder;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using Tests.Infrastructure.Services;

namespace Tests.Integration;

public sealed class NoOpEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string body) => Task.CompletedTask;
}

/// <summary>
/// Integration-test host backed by a throwaway PostgreSQL container.
/// </summary>
/// <remarks>
/// <para>
/// Previously this ran against in-memory SQLite, which could not create the schema at all: the model
/// is Postgres-specific — an ICU collation (<c>case_insensitive</c>), <c>jsonb</c> and <c>xid</c>
/// column types, and filtered indexes with Postgres predicates. Every integration test failed before
/// reaching its assertions, and had for months.
/// </para>
/// <para>
/// Running the real provider also means the schema under test is the one production uses, applied by
/// the actual migrations rather than <c>EnsureCreated</c>. A migration that would fail on deploy now
/// fails here first — which is the failure mode that took staging down for five weeks.
/// </para>
/// </remarks>
public class VulnWatchWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("vulnwatch_tests")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly Dictionary<string, string?> _originalEnvironmentVariables = new();

    public VulnWatchWebAppFactory()
    {
        // Replaced in InitializeAsync with the container's real connection string. It has to be an
        // environment variable rather than in-memory config because Program.cs reads configuration as
        // it executes, before the factory's ConfigureAppConfiguration is applied at Build() time.
        SetDefaultEnvironmentVariable("ConnectionStrings__DefaultConnectionString", "Host=localhost;Database=vulnwatch_tests;Username=test;Password=test");
        SetDefaultEnvironmentVariable("Jwt__SecretKey", "super-secret-test-key-32-chars-min!!");
        SetDefaultEnvironmentVariable("Jwt__ExpireInMinute", "60");
        SetDefaultEnvironmentVariable("Jwt__RefreshTokenExpiryDays", "7");
        SetDefaultEnvironmentVariable("Cors__AllowedOrigins__0", "https://test.example.com");
        SetDefaultEnvironmentVariable("Contact__InternalEmail", "support@example.com");
        SetDefaultEnvironmentVariable("Waitlist__CancellationTokenSecret", "test-waitlist-cancellation-secret-32-chars");
        // Required during Program.cs's own pass, same reason as the connection string above.
        // AddInfrastructure throws without GeoIp:BaseUrl, which failed the host outright.
        SetDefaultEnvironmentVariable("GeoIp__BaseUrl", "http://geoip.test.local/");
        SetDefaultEnvironmentVariable("GeoIp__TimeoutSeconds", "3");
        // WaitlistLinks throws on a missing link setting rather than falling back.
        SetDefaultEnvironmentVariable("FrontendUrl__WaitlistVerify", "https://test.example.com/waitlist/verify");
        SetDefaultEnvironmentVariable("FrontendUrl__WaitlistCancel", "https://test.example.com/waitlist/cancel");
        SetDefaultEnvironmentVariable("FrontendUrl__WaitlistJoin", "https://test.example.com/waitlist");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // The application's own Npgsql registration is left in place — it already points at the
            // container via the connection string, so the test host wires its database exactly as
            // production does. Only the genuinely external dependencies are faked below.
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, NoOpEmailService>();

            services.RemoveAll<IQuotaService>();
            services.RemoveAll<IScanJobFactory>();

            services.AddScoped<IQuotaService, FakeQuotaService>();
            services.AddScoped<IScanJobFactory, FakeScanJobFactory>();

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
                // Deliberately no ConnectionStrings:DefaultConnectionString here. This collection is
                // applied after the environment variables, so a placeholder would override the
                // container's real connection string set in InitializeAsync.
                ["Jwt:SecretKey"] = "super-secret-test-key-32-chars-min!!",
                ["Jwt:ExpireInMinute"] = "60",
                ["Jwt:RefreshTokenExpiryDays"] = "7",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Redis:Configuration"] = "localhost:6379",
                ["FrontendUrl:Verify"] = "https://test.example.com/verify",
                ["FrontendUrl:ForgotPassword"] = "https://test.example.com/reset",
                ["FrontendUrl:PasswordReset"] = "https://test.example.com/set-password",
                ["Cors:AllowedOrigins:0"] = "https://test.example.com",
                ["Dns:Lookup"] = "false",
                ["RateLimit:Auth:PermitLimit"] = "1000",
                ["RateLimit:Auth:WindowSeconds"] = "60",
                ["RateLimit:General:PermitLimit"] = "1000",
                ["RateLimit:General:WindowSeconds"] = "60",
                ["Contact:InternalEmail"] = "support@example.com",
                ["Waitlist:CancellationTokenSecret"] = "test-waitlist-cancellation-secret-32-chars",
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
        await _postgres.StartAsync();

        // Set before Services is touched for the first time: accessing it builds the host, and the
        // connection string has to be visible to Program.cs by then.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnectionString", _postgres.GetConnectionString());

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VulnWatchDbContext>();

        // Migrate rather than EnsureCreated, so the tests run against the schema the migrations
        // actually produce — a broken migration fails here instead of on deploy.
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
            await _postgres.DisposeAsync();
        }
        finally
        {
            RestoreEnvironmentVariables();
        }
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

    private void SetDefaultEnvironmentVariable(string key, string value)
    {
        if (!_originalEnvironmentVariables.ContainsKey(key))
            _originalEnvironmentVariables[key] = Environment.GetEnvironmentVariable(key);

        if (string.IsNullOrWhiteSpace(_originalEnvironmentVariables[key]))
            Environment.SetEnvironmentVariable(key, value);
    }

    private void RestoreEnvironmentVariables()
    {
        foreach (var (key, value) in _originalEnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
