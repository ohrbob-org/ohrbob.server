using Ohrbob.Server.Shared.TwoFactor;
using Ohrbob.Web.Data;

namespace Ohrbob.Web.Services;

public class TOTPSecretRepository : ITOTPSecretRepository
{
    public Task<TOTPSecret?> GetByUserIdAsync(string userId)
    {
        throw new NotImplementedException();
    }

    public Task CreateOrUpdateAsync(TOTPSecret secret)
    {
        throw new NotImplementedException();
    }

    public Task DeleteAsync(string userId)
    {
        throw new NotImplementedException();
    }
}