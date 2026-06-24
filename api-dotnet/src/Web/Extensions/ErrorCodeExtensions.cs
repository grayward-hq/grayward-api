using Domain.Common;
using Microsoft.AspNetCore.Http;

namespace Web.Extensions;

public static class ErrorCodeExtensions
{
    public static int ToStatusCode(this ErrorCode code) => code switch
    {
        ErrorCode.NotFound    => StatusCodes.Status404NotFound,
        ErrorCode.Conflict    => StatusCodes.Status409Conflict,
        ErrorCode.Validation  => StatusCodes.Status400BadRequest,
        ErrorCode.Unauthorized=> StatusCodes.Status401Unauthorized,
        ErrorCode.Forbidden   => StatusCodes.Status403Forbidden,
        ErrorCode.RateLimited => StatusCodes.Status429TooManyRequests,
        _                     => StatusCodes.Status500InternalServerError,
    };
}