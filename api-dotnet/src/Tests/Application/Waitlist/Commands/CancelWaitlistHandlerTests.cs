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
    private readonly Mock<ILogger<CancelWaitlistHandler>> _mockLogger;
    private readonly CancelWaitlistHandler _handler;

    public CancelWaitlistHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockLogger = new Mock<ILogger<CancelWaitlistHandler>>();
        _handler = new CancelWaitlistHandler(_mockWaitlistRepo.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithValidEntry_CancelsSuccessfully()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        var cmd = new CancelWaitlistCommand(email);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(WaitlistStatus.Cancelled, entry.Status);

        _mockWaitlistRepo.Verify(r => r.Update(entry), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentEntry_ReturnsNotFound()
    {
        // Arrange
        var cmd = new CancelWaitlistCommand("nonexistent@example.com");

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
    public async Task Handle_WithAlreadyCancelledEntry_ReturnsBadRequest()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        entry.MarkCancelled();

        var cmd = new CancelWaitlistCommand(email);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

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

        var cmd = new CancelWaitlistCommand(email);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

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
        var cmd = new CancelWaitlistCommand(mixedCaseEmail);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _mockWaitlistRepo.Verify(r => r.FindByEmail(mixedCaseEmail.ToLower(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
