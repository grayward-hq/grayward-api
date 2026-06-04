using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository(VulnWatchDbContext db)
    : BaseRepository<RefreshToken>(db), IRefreshTokenRepository
{
    public Task<RefreshToken?> GetById(Guid id, CancellationToken ct) =>
        Db.RefreshTokens.FindAsync([id], ct).AsTask();
    public Task<RefreshToken?> GetByToken(string tokenHash, CancellationToken ct) =>
        Db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    public Task<List<RefreshToken>> GetActiveByUserId(Guid userId, CancellationToken ct) =>
        Db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);
}