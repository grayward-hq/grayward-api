using System.Security.Claims;
using Application.Interfaces;
using Infrastructure.Services;

namespace Web.Services;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal User =>
        _httpContextAccessor.HttpContext!.User ?? new ClaimsPrincipal();

    public Guid UserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty    ;

    public string? FirstName =>
        NullIfEmpty(User.FindFirstValue(AppClaimTypes.FirstName));

    public string? LastName =>
        NullIfEmpty(User.FindFirstValue(AppClaimTypes.LastName));

    public string? ProfilePictureUrl =>
        NullIfEmpty(User.FindFirstValue(AppClaimTypes.Picture));
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}