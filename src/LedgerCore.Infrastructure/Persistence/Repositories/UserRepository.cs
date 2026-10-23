using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(LedgerDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        return db.Users.SingleOrDefaultAsync(u => u.Email == normalized, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        return db.Users.AnyAsync(u => u.Email == normalized, cancellationToken);
    }

    public void Add(User user) => db.Users.Add(user);

    // Same normalisation as User.Register, so lookups match what was stored.
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
