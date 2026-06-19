using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class WaitlistRepository : IWaitlistRepository
{
    private readonly VulnWatchDbContext _context;

    public WaitlistRepository(VulnWatchDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Waitlist entity, CancellationToken ct = default)
    {
        await _context.Waitlists.AddAsync(entity, ct);
    }

    public void Update(Waitlist entity)
    {
        _context.Waitlists.Update(entity);
    }

    public void Remove(Waitlist entity)
    {
        _context.Waitlists.Remove(entity);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    public async Task<Waitlist?> FindByEmail(string email, CancellationToken ct)
    {
        return await _context.Waitlists
            .FirstOrDefaultAsync(w => w.Email.ToLower() == email.ToLower(), ct);
    }

    public async Task<Waitlist?> GetById(Guid id, CancellationToken ct)
    {
        return await _context.Waitlists.FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<long> GetNextPosition(CancellationToken ct)
    {
        var maxPosition = await _context.Waitlists
            .MaxAsync(w => (long?)w.Position, ct) ?? 0;
        return maxPosition + 1;
    }

    public async Task<long> GetPositionByEmail(string email, CancellationToken ct)
    {
        var entry = await FindByEmail(email, ct);
        return entry?.Position ?? -1;
    }

    public async Task<int> GetTotalCount(CancellationToken ct)
    {
        return await _context.Waitlists.CountAsync(ct);
    }

    public async Task<(List<Waitlist> Items, int TotalCount)> GetPaged(
        WaitlistStatus? status,
        int page,
        int pageSize,
        string? searchEmail,
        string sortBy,
        string sortOrder,
        CancellationToken ct)
    {
        var query = _context.Waitlists.AsQueryable();

        // Apply filters
        if (status.HasValue)
        {
            query = query.Where(w => w.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchEmail))
        {
            var lowerSearch = searchEmail.ToLower();
            query = query.Where(w => w.Email.ToLower().Contains(lowerSearch));
        }

        // Apply sorting
        query = ApplySorting(query, sortBy, sortOrder);

        // Get total count
        var totalCount = await query.CountAsync(ct);

        // Apply pagination
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<int> CountByStatus(WaitlistStatus status, CancellationToken ct)
    {
        return await _context.Waitlists
            .CountAsync(w => w.Status == status, ct);
    }

    public async Task<int> CountCreatedSince(DateTime since, CancellationToken ct)
    {
        return await _context.Waitlists
            .CountAsync(w => w.CreatedAt >= since, ct);
    }

    public async Task<int> CountPromotedSince(DateTime since, CancellationToken ct)
    {
        return await _context.Waitlists
            .CountAsync(w => w.Status == WaitlistStatus.Promoted && w.PromotedAt >= since, ct);
    }

    // public async Task<double> GetAverageDaysToPromotion(CancellationToken ct)
    // {
    //     var promoted = await _context.Waitlists
    //         .Where(w => w.Status == WaitlistStatus.Promoted && w.PromotedAt.HasValue)
    //         .Select(w => new
    //         {
    //             CreatedAt = w.CreatedAt,
    //             PromotedAt = w.PromotedAt!.Value
    //         })
    //         .ToListAsync(ct);

    //     if (promoted.Count == 0)
    //         return 0;

    //     var averageTicks = promoted
    //         .Select(p => (p.PromotedAt - p.CreatedAt).TotalSeconds)
    //         .Average();

    //     return averageTicks / (24 * 3600); // Convert to days
    // }


public async Task<double> GetAverageDaysToPromotion(CancellationToken ct)
{
    // 1. Check if any records match first to avoid database average errors on empty sets
    var hasPromoted = await _context.Waitlists
        .AnyAsync(w => w.Status == WaitlistStatus.Promoted && w.PromotedAt != null, ct);

    if (!hasPromoted)
        return 0;

    // 2. Let PostgreSQL calculate the average seconds directly
    var averageSeconds = await _context.Waitlists
        .Where(w => w.Status == WaitlistStatus.Promoted && w.PromotedAt != null)
        .Select(w => (w.PromotedAt!.Value - w.CreatedAt).TotalSeconds)
        .AverageAsync(ct);

    return averageSeconds / (24 * 3600); // Convert seconds to days
}


    public async Task<List<(string Company, int Count)>> GetTopCompanies(CancellationToken ct, int limit = 10)
    {
        var topCompanies = await _context.Waitlists
            .Where(w => w.CompanyName != null)
            .GroupBy(w => w.CompanyName)
            .OrderByDescending(g => g.Count())
            .Take(limit)
            .Select(g => new { Company = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return topCompanies.Select(tc => (tc.Company!, tc.Count)).ToList();
    }

    public async Task<bool> ExistsByEmail(string email, CancellationToken ct)
    {
        return await _context.Waitlists
            .AnyAsync(w => w.Email.ToLower() == email.ToLower(), ct);
    }

    public async Task<bool> ExistsByPromotedUserId(Guid userId, CancellationToken ct)
    {
        return await _context.Waitlists
            .AnyAsync(w => w.PromotedUserId == userId, ct);
    }

    private IQueryable<Waitlist> ApplySorting(IQueryable<Waitlist> query, string sortBy, string sortOrder)
    {
        var isAsc = sortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase);

        return sortBy.ToLower() switch
        {
            "position" => isAsc ? query.OrderBy(w => w.Position) : query.OrderByDescending(w => w.Position),
            "email" => isAsc ? query.OrderBy(w => w.Email) : query.OrderByDescending(w => w.Email),
            "status" => isAsc ? query.OrderBy(w => w.Status) : query.OrderByDescending(w => w.Status),
            "createdat" => isAsc ? query.OrderBy(w => w.CreatedAt) : query.OrderByDescending(w => w.CreatedAt),
            _ => query.OrderBy(w => w.Position),
        };
    }
}
