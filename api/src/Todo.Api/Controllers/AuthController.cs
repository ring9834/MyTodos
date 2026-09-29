using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Todo.Application.Auth;

namespace Todo.Api.Controllers;

public record AuthRequest(string Username, string Password);

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;

    public AuthController(AuthService auth, IConfiguration config, IHostEnvironment env)
    {
        _auth = auth;
        _config = config;
        _env = env;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRequest request, CancellationToken ct)
    {
        try
        {
            var user = await _auth.RegisterAsync(request.Username, request.Password, ct);
            IssueSessionCookie(user.Id, user.Username);
            return StatusCode(StatusCodes.Status201Created);
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { detail = "Username already taken." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest request, CancellationToken ct)
    {
        var user = await _auth.ValidateCredentialsAsync(request.Username, request.Password, ct);
        if (user is null)
            return Unauthorized(new { detail = "Invalid username or password." });

        IssueSessionCookie(user.Id, user.Username);
        return Ok();
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // Delete relies on matching the exact same cookie options used to set it (path,
        // in particular) — using CookieOptions consistently here avoids a mismatched
        // Delete silently no-op'ing, a real and common gotcha with cookie clearing.
        Response.Cookies.Delete(Program.AuthCookieName, new CookieOptions
        {
            HttpOnly = true,
            // Secure ONLY in a genuine production environment with real TLS, not just "not
            // Development" — see IssueSessionCookie below for the full explanation.
            Secure = _env.IsProduction(),
            SameSite = SameSiteMode.Lax,
        });
        return NoContent();
    }

    // JWT carries the OwnerId as its "sub" claim — every /todos query filters by
    // this, read from the validated token, never from anything the client sends in a body.
    
    // Delivered via an httpOnly, Secure, SameSite=Lax cookie, not returned in the
    // response body — JavaScript can never read this token, mitigating XSS token theft.
    private void IssueSessionCookie(Guid userId, string username)
    {
        var signingKey = _config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("username", username),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        Response.Cookies.Append(Program.AuthCookieName, tokenString, new CookieOptions
        {
            HttpOnly = true,
            // Real HTTPS (via ingress TLS) exists in any deployed environment, but neither
            // WebApplicationFactory's in-memory test server nor local `docker compose` over
            // plain http://localhost do — a Secure-flagged cookie is silently NOT sent back
            // by the client over a non-HTTPS connection (confirmed by OwnerIsolationTests
            // failing with every authenticated request looking anonymous). Environment-
            // conditional, same pattern as the Development-only auto-migrate.
            //
            // Secure ONLY in a genuine production environment with real TLS — not simply
            // "not Development". This deployed AKS "dev" tier runs ASPNETCORE_ENVIRONMENT=
            // Staging specifically so it falls into neither IsDevelopment() nor
            // IsProduction(), matching reality: a real deployed environment (unlike local
            // dev) with no TLS configured yet (unlike a genuine production environment).
            // Confirmed necessary via a real bug: login appeared to work (client-side
            // state only), but every subsequent API call silently came back 401 — the
            // Secure-flagged cookie was never sent back over this ingress's plain HTTP.
            Secure = _env.IsProduction(),
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(8),
        });
    }
}
