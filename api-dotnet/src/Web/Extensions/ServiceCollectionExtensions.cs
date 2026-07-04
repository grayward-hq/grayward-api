using Application.Features.Auth;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json.Serialization;
using Web.Configurations;
using Web.Consumers;
using Web.Services;
using Web.Workers.Alerts;
using Web.Workers.Monitoring;
using Web.Workers.Monitoring.Jobs;
using Web.Workers.Reapers;

namespace Web.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddControllersConfiguration();

            services.AddEndpointsApiExplorer();
            services.AddSwaggerDocumentation();

            services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();
            services.AddScoped<ICurrentUser, CurrentUser>();

            services.AddValidatorsFromAssembly(typeof(RegisterCommand).Assembly);

            services.AddSignalR();
            services.AddHostedService<DomainIntelConsumer>();
            services.AddHostedService<MonitoringWorker>();
            services.AddHostedService<AlertOutboxProcessor>();
            services.AddHostedService<ScanReaperWorker>();
            services.AddHostedService<DomainVerificationReaper>();

            services.AddScoped<ScanDispatchService>();
            services.AddScoped<SslExpiryCheckService>();
            services.AddScoped<OwnershipCheckService>();
            services.AddScoped<BreachMonitoringService>();
            services.AddScoped<BrandProtectionCheckService>();

            services.AddCorsConfiguration(configuration);


            // Configure ForwardedHeaders middleware for reverse proxy scenarios
            services.ConfigureForwardingHeaders();


            services.AddAppRateLimiting(configuration);

            services.AddHealthCheckConfiguration(configuration);

            services.Configure<RouteOptions>(options => options.LowercaseUrls = true);


            return services;
        }


        private static IServiceCollection ConfigureForwardingHeaders(
        this IServiceCollection services)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                // Accept X-Forwarded-For, X-Forwarded-Proto headers
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // For Azure App Service, Docker behind proxy, etc.
                // If behind a known proxy, add its IP/network; otherwise clear known proxies to accept all
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            return services;
        }

        private static IServiceCollection AddControllersConfiguration(
        this IServiceCollection services)
        {
            services.AddControllers()
               .AddJsonOptions(options =>
               {
                   options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                   options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
               });

            return services;
        }

        private static IServiceCollection AddSwaggerDocumentation(
            this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
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
            return services;
        }

        private static IServiceCollection AddHealthCheckConfiguration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var redisConfig = configuration.GetValue<string>("Redis:Configuration") ?? "localhost:6379";

            services.AddHealthChecks()
                .AddNpgSql(
                    configuration.GetConnectionString("DefaultConnectionString")
                        ?? configuration.GetConnectionString("DefaultConnection"),
                    name: "postgres",
                    tags: ["db", "ready"])
                .AddRedis(
                    redisConfig,
                    name: "redis",
                    tags: ["cache", "ready"]);

            return services;
        }

        private static IServiceCollection AddCorsConfiguration(
            this IServiceCollection services,
            IConfiguration configuration)
        {

            var corsSettings = configuration
                .GetSection("Cors")
                .Get<CorsOptions>();

            if (corsSettings?.AllowedOrigins is null ||
                corsSettings.AllowedOrigins.Length == 0 ||
                corsSettings.AllowedOrigins.Any(o => string.IsNullOrWhiteSpace(o)))
            {
                throw new InvalidOperationException("CORS AllowedOrigins is not configured.");
            }

            services.AddCors(options =>
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

            return services;
        }
    }
}
