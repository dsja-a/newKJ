using Keji.Security.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Keji.Integration.Tests.Authorization;

[ApiController]
[Route("probe")]
public sealed class ProbeController : ControllerBase
{
    [HttpGet("anonymous")]
    [KejiAllowAnonymous]
    public IActionResult Anonymous() => Ok(new { result = "anonymous" });

    [HttpGet("standard-anonymous")]
    [AllowAnonymous]
    public IActionResult StandardAnonymous() => Ok(new { result = "standard-anonymous" });

    [HttpGet("no-metadata")]
    public IActionResult NoMetadata() => Ok(new { result = "no-metadata" });

    [HttpGet("account-read")]
    [KejiRequirePermission(KejiPermission.AccountSelfRead)]
    public IActionResult AccountRead() => Ok(new { result = "account-read" });

    [HttpGet("file-read")]
    [KejiRequirePermission(KejiPermission.FileRead)]
    public IActionResult FileRead() => Ok(new { result = "file-read" });

    [HttpGet("file-write")]
    [KejiRequirePermission(KejiPermission.FileWrite)]
    public IActionResult FileWrite() => Ok(new { result = "file-write" });

    [HttpGet("admin-users")]
    [KejiRequirePermission(KejiPermission.AdminUsers)]
    public IActionResult AdminUsers() => Ok(new { result = "admin-users" });

    [HttpGet("admin-conversations")]
    [KejiRequirePermission(KejiPermission.AdminConversations)]
    public IActionResult AdminConversations() => Ok(new { result = "admin-conversations" });

    [HttpGet("multiple-permissions")]
    [KejiRequirePermission(KejiPermission.FileRead)]
    [KejiRequirePermission(KejiPermission.FileWrite)]
    public IActionResult MultiplePermissions() => Ok(new { result = "multiple-permissions" });

    [HttpGet("conflicting-metadata")]
    [KejiAllowAnonymous]
    [KejiRequirePermission(KejiPermission.AccountSelfRead)]
    public IActionResult ConflictingMetadata() => Ok(new { result = "should-not-reach" });

    [HttpGet("throwing")]
    [KejiRequirePermission(KejiPermission.AccountSelfRead)]
    public IActionResult Throwing()
    {
        throw new InvalidOperationException("Intentional exception for testing");
    }
}
