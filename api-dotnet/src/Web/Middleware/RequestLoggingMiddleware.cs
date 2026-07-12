using System.Diagnostics;
using Serilog.Context;

namespace Web.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var stopwatch = Stopwatch.StartNew();

        using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
        {
            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                var elapsed = stopwatch.Elapsed.TotalMilliseconds;
                var statusCode = context.Response.StatusCode;
                var level = statusCode >= 500 ? LogLevel.Error
                           : statusCode >= 400 ? LogLevel.Warning
                           : LogLevel.Information;

                _logger.Log(level,
                    "{Method} {Path} → {StatusCode} ({Elapsed}ms) | IP: {IP} | ReqId: {RequestId}",
                    request.Method,
                    request.Path,
                    statusCode,
                    Math.Round(elapsed, 1),
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    context.TraceIdentifier);
            }
        }
    }
}