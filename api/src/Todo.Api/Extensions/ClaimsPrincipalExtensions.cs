using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Todo.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    // Single source of truth for reading OwnerId from the authenticated principal — Q1
    // requires every /todos query to filter by this, never by anything client-supplied.
    public static Guid GetOwnerId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("No sub claim on the authenticated principal.");
        return Guid.Parse(sub);
    }
}
