using Domain.Enums;
namespace Domain.Common;

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