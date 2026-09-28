using Ohrbob.Server.Shared.TwoFactor;

namespace Ohrbob.Web.Data;

public interface ITOTPSecretRepository
{
    Task<TOTPSecret?> GetByUserIdAsync(string userId);
    Task CreateOrUpdateAsync(TOTPSecret secret);
    Task DeleteAsync(string userId);
}
