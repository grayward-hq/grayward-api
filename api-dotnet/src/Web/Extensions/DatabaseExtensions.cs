using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Web.Extensions
{
    public static class DatabaseExtensions
    {

        public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
        {
           
            // Database
            var connectionString = configuration.GetConnectionString("DefaultConnectionString");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = configuration.GetConnectionString("DefaultConnection");
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Default database connection string is not configured.");
            }

            services.AddDbContext<VulnWatchDbContext>(options =>
                options.UseNpgsql(connectionString));

            return services;
        }
    }
}
