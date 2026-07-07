using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Web.Hubs;
using Web.Middleware;

namespace Web.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static WebApplication UseWebPipeline(this WebApplication app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
                options.RoutePrefix = "docs";
            });
            app.UseHttpsRedirection();
            app.UseForwardedHeaders();
            app.UseCors("DefaultCors");
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMiddleware<RequestLoggingMiddleware>();
            app.UseAuthentication();
            app.UseMiddleware<JwtMiddleware>();
            app.UseRateLimiter();
            app.UseAuthorization();
            app.MapControllers();

            return app;
        }

        public static WebApplication MapWebEndpoints(this WebApplication app)
        {
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
            
            return app;
        }

        public static async Task ApplyMigrationsAsync(this WebApplication app)
        {
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<VulnWatchDbContext>();
                if (dbContext.Database.IsRelational())
                    await dbContext.Database.MigrateAsync();
                else
                    dbContext.Database.EnsureCreated();
            }


        }


    }
}
