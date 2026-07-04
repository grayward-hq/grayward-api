using Domain.Enums;

namespace Application.Features.Waitlist.DTOs;

public record JoinWaitlistRequest(
    string Email,
    string? CompanyName = null,
    string? Comments = null,
    string? ReferralCode = null);

public record WaitlistResponse(
    string Email,
    long Position,
    WaitlistStatus Status,
    DateTime CreatedAt,
    bool EmailConfirmed,
    DateTime? EmailConfirmedAt = null,
    string? ReferralCode = null,
    string? ReferralLink = null);

public record WaitlistStatusResponse(
    string Email,
    long Position,
    int TotalOnWaitlist,
    WaitlistStatus Status,
    bool EmailConfirmed,
    DateTime JoinedAt);

public record WaitlistListItemDto(
    Guid Id,
    string Email,
    string? CompanyName,
    long? Position,
    WaitlistStatus Status,
    bool EmailConfirmed,
    DateTime CreatedAt,
    DateTime? EmailConfirmedAt,
    DateTime? PromotedAt,
    string? Comments,
    string? Notes,
    string? ReferralCode = null,
    Guid? ReferredByWaitlistId = null,
    int ReferralCount = 0,
    long ReferralPosition = 0,
    DateTime? LastReferralAt = null);

public record UpdateWaitlistRequest(string? CompanyName = null, string? Notes = null);

public record WaitlistAnalyticsDto(
    int TotalOnWaitlist,
    int PendingCount,
    int EmailConfirmedCount,
    int PromotedCount,
    int CancelledCount,
    decimal PromotionRate,
    decimal CancellationRate,
    double AverageDaysToPromotion,
    List<TopCompanyDto> TopCompanies);

public record TopCompanyDto(string Company, int Count);

public record PromoteWaitlistResponse(
    Guid WaitlistId,
    Guid NewUserId,
    string Email,
    WaitlistStatus Status,
    DateTime PromotedAt);

public record CancelWaitlistRequest(string Email, string Token);

public record RequestWaitlistCancellationRequest(string Email);

public record VerifyWaitlistEmailRequest(string Email, string Token);
