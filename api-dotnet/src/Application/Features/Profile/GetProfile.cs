using Application.Features.Profile.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Profile;

public record GetProfileQuery
    : IRequest<Result<UserProfileDto>>;

public class GetProfileHandler(
    UserManager<User> userManager,
    INotificationPreferencesRepository notifPrefs,
    IWaitlistRepository waitlistRepo,
    ICurrentUser currentUser,
    IConfiguration config)
    : IRequestHandler<GetProfileQuery, Result<UserProfileDto>>
{
    public async Task<Result<UserProfileDto>> Handle(GetProfileQuery query, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(currentUser.UserId.ToString());

        if (user is null)
            return Result<UserProfileDto>.Failure(Error.NotFound("User not found."));

        var prefs = await notifPrefs.GetByUserId(currentUser.UserId, ct);

        var prefsDto = prefs is null ? null : new NotificationPreferencesDto(
            EmailAlerts: prefs.EmailAlerts,
            SlackAlerts: prefs.SlackAlerts,
            PushNotifications: prefs.PushNotifications
        );

        // Fetch referral code if user was promoted from waitlist
        string? referralCode = null;
        string? referralLink = null;
        var waitlistEntry = await waitlistRepo.FindByPromotedUserId(currentUser.UserId, ct);
        if (waitlistEntry is not null)
        {
            referralCode = waitlistEntry.ReferralCode;
            referralLink = BuildReferralLink(referralCode);
        }

        return Result<UserProfileDto>.Success(new UserProfileDto(
            Id:               user.Id,
            Email:            user.Email!,
            FirstName:        user.FirstName,
            LastName:         user.LastName,
            ProfilePictureUrl: user.ProfilePictureUrl,
            EmailConfirmed:   user.EmailConfirmed,
            HasGoogleLinked:  user.GoogleId is not null,
            NotificationPreferences: prefsDto,
            CreatedAt:        user.CreatedAt,
            UpdatedAt:        user.UpdatedAt,
            ReferralCode:     referralCode,
            ReferralLink:     referralLink
        ));
    }

    private string BuildReferralLink(string referralCode)
    {
        var baseUrl = config["FrontendUrl:WaitlistJoin"] ?? config["FrontendUrl:Base"] ?? "http://localhost:3000";
        baseUrl = baseUrl.TrimEnd('/');
        return $"{baseUrl}?ref={Uri.EscapeDataString(referralCode)}";
    }
}