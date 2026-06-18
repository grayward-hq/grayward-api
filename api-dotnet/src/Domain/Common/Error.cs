namespace Domain.Common;

/// <summary>
/// Describes why an operation failed. Always create via the static helpers below — never with
/// <c>new Error(...)</c> directly — so call sites read naturally: <c>Error.NotFound("Scan not found")</c>.
/// <para>
/// <see cref="Code"/> tells the controller which HTTP status to return.
/// <see cref="Message"/> is sent to the client as-is, so keep it clear and safe to expose
/// (no stack traces, no internal details).
/// </para>
/// </summary>
public record Error(ErrorCode Code, string Message)
{
    /// <summary>429 — the caller has exceeded an allowed quota or rate limit.</summary>
    public static Error RateLimited(string message) => new(ErrorCode.RateLimited, message);
    /// <summary>404 — the requested resource does not exist.</summary>
    public static Error NotFound(string message) => new(ErrorCode.NotFound, message);

    /// <summary>409 — the request clashes with existing state (e.g. duplicate idempotency key).</summary>
    public static Error Conflict(string message, object? details = null) => new(ErrorCode.Conflict, message);

    /// <summary>400 — the request data is invalid or missing required fields.</summary>
    public static Error Validation(string message) => new(ErrorCode.Validation, message);

    /// <summary>401 — the caller is not authenticated.</summary>
    public static Error Unauthorized(string message) => new(ErrorCode.Unauthorized, message);

    /// <summary>403 — the caller is authenticated but not allowed to perform this action.</summary>
    public static Error Forbidden(string message) => new(ErrorCode.Forbidden, message);

    /// <summary>500 — something unexpected went wrong on our side.</summary>
    public static Error Internal(string message) => new(ErrorCode.Internal, message);
}
