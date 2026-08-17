using Application.Features.Waitlist;
using Application.Features.Waitlist.Commands;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using UserEntity = global::Domain.Entities.User;
using WaitlistEntity = global::Domain.Entities.Waitlist;

namespace Tests.Application.Waitlist.Commands;

public class JoinWaitlistHandlerTests
{
    private readonly Mock<IWaitlistRepository> _mockWaitlistRepo;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<IWaitlistCancellationTokenService> _mockCancellationTokenService;
    private readonly Mock<UserManager<UserEntity>> _mockUserManager;
    private readonly Mock<IWaitlistMailQueue> _mockQueue;
    private readonly Mock<IHttpContextAccessor> _mockHttp;
    private readonly Mock<ILogger<JoinWaitlistHandler>> _mockLogger;
    private readonly JoinWaitlistHandler _handler;

    public JoinWaitlistHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockEmailService = new Mock<IEmailService>();
        _mockConfig = new Mock<IConfiguration>();
        _mockCancellationTokenService = new Mock<IWaitlistCancellationTokenService>();
        _mockUserManager = MockUserManager();
        _mockQueue = new Mock<IWaitlistMailQueue>();
        _mockHttp = new Mock<IHttpContextAccessor>();
        _mockLogger = new Mock<ILogger<JoinWaitlistHandler>>();

        _mockConfig.Setup(c => c["FrontendUrl:WaitlistVerify"]).Returns("http://localhost:3000/verify");
        _mockConfig.Setup(c => c["FrontendUrl:WaitlistCancel"]).Returns("http://localhost:3000/waitlist/cancel");
        _mockConfig.Setup(c => c["FrontendUrl:WaitlistJoin"]).Returns("http://localhost:3000/waitlist");
        _mockCancellationTokenService
            .Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("cancel-token");

        _handler = new JoinWaitlistHandler(
            _mockWaitlistRepo.Object,
            _mockEmailService.Object,
            _mockConfig.Object,
            _mockCancellationTokenService.Object,
            _mockUserManager.Object,
            _mockQueue.Object,
            _mockHttp.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithValidEmail_CreatesWaitlistEntry()
    {
        // Arrange
        const string comments = "I want scheduled scans and Slack alerts.";
        var cmd = new JoinWaitlistCommand("test@example.com", "Test Company", comments);
        WaitlistEntity? addedEntry = null;

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);
        _mockWaitlistRepo.Setup(r => r.GetNextPosition(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        _mockWaitlistRepo.Setup(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()))
            .Callback<WaitlistEntity, CancellationToken>((entry, _) => addedEntry = entry)
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert: join creates a pending entry with NO position or referral code yet
        // (both are claimed on confirmation), so the response is generic.
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test@example.com", result.Value!.Email);
        Assert.Equal(0L, result.Value.Position);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);
        Assert.False(result.Value.EmailConfirmed);
        Assert.Null(result.Value.ReferralCode);
        Assert.Null(result.Value.ReferralLink);
        Assert.NotNull(addedEntry);
        Assert.Equal(comments, addedEntry!.Comments);
        Assert.Null(addedEntry.Position);
        Assert.Null(addedEntry.ReferralCode);

        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockCancellationTokenService.Verify(
            s => s.GenerateToken(addedEntry.Id, "test@example.com"),
            Times.Once);
        _mockEmailService.Verify(es => es.SendAsync(
            "test@example.com",
            "Confirm Your Email - Vulnwatch Waitlist",
            It.Is<string>(body => body.Contains("http://localhost:3000/waitlist/cancel?email=test%40example.com&token=cancel-token"))),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidReferralCode_StoresReferrerWithoutBumpingAtJoin()
    {
        // Arrange: referrer must be a confirmed entry so it owns a referral code.
        var referrer = WaitlistEntity.Create("referrer@example.com");
        referrer.ConfirmEmail(40L, "REF123");
        var cmd = new JoinWaitlistCommand(
            "new@example.com",
            "New Company",
            ReferralCode: " ref123 ");
        WaitlistEntity? addedEntry = null;

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);
        _mockWaitlistRepo.Setup(r => r.FindByReferralCode("REF123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(referrer);
        _mockWaitlistRepo.Setup(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()))
            .Callback<WaitlistEntity, CancellationToken>((entry, _) => addedEntry = entry)
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert: the referrer link is stored, but the bump is deferred to confirmation.
        Assert.True(result.IsSuccess);
        Assert.NotNull(addedEntry);
        Assert.Equal(referrer.Id, addedEntry!.ReferredByWaitlistId);
        _mockWaitlistRepo.Verify(r => r.ApplyReferralBump(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidReferralCode_JoinsWithoutApplyingBump()
    {
        // Arrange
        var cmd = new JoinWaitlistCommand("new@example.com", ReferralCode: "UNKNOWN");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);
        _mockWaitlistRepo.Setup(r => r.FindByReferralCode(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockWaitlistRepo.Setup(r => r.GetNextPosition(It.IsAny<CancellationToken>()))
            .ReturnsAsync(41L);
        _mockWaitlistRepo.Setup(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _mockWaitlistRepo.Verify(r => r.ApplyReferralBump(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithDuplicateEmail_ReturnsGenericSuccess()
    {
        // Arrange
        var cmd = new JoinWaitlistCommand("test@example.com");
        var existingEntry = WaitlistEntity.Create("test@example.com");
        existingEntry.ConfirmEmail(100L, "EXISTCODE1");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test@example.com", result.Value!.Email);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);

        // No new entry is created for a duplicate...
        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        // ...but a notice is queued for the address owner. It is queued rather than sent inline
        // because doing the eligibility check and send here made this path cost measurably more
        // than a miss, and the API response is masked either way. WaitlistMailDispatcher decides
        // whether anything actually goes out.
        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<WaitlistMailJob>(j => j.Kind == WaitlistMailKind.AlreadyJoinedNotice),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithPendingDuplicate_NotifiesOwnerWithoutLookingUpPosition()
    {
        // Arrange: an existing entry that joined but never confirmed its email.
        var cmd = new JoinWaitlistCommand("test@example.com");
        var existingEntry = WaitlistEntity.Create("test@example.com");
        existingEntry.GenerateEmailConfirmationToken("pending-token");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert: same masked response, and the owner is still notified...
        Assert.True(result.IsSuccess);
        Assert.Equal(WaitlistStatus.Pending, result.Value!.Status);
        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<WaitlistMailJob>(j => j.Kind == WaitlistMailKind.AlreadyJoinedNotice),
            It.IsAny<CancellationToken>()), Times.Once);
        // ...and the handler looks nothing up: position resolution now happens in the dispatcher,
        // which is the point — the request path must not do work that varies by recipient.
        _mockWaitlistRepo.Verify(r => r.GetLivePosition(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithRegisteredUser_ReturnsGenericSuccess()
    {
        // Arrange
        var cmd = new JoinWaitlistCommand("test@example.com");
        var existingUser = UserEntity.Create("test@example.com");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(existingUser);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test@example.com", result.Value!.Email);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);

        // No waitlist entry is created for an already-registered email...
        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        // ...but an already-registered notice is queued for the owner (masked response unchanged).
        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<WaitlistMailJob>(j => j.Kind == WaitlistMailKind.AlreadyRegisteredNotice),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The handler never sends a notice itself, throttled or not — it only queues. Throttling moved
    /// to WaitlistMailDispatcher, where suppressing a send costs the caller no observable time.
    /// Coverage for the throttle itself lives in WaitlistMailDispatcherTests.
    /// </summary>
    [Fact]
    public async Task Handle_ForANotice_QueuesRatherThanSendingInline()
    {
        var cmd = new JoinWaitlistCommand("test@example.com");
        var existingEntry = WaitlistEntity.Create("test@example.com");
        existingEntry.GenerateEmailConfirmationToken("pending-token");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _mockEmailService.Verify(
            es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _mockQueue.Verify(q => q.EnqueueAsync(
            It.IsAny<WaitlistMailJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NewJoin_UsesAllowlistedRequestOriginForConfirmationLink()
    {
        // Arrange: config points at prod, but the request comes from an allowlisted test origin.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl:WaitlistVerify"] = "https://prod.example.com/verify",
            ["FrontendUrl:WaitlistCancel"] = "https://prod.example.com/waitlist/cancel",
            ["FrontendUrl:AllowedOrigins:0"] = "https://test.example.com",
        }).Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Origin = "https://test.example.com";
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(httpContext);

        string? capturedBody = null;
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);
        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);

        var handler = new JoinWaitlistHandler(
            _mockWaitlistRepo.Object, _mockEmailService.Object, config,
            _mockCancellationTokenService.Object, _mockUserManager.Object,
            _mockQueue.Object, http.Object, _mockLogger.Object);

        // Act
        await handler.Handle(new JoinWaitlistCommand("new@example.com"), CancellationToken.None);

        // Assert: the emailed link uses the allowlisted origin, keeping the configured path.
        Assert.NotNull(capturedBody);
        Assert.Contains("https://test.example.com/verify", capturedBody);
        Assert.DoesNotContain("https://prod.example.com/verify", capturedBody);
    }

    [Fact]
    public async Task Handle_NewJoin_PersistsAllowlistedOriginOnEntry()
    {
        // Arrange: request comes from an allowlisted origin; it must be captured on the entry.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl:WaitlistVerify"] = "https://prod.example.com/verify",
            ["FrontendUrl:WaitlistCancel"] = "https://prod.example.com/waitlist/cancel",
            ["FrontendUrl:AllowedOrigins:0"] = "https://test.example.com",
        }).Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Origin = "https://test.example.com";
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(httpContext);

        WaitlistEntity? addedEntry = null;
        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);
        _mockWaitlistRepo.Setup(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()))
            .Callback<WaitlistEntity, CancellationToken>((entry, _) => addedEntry = entry)
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var handler = new JoinWaitlistHandler(
            _mockWaitlistRepo.Object, _mockEmailService.Object, config,
            _mockCancellationTokenService.Object, _mockUserManager.Object,
            _mockQueue.Object, http.Object, _mockLogger.Object);

        // Act
        await handler.Handle(new JoinWaitlistCommand("new@example.com"), CancellationToken.None);

        // Assert
        Assert.NotNull(addedEntry);
        Assert.Equal("https://test.example.com", addedEntry!.JoinOrigin);
    }

    [Fact]
    public async Task Handle_NewJoin_DoesNotPersistNonAllowlistedOrigin()
    {
        // Arrange: a spoofed / unknown origin must never be persisted.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl:WaitlistVerify"] = "https://prod.example.com/verify",
            ["FrontendUrl:WaitlistCancel"] = "https://prod.example.com/waitlist/cancel",
            ["FrontendUrl:AllowedOrigins:0"] = "https://test.example.com",
        }).Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Origin = "https://evil.example.com";
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(httpContext);

        WaitlistEntity? addedEntry = null;
        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);
        _mockWaitlistRepo.Setup(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()))
            .Callback<WaitlistEntity, CancellationToken>((entry, _) => addedEntry = entry)
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var handler = new JoinWaitlistHandler(
            _mockWaitlistRepo.Object, _mockEmailService.Object, config,
            _mockCancellationTokenService.Object, _mockUserManager.Object,
            _mockQueue.Object, http.Object, _mockLogger.Object);

        // Act
        await handler.Handle(new JoinWaitlistCommand("new@example.com"), CancellationToken.None);

        // Assert
        Assert.NotNull(addedEntry);
        Assert.Null(addedEntry!.JoinOrigin);
    }

    [Fact]
    public async Task Handle_NewJoin_IgnoresNonAllowlistedRequestOrigin()
    {
        // Arrange: request Origin is NOT on the allowlist — must fall back to the configured URL.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl:WaitlistVerify"] = "https://prod.example.com/verify",
            ["FrontendUrl:WaitlistCancel"] = "https://prod.example.com/waitlist/cancel",
            ["FrontendUrl:AllowedOrigins:0"] = "https://test.example.com",
        }).Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Origin = "https://evil.example.com"; // spoofed / unknown
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(httpContext);

        string? capturedBody = null;
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);
        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);

        var handler = new JoinWaitlistHandler(
            _mockWaitlistRepo.Object, _mockEmailService.Object, config,
            _mockCancellationTokenService.Object, _mockUserManager.Object,
            _mockQueue.Object, http.Object, _mockLogger.Object);

        // Act
        await handler.Handle(new JoinWaitlistCommand("new@example.com"), CancellationToken.None);

        // Assert: the spoofed origin is never reflected; the configured host is used.
        Assert.NotNull(capturedBody);
        Assert.Contains("https://prod.example.com/verify", capturedBody);
        Assert.DoesNotContain("evil.example.com", capturedBody);
    }

    [Fact]
    public async Task Handle_EmailServiceFails_ReturnsValidationAndDoesNotSave()
    {
        // Arrange
        var cmd = new JoinWaitlistCommand("test@example.com");
        WaitlistEntity? addedEntry = null;

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);
        _mockWaitlistRepo.Setup(r => r.GetNextPosition(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        _mockWaitlistRepo.Setup(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()))
            .Callback<WaitlistEntity, CancellationToken>((entry, _) => addedEntry = entry)
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Email service error"));

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.Validation, result.Error.Code);
        Assert.NotNull(addedEntry);

        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockWaitlistRepo.Verify(r => r.Remove(addedEntry!), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockWaitlistRepo.Verify(r => r.ApplyReferralBump(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithCancelledEntry_ReactivatesInsteadOfMasking()
    {
        // Arrange: a previously cancelled email should be able to rejoin.
        var cancelled = WaitlistEntity.Create("comeback@example.com");
        cancelled.ConfirmEmail(5L, "OLDCODE");
        cancelled.MarkCancelled();
        var cmd = new JoinWaitlistCommand("comeback@example.com");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelled);
        _mockUserManager.Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((UserEntity?)null);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert: reactivated in place (Update, not Add), fresh pending state, new confirmation email.
        Assert.True(result.IsSuccess);
        Assert.Equal(WaitlistStatus.Pending, cancelled.Status);
        Assert.False(cancelled.EmailConfirmed);
        Assert.Null(cancelled.Position);
        Assert.Null(cancelled.ReferralCode);

        _mockWaitlistRepo.Verify(r => r.Update(cancelled), Times.Once);
        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockEmailService.Verify(es => es.SendAsync("comeback@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    private static Mock<UserManager<UserEntity>> MockUserManager()
    {
        var store = new Mock<IUserStore<UserEntity>>();
        return new Mock<UserManager<UserEntity>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
