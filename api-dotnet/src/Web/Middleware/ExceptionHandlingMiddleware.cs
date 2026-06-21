using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Common;

namespace Web.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            await Write(context, ex.Error, ex.Error.Code.ToStatusCode());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Path}", context.Request.Path);
            // Don't leak internals — generic 500, same as Result.Internal
            await Write(context, Error.Internal("An unexpected error occurred."),
                StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task Write(HttpContext context, Error error, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(Result<object>.Failure(error), JsonOptions);
        await context.Response.WriteAsync(body);
    }
}