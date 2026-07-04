using Domain.Common;
using Domain.Entities;

namespace Application.Interfaces;
public interface IUserService
{
    Task<Result<bool>> ProvisionNewUser(User user, CancellationToken ct);
}