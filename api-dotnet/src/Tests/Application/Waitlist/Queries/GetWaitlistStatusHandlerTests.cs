using Application.Features.Waitlist.Queries;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using WaitlistEntity = Domain.Entities.Waitlist;

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
    public async Task Handle_WithUnconfirmedEntry_ReturnsPendingWithNoPosition()
    {
        // Arrange: an unconfirmed entry has not claimed a queue slot yet.
        var entry = WaitlistEntity.Create("test@example.com");
        var query = new GetWaitlistStatusQuery("test@example.com");

        _mockWaitlistRepo.Setup(r => r.FindByEmail("test@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test@example.com", result.Value!.Email);
        Assert.Equal(0L, result.Value.Position);
        Assert.Equal(100, result.Value.TotalOnWaitlist);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);
        Assert.False(result.Value.EmailConfirmed);
        Assert.Equal(entry.CreatedAt, result.Value.JoinedAt);
        _mockWaitlistRepo.Verify(r => r.GetLivePosition(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithConfirmedEntry_ReturnsLiveRankNotRawSequence()
    {
        // Arrange: confirmed at sequence 7, but live rank is 3 (earlier entries left the queue).
        var entry = WaitlistEntity.Create("confirmed@example.com");
        entry.ConfirmEmail(7L, "CODE7");
        var query = new GetWaitlistStatusQuery("confirmed@example.com");

        _mockWaitlistRepo.Setup(r => r.FindByEmail("confirmed@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetLivePosition(7L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3L);
        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(3L, result.Value!.Position);
        Assert.True(result.Value.EmailConfirmed);
        Assert.Equal(WaitlistStatus.EmailConfirmed, result.Value.Status);
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_ReturnsNotFound()
    {
        // Arrange
        var query = new GetWaitlistStatusQuery("nonexistent@example.com");

        _mockWaitlistRepo.Setup(r => r.FindByEmail("nonexistent@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
        _mockWaitlistRepo.Verify(r => r.GetTotalCount(It.IsAny<CancellationToken>()), Times.Never);
    }
}
