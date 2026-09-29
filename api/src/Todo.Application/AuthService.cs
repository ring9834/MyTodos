using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Todo.Domain.Entities;
using Todo.Infrastructure;

namespace Todo.Application.Auth;

// ADR-0007: self-issued, minimal — not a real IdP. IPasswordHasher<T> is ASP.NET Core's
// own built-in hashing (PBKDF2), no third-party dependency needed for this.
public class AuthService
{
    private readonly TodoDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public AuthService(TodoDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<User> RegisterAsync(string username, string password, CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            throw new InvalidOperationException("Username already taken."); // -> 409, see handler

        // IPasswordHasher<TUser>.HashPassword needs a TUser instance for its generic API,
        // but ASP.NET Core's default implementation doesn't actually read any of its
        // fields — it generates its own random salt internally. This placeholder instance
        // exists only to satisfy that signature; the real User (with the real hash) is
        // created immediately after.
        var placeholder = User.Create(username, passwordHash: string.Empty);
        var hash = _hasher.HashPassword(placeholder, password);
        var user = User.Create(username, hash);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User?> ValidateCredentialsAsync(string username, string password, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null) return null;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Success ? user : null;
    }
}
