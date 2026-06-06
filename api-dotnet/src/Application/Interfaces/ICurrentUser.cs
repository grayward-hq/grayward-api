namespace Application.Interfaces;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid? SessionId { get; }
    bool IsAuthenticated { get; }
    string? FirstName { get; }
    string? LastName { get; }
    string? ProfilePictureUrl { get; }
}
