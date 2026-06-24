using Application.Features.Waitlist.Queries;
using Application.Interfaces;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Application.Waitlist.Queries;

public class GetWaitlistStatusHandlerTests
{
    private readonly Mock<IWaitlistRepository> _mockWaitlistRepo;
    private readonly Mock<ILogger<GetWaitlistStatusHandler>> _mockLogger;
    private readonly GetWaitlistStatusHandler _handler;

    public GetWaitlistStatusHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockLogger = new Mock<ILogger<GetWaitlistStatusHandler>>();
        _handler = new GetWaitlistStatusHandler(_mockWaitlistRepo.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithAnyEmail_ReturnsGenericStatus()
    {
        // Arrange
        var email = "test@example.com";
        var query = new GetWaitlistStatusQuery(email);

        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(email, result.Value!.Email);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);
        Assert.Equal(0L, result.Value.Position);
        Assert.Equal(100, result.Value.TotalOnWaitlist);
        Assert.False(result.Value.EmailConfirmed);
        _mockWaitlistRepo.Verify(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_ReturnsGenericSuccess()
    {
        // Arrange
        var query = new GetWaitlistStatusQuery("nonexistent@example.com");

        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("nonexistent@example.com", result.Value!.Email);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);
        Assert.Equal(0L, result.Value.Position);
        Assert.False(result.Value.EmailConfirmed);
    }

    [Fact]
    public async Task Handle_NormalizesEmail()
    {
        // Arrange
        var mixedCaseEmail = "Test@Example.Com";
        var query = new GetWaitlistStatusQuery(mixedCaseEmail);

        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(mixedCaseEmail.ToLowerInvariant(), result.Value!.Email);
        _mockWaitlistRepo.Verify(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotRevealStoredStatus()
    {
        // Arrange
        var email = "test@example.com";
        var query = new GetWaitlistStatusQuery(email);

        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(WaitlistStatus.Pending, result.Value!.Status);
        Assert.Equal(0L, result.Value.Position);
        _mockWaitlistRepo.Verify(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
