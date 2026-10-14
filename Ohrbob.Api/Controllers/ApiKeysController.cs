using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApiKeysController : ControllerBase
{
    private readonly ApiKeysService _apiKeysService;

    public ApiKeysController(ApiKeysService apiKeysService)
    {
        _apiKeysService = apiKeysService;
    }

    [HttpGet("account/{accountId}")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    public async Task<ActionResult<List<ApiKey>>> GetByAccountId(string accountId)
    {
        var keys = await _apiKeysService.GetByAccountIdAsync(accountId);
        return Ok(keys);
    }

    [HttpPost("account/{accountId}")]
    [Authorize(Policy = Consts.CreateAccountPolicy)]
    public async Task<ActionResult<ApiKey>> Create(string accountId, [FromBody] ApiKey request)
    {
        var apiKey = new ApiKey
        {
            AccountId = accountId,
            Name = request.Name,
            ProfileId = request.ProfileId,
            Token = Guid.NewGuid().ToString()
        };
        var created = await _apiKeysService.CreateAsync(apiKey);
        return Ok(created);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    public async Task<IActionResult> Delete(string id)
    {
        var success = await _apiKeysService.DeleteAsync(id, hardDelete: true);
        if (!success) return NotFound();
        return Ok();
    }
}
