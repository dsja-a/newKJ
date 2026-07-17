using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Security.Options;
using Microsoft.AspNetCore.Mvc;

namespace Keji.Api.Controllers;

[ApiController]
[Route("api/security")]
public sealed class SecurityController : ControllerBase
{
    private readonly KejiSecurityOptions _options;
    private readonly ICurrentUserAccessor _users;
    public SecurityController(KejiSecurityOptions options,ICurrentUserAccessor users)
    {
        _options=options;_users=users;
    }

    [HttpGet("status")]
    [KejiAllowAnonymous]
    public IActionResult Status()
    {
        var user=_users.CurrentUser;
        return Ok(new
        {
            enabled=_options.Enabled,
            authMode=_options.AuthMode.ToString(),
            authenticated=user is not null,
            role=user?.Role
        });
    }
}
