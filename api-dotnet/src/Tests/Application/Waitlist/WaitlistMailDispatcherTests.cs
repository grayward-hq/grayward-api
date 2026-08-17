using Application.Features.Waitlist;
using Application.Interfaces;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using WaitlistEntity = global::Domain.Entities.Waitlist;
using UserEntity = global::Domain.Entities.User;

namespace Tests.Application.Waitlist;

/// <summary>
/// Covers the rules that moved off the request path when waitlist mail became asynchronous: which
/// messages are eligible to send, and the per-recipient throttle.
/// </summary>
/// <remarks>
/// These used to be asserted through the handlers. They belong here now — the handlers only enqueue,
/// and deliberately know nothing about whether a send should happen, because doing that work in the
/// request made a hit measurably slower than a miss.
/// </remarks>
public class WaitlistMailDispatcherTests
{
    private readonly Mock<IWaitlistRepository> _repo = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IRedisService> _redis = new();
    private readonly Mock<IWaitlistCancellationTokenService> _tokens = new();
    private readonly Mock<UserManager<UserEntity>> _users = MockUserManager();
    private readonly WaitlistMailDispatcher _dispatcher;

    public WaitlistMailDispatcherTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl:WaitlistVerify"] = "https://app.example.com/waitlist/verify",
            ["FrontendUrl:WaitlistCancel"] = "https://app.example.com/waitlist/cancel",
        }).Build();

        // Default: the cooldown slot is free, so eligibility is what decides each test.
        _redis.Setup(r => r.TryClaimEmailCooldownSlot(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _tokens.Setup(t => t.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>())).Returns("cancel-token");

        _dispatcher = new WaitlistMailDispatcher(
            _repo.Object, _email.Object, _redis.Object, _tokens.Object, _users.Object,
            config, new Mock<ILogger<WaitlistMailDispatcher>>().Object);
    }

    private Task Dispatch(WaitlistMailKind kind, string email = "someone@example.com") =>
        _dispatcher.DispatchAsync(new WaitlistMailJob(kind, email, Origin: null), CancellationToken.None);

    private void VerifySent(Times times) => _email.Verify(
        e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), times);

    // ── Throttle ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The throttle now guards every kind from one place, and suppressing a send here costs the
    /// original caller no observable time.
    /// </summary>
    [Theory]
    [InlineData(WaitlistMailKind.AlreadyJoinedNotice)]
    [InlineData(WaitlistMailKind.AlreadyRegisteredNotice)]
    [InlineData(WaitlistMailKind.CancellationLink)]
    [InlineData(WaitlistMailKind.ResendConfirmation)]
    public async Task Dispatch_WhenCooldownSlotIsHeld_SendsNothing(WaitlistMailKind kind)
    {
        _redis.Setup(r => r.TryClaimEmailCooldownSlot(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Dispatch(kind);

        VerifySent(Times.Never());
        // Throttled means throttled: no lookup either, so a held slot costs nothing downstream.
        _repo.Verify(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Already registered ────────────────────────────────────────────────────

    [Fact]
    public async Task AlreadyRegistered_WithNoAccount_SendsNothing()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((UserEntity?)null);

        await Dispatch(WaitlistMailKind.AlreadyRegisteredNotice);

        VerifySent(Times.Never());
    }

    [Fact]
    public async Task AlreadyRegistered_WithAnAccount_Sends()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(UserEntity.Create("someone@example.com"));

        await Dispatch(WaitlistMailKind.AlreadyRegisteredNotice);

        VerifySent(Times.Once());
    }

    // ── Already joined ────────────────────────────────────────────────────────

    [Fact]
    public async Task AlreadyJoined_WithNoEntry_SendsNothing()
    {
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);

        await Dispatch(WaitlistMailKind.AlreadyJoinedNotice);

        VerifySent(Times.Never());
    }

    [Fact]
    public async Task AlreadyJoined_WithACancelledEntry_SendsNothing()
    {
        var entry = WaitlistEntity.Create("someone@example.com");
        entry.MarkCancelled();
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        await Dispatch(WaitlistMailKind.AlreadyJoinedNotice);

        VerifySent(Times.Never());
    }

    /// <summary>A pending entry has no position, so no position lookup should happen.</summary>
    [Fact]
    public async Task AlreadyJoined_WhenPending_SendsWithoutResolvingAPosition()
    {
        var entry = WaitlistEntity.Create("someone@example.com");
        entry.GenerateEmailConfirmationToken("pending-token");
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        await Dispatch(WaitlistMailKind.AlreadyJoinedNotice);

        VerifySent(Times.Once());
        _repo.Verify(r => r.GetLivePosition(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AlreadyJoined_WhenConfirmed_ResolvesPositionAndTotal()
    {
        var entry = WaitlistEntity.Create("someone@example.com");
        entry.ConfirmEmail(100L, "CODE12345");
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _repo.Setup(r => r.GetLivePosition(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(5L);
        _repo.Setup(r => r.CountByStatus(WaitlistStatus.EmailConfirmed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(126);

        await Dispatch(WaitlistMailKind.AlreadyJoinedNotice);

        VerifySent(Times.Once());
        _repo.Verify(r => r.GetLivePosition(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.CountByStatus(WaitlistStatus.EmailConfirmed, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Cancellation link ─────────────────────────────────────────────────────

    [Fact]
    public async Task CancellationLink_WithNoEntry_SendsNothing()
    {
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);

        await Dispatch(WaitlistMailKind.CancellationLink);

        VerifySent(Times.Never());
    }

    [Fact]
    public async Task CancellationLink_WhenAlreadyCancelled_SendsNothing()
    {
        var entry = WaitlistEntity.Create("someone@example.com");
        entry.MarkCancelled();
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        await Dispatch(WaitlistMailKind.CancellationLink);

        VerifySent(Times.Never());
    }

    [Fact]
    public async Task CancellationLink_WhenPending_Sends()
    {
        var entry = WaitlistEntity.Create("someone@example.com");
        entry.GenerateEmailConfirmationToken("pending-token");
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        await Dispatch(WaitlistMailKind.CancellationLink);

        VerifySent(Times.Once());
    }

    // ── Resend confirmation ───────────────────────────────────────────────────

    [Fact]
    public async Task Resend_WhenAlreadyConfirmed_SendsNothing()
    {
        var entry = WaitlistEntity.Create("someone@example.com");
        entry.ConfirmEmail(100L, "CODE12345");
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        await Dispatch(WaitlistMailKind.ResendConfirmation);

        VerifySent(Times.Never());
    }

    /// <summary>
    /// The resent link must be the one already mailed on join. Regenerating would invalidate the
    /// link the recipient may already be holding.
    /// </summary>
    [Fact]
    public async Task Resend_WhenPending_ReusesTheExistingTokenAndDoesNotWrite()
    {
        var entry = WaitlistEntity.Create("someone@example.com");
        entry.GenerateEmailConfirmationToken("original-token");
        _repo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        await Dispatch(WaitlistMailKind.ResendConfirmation);

        Assert.Equal("original-token", entry.EmailConfirmationToken);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(e => e.SendAsync(
            "someone@example.com",
            It.IsAny<string>(),
            It.Is<string>(body => body.Contains("original-token"))), Times.Once);
    }

    private static Mock<UserManager<UserEntity>> MockUserManager()
    {
        var store = new Mock<IUserStore<UserEntity>>();
        return new Mock<UserManager<UserEntity>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
