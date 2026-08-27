using Microsoft.AspNetCore.Identity;

namespace ContextEngine.Api.Models.Identity
{
    /// <summary>
    /// The API's Identity user. No extra profile fields yet - callers authenticate with just the
    /// email/password that <see cref="IdentityUser"/> already provides.
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
    }
}
