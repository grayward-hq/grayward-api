using Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace Domain.Common;

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

public class AppException : Exception
{
    public Error Error { get; }
    public AppException(Error error) : base(error.Message) => Error = error;
}

public sealed class QuotaExceededException : AppException
{
    public QuotaKind QuotaKind { get; }
    public ResourceKind ResourceKind { get; }
    public int Limit { get; }

    public QuotaExceededException(QuotaKind quotaKind, ResourceKind resourceKind, int limit)
        : base(Error.RateLimited(
            $"You've reached your plan limit of {limit}. Upgrade your plan or remove existing ones."))
    {
        QuotaKind = quotaKind;
        ResourceKind = resourceKind;
        Limit = limit;
    }
}
public sealed class ForbiddenException(string message)   : AppException(Error.Forbidden(message));
public sealed class NotFoundException(string message)    : AppException(Error.NotFound(message));
public sealed class DomainException(string message)    : AppException(Error.Conflict(message));
// public sealed class ConflictException(string message)    : AppException(Error.Conflict(message));
// public sealed class ValidationException(string message)  : AppException(Error.Validation(message));
// 