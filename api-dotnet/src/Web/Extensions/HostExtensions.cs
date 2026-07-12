using Serilog;

namespace Web.Extensions
{
    public static class HostExtensions
    {
        public static IHostBuilder AddAppLogging(this IHostBuilder host)
        {
            host.UseSerilog((ctx, config) =>
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
            return host;
        }
    }
}
