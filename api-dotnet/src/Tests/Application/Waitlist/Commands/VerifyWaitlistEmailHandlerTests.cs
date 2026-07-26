using Application.Features.Waitlist.Commands;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using WaitlistEntity = global::Domain.Entities.Waitlist;

namespace Tests.Application.Waitlist.Commands;

public class VerifyWaitlistEmailHandlerTests
{
    private readonly Mock<IWaitlistRepository> _mockWaitlistRepo;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<IHttpContextAccessor> _mockHttp;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<ILogger<VerifyWaitlistEmailHandler>> _mockLogger;
    private readonly VerifyWaitlistEmailHandler _handler;

    public VerifyWaitlistEmailHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockEmailService = new Mock<IEmailService>();
        _mockConfig = new Mock<IConfiguration>();
        _mockHttp = new Mock<IHttpContextAccessor>();
        _mockUow = new Mock<IUnitOfWork>();
        _mockLogger = new Mock<ILogger<VerifyWaitlistEmailHandler>>();
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockConfig.Setup(c => c["FrontendUrl:WaitlistJoin"]).Returns("http://localhost:3000/waitlist");
        // Run the transactional work inline (no real transaction in unit tests).
        _mockUow.Setup(u => u.InTransaction(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> work, CancellationToken token) => work(token));
        _handler = new VerifyWaitlistEmailHandler(
            _mockWaitlistRepo.Object, _mockEmailService.Object, _mockConfig.Object,
            _mockHttp.Object, _mockUow.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithValidToken_ConfirmsEmailAndAssignsPositionAndReferralCode()
    {
        // Arrange
        var email = "test@example.com";
        var token = "valid-token-12345";
        var entry = WaitlistEntity.Create(email, null);
        entry.GenerateEmailConfirmationToken(token);

        var cmd = new VerifyWaitlistEmailCommand(email, token);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetNextPosition(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);
        _mockWaitlistRepo.Setup(r => r.FindByReferralCode(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockWaitlistRepo.Setup(r => r.GetLivePosition(5L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3L);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(entry.EmailConfirmed);
        Assert.Equal(WaitlistStatus.EmailConfirmed, entry.Status);
        Assert.Null(entry.EmailConfirmationToken);
        Assert.Equal(5L, entry.Position);
        Assert.False(string.IsNullOrWhiteSpace(entry.ReferralCode));
        // Response carries the live rank, not the raw sequence.
        Assert.Equal(3L, result.Value!.Position);
        Assert.Equal(entry.ReferralCode, result.Value.ReferralCode);

        _mockWaitlistRepo.Verify(r => r.Update(entry), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // A post-confirmation email with position + referral link is sent to the user.
        _mockEmailService.Verify(
            es => es.SendAsync(email, It.IsAny<string>(), It.Is<string>(b => b.Contains(entry.ReferralCode!))),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReferralLink_UsesPersistedJoinOriginWhenRequestHasNoHeader()
    {
        // Arrange: config points at prod, but the entry joined from an allowlisted test origin.
        // The verify request carries no Origin header (typical email-link navigation).
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl:WaitlistJoin"] = "https://prod.example.com/waitlist",
            ["FrontendUrl:AllowedOrigins:0"] = "https://test.example.com",
        }).Build();

        var email = "test@example.com";
        var token = "valid-token-12345";
        var entry = WaitlistEntity.Create(email, null);
        entry.GenerateEmailConfirmationToken(token);
        entry.SetJoinOrigin("https://test.example.com");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetNextPosition(It.IsAny<CancellationToken>())).ReturnsAsync(5L);
        _mockWaitlistRepo.Setup(r => r.FindByReferralCode(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockWaitlistRepo.Setup(r => r.GetLivePosition(5L, It.IsAny<CancellationToken>())).ReturnsAsync(3L);

        string? capturedBody = null;
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);

        var handler = new VerifyWaitlistEmailHandler(
            _mockWaitlistRepo.Object, _mockEmailService.Object, config,
            _mockHttp.Object, _mockUow.Object, _mockLogger.Object);

        // Act
        var result = await handler.Handle(new VerifyWaitlistEmailCommand(email, token), CancellationToken.None);

        // Assert: the referral link routes to the persisted test origin, not the configured prod host.
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedBody);
        Assert.Contains("https://test.example.com/waitlist?ref=", capturedBody);
        Assert.DoesNotContain("prod.example.com", capturedBody);
        Assert.StartsWith("https://test.example.com/waitlist?ref=", result.Value!.ReferralLink);
    }

    [Fact]
    public async Task Handle_WithValidToken_AppliesReferrerCredit()
    {
        // Arrange: a referred entry credits its referrer on confirmation (not at join).
        var referrerId = Guid.NewGuid();
        var email = "referred@example.com";
        var token = "valid-token-12345";
        var entry = WaitlistEntity.Create(email, null, referredByWaitlistId: referrerId);
        entry.GenerateEmailConfirmationToken(token);

        var cmd = new VerifyWaitlistEmailCommand(email, token);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetNextPosition(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);
        _mockWaitlistRepo.Setup(r => r.FindByReferralCode(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockWaitlistRepo.Setup(r => r.ApplyReferralBump(referrerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _mockWaitlistRepo.Verify(r => r.ApplyReferralBump(referrerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenReferralBumpFails_ReturnsFailureAndDoesNotSendEmail()
    {
        // Arrange: the referral bump throws inside the transaction. Because confirmation and credit
        // are now atomic, the whole thing rolls back — the caller gets a retryable failure rather
        // than a confirmed user whose referrer was silently never credited (issue #260).
        var referrerId = Guid.NewGuid();
        var email = "referred@example.com";
        var token = "valid-token-12345";
        var entry = WaitlistEntity.Create(email, null, referredByWaitlistId: referrerId);
        entry.GenerateEmailConfirmationToken(token);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetNextPosition(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);
        _mockWaitlistRepo.Setup(r => r.FindByReferralCode(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);
        _mockWaitlistRepo.Setup(r => r.ApplyReferralBump(referrerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("transient db error"));

        // Act
        var result = await _handler.Handle(new VerifyWaitlistEmailCommand(email, token), CancellationToken.None);

        // Assert: failure surfaced, no post-confirmation email sent.
        Assert.False(result.IsSuccess);
        _mockEmailService.Verify(
            es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsBadRequest()
    {
        // Arrange
        var email = "test@example.com";
        var correctToken = "valid-token-12345";
        var wrongToken = "wrong-token";

        var entry = WaitlistEntity.Create(email, null);
        entry.GenerateEmailConfirmationToken(correctToken);

        var cmd = new VerifyWaitlistEmailCommand(email, wrongToken);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.Validation, result.Error.Code);
        Assert.False(entry.EmailConfirmed);

        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_ReturnsNotFound()
    {
        // Arrange
        var cmd = new VerifyWaitlistEmailCommand("nonexistent@example.com", "token");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithAlreadyConfirmedEmail_ReturnsBadRequest()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null);
        entry.ConfirmEmail(1, "PROMOCODE1");

        var cmd = new VerifyWaitlistEmailCommand(email, "some-token");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.Conflict, result.Error.Code);
    }
}
