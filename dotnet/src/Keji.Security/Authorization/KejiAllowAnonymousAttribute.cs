using Microsoft.AspNetCore.Authorization;

namespace Keji.Security.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class KejiAllowAnonymousAttribute : Attribute, IAllowAnonymous
{
}
