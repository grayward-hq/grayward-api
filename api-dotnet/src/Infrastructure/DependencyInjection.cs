using Application.Behaviours;
using Application.Catalogs;
using Application.Features.Alerts;
using Application.Features.Alerts.SslExpiry;
using Application.Features.Auth;
using Application.Features.BreachMonitoring;
using Application.Helpers;
using Application.Interfaces;
using Application.Mappers;
using Application.Options;
using Application.Services;
using DnsClient;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Redis;
using Infrastructure.Services;
using Infrastructure.Services.Chat;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
        {


            // Identity — AddIdentityCore avoids overriding auth scheme to cookies
            services.AddIdentityCore<User>(options =>
            {
                // Password policy
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 1;

                // Email
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;

                // Lockout
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<VulnWatchDbContext>()
            .AddDefaultTokenProviders();


            // JWT Authentication
            var jwtSecret = configuration["Jwt:SecretKey"];

            if (string.IsNullOrWhiteSpace(jwtSecret))
            {
                throw new InvalidOperationException("Jwt:SecretKey is not configured.");
            }

            var jwtKey = Encoding.UTF8.GetBytes(jwtSecret);

            if (jwtKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Jwt:SecretKey must be at least 32 characters (256 bits) for HS256 signing.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnChallenge = ctx =>
                        {
                            ctx.HandleResponse();
                            return Task.CompletedTask;
                        }
                    };
                });

            services.AddAuthorization();



            // Redis
            var redisConfig = configuration.GetValue<string>("Redis:Configuration") ?? "localhost:6379";
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var config = ConfigurationOptions.Parse(redisConfig);
                config.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(config);
            });
            services.AddSingleton<IRedisProducer, RedisProducer>();
            services.AddSingleton<IRedisService, RedisService>();

            // Application services
            services.AddHttpContextAccessor();
            services.Configure<JwtConfig>(configuration.GetSection(JwtConfig.SectionName));
            services.AddScoped<IVulnWatchDbContext, VulnWatchDbContext>();
            services.AddScoped<IJwtService, JwtService>();
            services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IDomainRepository, DomainRepository>();
            services.AddScoped<IScanRepository, ScanRepository>();
            services.AddScoped<IMonitoredRepoRepository, MonitoredRepoRepository>();
            services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
            services.AddScoped<IFindingRepository, FindingRepository>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddSingleton<LookupClient>(_ =>
                            new LookupClient(
                                new LookupClientOptions(
                                    NameServer.GooglePublicDns,       // 8.8.8.8
                                    NameServer.GooglePublicDns2       // 8.8.4.4
                                )
                                {
                                    UseCache = false,                 // don't cache during testing
                                    Retries = 3,
                                    Timeout = TimeSpan.FromSeconds(5)
                                }
                            )
                        );
            services.AddScoped<IDnsResolver, DnsResolver>();
            services.AddScoped<SslExpiryChecker>();
            services.AddScoped<IAlertService, AlertService>();
            services.AddScoped<IAlertRepository, AlertRepository>();
            services.AddScoped<AlertDispatcher>();
           

            services.AddScoped<INotificationPreferencesRepository, NotificationPreferencesRepository>();
            services.AddScoped<IDomainSettingsRepository, DomainSettingsRepository>();
            services.AddScoped<IWaitlistRepository, WaitlistRepository>();
            services.AddScoped<IWaitlistCancellationTokenService, WaitlistCancellationTokenService>();
            services.AddHttpClient("anthropic");  // base URL set per-request in the service
            services.AddHttpClient("gemini");     // base URL set per-request in the service
            services
                    .AddHttpClient("openai", client =>
                    {
                        // Works for both OpenAI and Groq — base URL differs by key config
                        var baseUrl = configuration["Chat:OpenAi:BaseUrl"] ?? "https://api.openai.com";
                        var apiKey = configuration["Chat:OpenAi:ApiKey"] ?? "";

                        client.BaseAddress = new Uri(baseUrl);
                        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    });
            services.AddScoped<ClaudeService>();
            services.AddScoped<AnthropicChatService>();
            services.AddScoped<GeminiChatService>();
            services.AddScoped<OpenAiChatService>();
            services.AddScoped<IChatServiceFactory, ChatServiceFactory>();
            services.AddScoped<IChatService>(sp =>
                    sp.GetRequiredService<IChatServiceFactory>().Resolve());
            services.AddHttpClient("slack");
            services.AddScoped<ISlackService, SlackService>();
            services.AddSingleton<IPlanCatalog, PlanCatalog>();
            services.AddScoped<IQuotaService, QuotaService>();
            services.AddScoped<IScanJobFactory, ScanJobFactory>();
            services.AddScoped<IIntegrationRepository, IntegrationRepository>();
            services.AddDataProtection()
                    .PersistKeysToDbContext<VulnWatchDbContext>()
                    .SetApplicationName("VulnWatch");
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<OwaspEvaluationEngine>();
            services.AddHttpClient("BrandProtection", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator // lookalikes may have bad certs
            });
            services.AddScoped<HaveIBeenPwnedService>();
            services.AddScoped<LookAlikeDomainChecker>();
            services.AddScoped<IBrandThreatRepository, BrandThreatRepository>();
            services.AddScoped<IMonitoredEmailRepository, MonitoredEmailRepository>();
            services.Configure<GitHubAppOptions>(
                configuration.GetSection(GitHubAppOptions.Section));
            services.AddSingleton<GitHubAppJwtFactory>();
            services.AddHttpClient<IGitHubAppClient, GitHubAppClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.github.com/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VulnWatch");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            });

            var geoIpBase = configuration["GeoIp:BaseUrl"]
                ?? throw new InvalidOperationException("GeoIp:BaseUrl is not configured.");
            var geoIpTimeout = int.TryParse(configuration["GeoIp:TimeoutSeconds"], out var t) ? t : 3;

            services.AddHttpClient<IGeoLocationService, GeoLocationService>(client =>
            {
                client.BaseAddress = new Uri(geoIpBase);
                client.Timeout = TimeSpan.FromSeconds(geoIpTimeout);
            });

            services.AddMemoryCache(options =>
            {
                // so the cache doesn't grow unbounded
                // Each GeoIP entry has Size = 1, so this allows up to 10 000 IPs in-process
                options.SizeLimit = 10_000;
            });


            return services;
        }
    }
}
