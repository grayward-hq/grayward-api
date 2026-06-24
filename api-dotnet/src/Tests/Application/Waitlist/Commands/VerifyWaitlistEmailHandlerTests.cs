using Application.Features.Waitlist.Commands;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using WaitlistEntity = global::Domain.Entities.Waitlist;

namespace Tests.Application.Waitlist.Commands;

public class VerifyWaitlistEmailHandlerTests
{
    private readonly Mock<IWaitlistRepository> _mockWaitlistRepo;
    private readonly Mock<ILogger<VerifyWaitlistEmailHandler>> _mockLogger;
    private readonly VerifyWaitlistEmailHandler _handler;

    public VerifyWaitlistEmailHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockLogger = new Mock<ILogger<VerifyWaitlistEmailHandler>>();
        _handler = new VerifyWaitlistEmailHandler(_mockWaitlistRepo.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithValidToken_ConfirmsEmail()
    {
        // Arrange
        var email = "test@example.com";
        var token = "valid-token-12345";
        var entry = WaitlistEntity.Create(email, null, 1L);
        entry.GenerateEmailConfirmationToken(token);

        var cmd = new VerifyWaitlistEmailCommand(email, token);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(entry.EmailConfirmed);
        Assert.Equal(WaitlistStatus.EmailConfirmed, entry.Status);
        Assert.Null(entry.EmailConfirmationToken);

        _mockWaitlistRepo.Verify(r => r.Update(entry), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsBadRequest()
    {
        // Arrange
        var email = "test@example.com";
        var correctToken = "valid-token-12345";
        var wrongToken = "wrong-token";
        
        var entry = WaitlistEntity.Create(email, null, 1L);
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
        var entry = WaitlistEntity.Create(email, null, 1L);
        entry.ConfirmEmail();

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
