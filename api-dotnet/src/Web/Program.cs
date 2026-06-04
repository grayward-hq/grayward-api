using Application.Behaviours;
using Application.Features.Alerts;
using Application.Features.Alerts.SslExpiry;
using Application.Features.Auth;
using Application.Features.Scans;
using Application.Helpers;
using Application.Interfaces;
using DnsClient;
using Domain.Entities;
using FluentValidation;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using System.Text;
using System.Text.Json.Serialization;
using Web.Configurations;
using Web.Consumers;
using Web.Extensions;
using Web.Hubs;
using Web.Middleware;
using Web.Services;
using Web.Workers.Alerts;
using Web.Workers.Monitoring;
using Web.Workers.Reapers;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, config) =>
{
    config
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Update", Serilog.Events.LogEventLevel.Fatal)
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("StackExchange.Redis", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProcessId()
        .Enrich.WithThreadId()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token. Example: eyJhbGci..."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});

// MediatR — scans Application assembly
builder.Services.AddValidatorsFromAssembly(typeof(RegisterCommand).Assembly);
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(RegisterCommand).Assembly);
});
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnectionString");

if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Default database connection string is not configured.");
}

builder.Services.AddDbContext<VulnWatchDbContext>(options =>
    options.UseNpgsql(connectionString));

// Identity — AddIdentityCore avoids overriding auth scheme to cookies
builder.Services.AddIdentityCore<User>(options =>
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
var jwtSecret = builder.Configuration["Jwt:SecretKey"];

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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

builder.Services.AddAuthorization();



// Redis
var redisConfig = builder.Configuration.GetValue<string>("Redis:Configuration") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = ConfigurationOptions.Parse(redisConfig);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});
builder.Services.AddSingleton<IRedisProducer, RedisProducer>();
builder.Services.AddSingleton<IRedisService, RedisService>();

// Application services
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();
builder.Services.Configure<JwtConfig>(builder.Configuration.GetSection(JwtConfig.SectionName));
builder.Services.AddScoped<IVulnWatchDbContext, VulnWatchDbContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IDomainRepository, DomainRepository>();
builder.Services.AddScoped<IScanRepository, ScanRepository>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<LookupClient>(_ =>
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
builder.Services.AddScoped<IDnsResolver, DnsResolver>();
builder.Services.AddScoped<SslExpiryChecker>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<DomainIntelConsumer>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IAlertRepository, AlertRepository>();
builder.Services.AddHostedService<AlertOutboxProcessor>();
builder.Services.AddScoped<AlertDispatcher>();
builder.Services.AddScoped<ScanDispatchService>();
builder.Services.AddScoped<SslExpiryCheckService>();
builder.Services.AddScoped<OwnershipCheckService>();
builder.Services.AddHostedService<MonitoringWorker>();
builder.Services.AddHostedService<ScanReaperWorker>();
builder.Services.AddScoped<INotificationPreferencesRepository, NotificationPreferencesRepository>();
builder.Services.AddScoped<IDomainSettingsRepository, DomainSettingsRepository>();
builder.Services.AddHttpClient("anthropic");  // base URL set per-request in the service
builder.Services.AddHttpClient("gemini");     // base URL set per-request in the service
builder.Services
        .AddHttpClient("openai", client =>
        {
            // Works for both OpenAI and Groq — base URL differs by key config
            var baseUrl = builder.Configuration["Chat:OpenAi:BaseUrl"] ?? "https://api.openai.com";
            var apiKey  = builder.Configuration["Chat:OpenAi:ApiKey"] ?? "";

            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        });
builder.Services.AddScoped<AnthropicChatService>();
builder.Services.AddScoped<GeminiChatService>();
builder.Services.AddScoped<OpenAiChatService>();
builder.Services.AddScoped<IChatServiceFactory, ChatServiceFactory>();
builder.Services.AddScoped<IChatService>(sp =>
        sp.GetRequiredService<IChatServiceFactory>().Resolve());

builder.Services.AddHttpClient("slack");
builder.Services.AddScoped<ISlackService, SlackService>();
builder.Services.AddScoped<IIntegrationRepository, IntegrationRepository>();
builder.Services.AddDataProtection()
        .PersistKeysToDbContext<VulnWatchDbContext>()
        .SetApplicationName("VulnWatch");
        
var corsSettings = builder.Configuration
    .GetSection("Cors")
    .Get<CorsOptions>();

if (corsSettings?.AllowedOrigins is null ||
    corsSettings.AllowedOrigins.Length == 0 ||
    corsSettings.AllowedOrigins.Any(o => string.IsNullOrWhiteSpace(o)))
{
    throw new InvalidOperationException("CORS AllowedOrigins is not configured.");
}

builder.Services.AddCors(options =>
{

    options.AddPolicy("DefaultCors", policy =>
    {
        policy
            .WithOrigins(corsSettings.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAppRateLimiting(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        name: "postgres",
        tags: ["db", "ready"])
    .AddRedis(
        redisConfig,
        name: "redis",
        tags: ["cache", "ready"]);

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<VulnWatchDbContext>();
    if (dbContext.Database.IsRelational())
        dbContext.Database.Migrate();
    else
        dbContext.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    options.RoutePrefix = "docs";
});
app.UseHttpsRedirection();
app.UseCors("DefaultCors");
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<JwtMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ScanHub>("/hubs/scans");
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = HealthResponse.WriteAsync
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponse.WriteAsync
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("Application has started.");
});

app.Run();

static void LoadDotEnv()
{
    foreach (var envPath in ResolveDotEnvCandidates())
    {
        if (!File.Exists(envPath))
            continue;

        foreach (var rawLine in File.ReadAllLines(envPath))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }

        return;
    }
}

static IEnumerable<string> ResolveDotEnvCandidates()
{
    var currentDirectory = Directory.GetCurrentDirectory();
    var appBaseDirectory = AppContext.BaseDirectory;

    return new[]
    {
        Path.GetFullPath(Path.Combine(currentDirectory, "api-dotnet", ".env")),
        Path.GetFullPath(Path.Combine(currentDirectory, ".env")),
        Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", ".env")),
        Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", "..", ".env")),
        Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", "..", "..", ".env"))
    }.Distinct(StringComparer.OrdinalIgnoreCase);
}


public partial class Program { }