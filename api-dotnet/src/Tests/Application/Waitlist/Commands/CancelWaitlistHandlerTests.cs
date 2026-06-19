using Application.Features.Waitlist.Commands;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using WaitlistEntity = global::Domain.Entities.Waitlist;

namespace Tests.Application.Waitlist.Commands;

public class CancelWaitlistHandlerTests
{
    private readonly Mock<IWaitlistRepository> _mockWaitlistRepo;
    private readonly Mock<IWaitlistCancellationTokenService> _mockTokenService;
    private readonly Mock<ILogger<CancelWaitlistHandler>> _mockLogger;
    private readonly CancelWaitlistHandler _handler;

    private const string ValidToken = "valid-token";

    public CancelWaitlistHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockLogger = new Mock<ILogger<CancelWaitlistHandler>>();
        _mockTokenService = new Mock<IWaitlistCancellationTokenService>();
        _handler = new CancelWaitlistHandler(_mockWaitlistRepo.Object, _mockTokenService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithValidEntryAndValidToken_CancelsSuccessfully()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        var cmd = new CancelWaitlistCommand(email, ValidToken);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockTokenService.Setup(t => t.ValidateToken(ValidToken, entry.Id, email))
            .Returns(true);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(WaitlistStatus.Cancelled, entry.Status);

        _mockWaitlistRepo.Verify(r => r.Update(entry), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsUnauthorizedAndDoesNotCancel()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        var cmd = new CancelWaitlistCommand(email, "wrong-token");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockTokenService.Setup(t => t.ValidateToken(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.Unauthorized, result.Error.Code);
        Assert.Equal(WaitlistStatus.Pending, entry.Status);

        _mockWaitlistRepo.Verify(r => r.Update(It.IsAny<WaitlistEntity>()), Times.Never);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithMissingToken_ReturnsUnauthorizedAndDoesNotCancel()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        var cmd = new CancelWaitlistCommand(email, string.Empty);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockTokenService.Setup(t => t.ValidateToken(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Unauthorized, result.Error!.Code);

        _mockWaitlistRepo.Verify(r => r.Update(It.IsAny<WaitlistEntity>()), Times.Never);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TokenValidatedAgainstCorrectEntryIdAndEmail()
    {
        // Arrange: ensures the token is checked against *this* entry's id and
        // normalized email, not just "any valid-looking token" — guards
        // against a token minted for one entry being replayed on another.
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        var cmd = new CancelWaitlistCommand(email, ValidToken);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockTokenService.Setup(t => t.ValidateToken(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        _mockTokenService.Verify(
            t => t.ValidateToken(ValidToken, entry.Id, email),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentEntry_ReturnsNotFound()
    {
        // Arrange
        var cmd = new CancelWaitlistCommand("nonexistent@example.com", ValidToken);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.NotFound, result.Error.Code);

        // Token should never be checked when there's no entry to check it against.
        _mockTokenService.Verify(
            t => t.ValidateToken(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithAlreadyCancelledEntry_ReturnsBadRequest()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        entry.MarkCancelled();

        var cmd = new CancelWaitlistCommand(email, ValidToken);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockTokenService.Setup(t => t.ValidateToken(ValidToken, entry.Id, email))
            .Returns(true);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.Conflict, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithPromotedEntry_ReturnsBadRequest()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        entry.ConfirmEmail();
        entry.MarkPromoted(Guid.NewGuid());

        var cmd = new CancelWaitlistCommand(email, ValidToken);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockTokenService.Setup(t => t.ValidateToken(ValidToken, entry.Id, email))
            .Returns(true);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.Conflict, result.Error.Code);
    }

    [Fact]
    public async Task Handle_CaseInsensitiveEmail()
    {
        // Arrange
        var lowerEmail = "test@example.com";
        var mixedCaseEmail = "Test@Example.Com";
        var entry = WaitlistEntity.Create(lowerEmail, null, 1L);
        var cmd = new CancelWaitlistCommand(mixedCaseEmail, ValidToken);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockTokenService.Setup(t => t.ValidateToken(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(true);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _mockWaitlistRepo.Verify(r => r.FindByEmail(mixedCaseEmail.ToLower(), It.IsAny<CancellationToken>()), Times.Once);

        // Token should be validated against the *normalized* (lowercased) email,
        // matching what the handler actually signs/checks against.
        _mockTokenService.Verify(
            t => t.ValidateToken(ValidToken, entry.Id, lowerEmail),
            Times.Once);
    }
}