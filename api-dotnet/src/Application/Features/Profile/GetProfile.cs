using Application.Features.Profile.DTOs;
using Application.Features.Waitlist;
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
    IHttpContextAccessor http,
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

            // Only build a link when a code actually exists — a promoted entry normally has one, but
            // ReferralCode is nullable, so guard rather than assume. The referral link is incidental
            // to the profile, so a missing FrontendUrl config must not fail the whole read.
            if (referralCode is not null)
            {
                try
                {
                    referralLink = WaitlistLinks.BuildReferralLink(
                        config, http.HttpContext?.Request, referralCode, waitlistEntry.JoinOrigin);
                }
                catch (InvalidOperationException)
                {
                    // FrontendUrl:WaitlistJoin not configured — leave the link null.
                }
            }
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
}