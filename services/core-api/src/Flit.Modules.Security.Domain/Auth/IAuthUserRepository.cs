using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Domain.Auth;

public interface IAuthUserRepository
{
    Task<UserAuthSnapshot?> FindByEmailAsync(string email, CancellationToken cancellationToken);
}
