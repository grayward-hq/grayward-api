using Application.Features.Waitlist.Queries;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using WaitlistEntity = global::Domain.Entities.Waitlist;

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
    public async Task Handle_WithValidEmail_ReturnsStatus()
    {
        // Arrange
        var email = "test@example.com";
        var query = new GetWaitlistStatusQuery(email);
        var entry = WaitlistEntity.Create(email, "Test Company", 42L);
        entry.ConfirmEmail();

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(WaitlistStatus.EmailConfirmed, result.Value!.Status);
        Assert.Equal(42L, result.Value.Position);
        Assert.Equal(100, result.Value.TotalOnWaitlist);
    }

    [Fact]
    public async Task Handle_WithNonExistentEmail_ReturnsNotFound()
    {
        // Arrange
        var query = new GetWaitlistStatusQuery("nonexistent@example.com");

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Handle_CaseInsensitiveSearch()
    {
        // Arrange
        var lowerEmail = "test@example.com";
        var mixedCaseEmail = "Test@Example.Com";
        var query = new GetWaitlistStatusQuery(mixedCaseEmail);
        var entry = WaitlistEntity.Create(lowerEmail, null, 5L);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _mockWaitlistRepo.Verify(r => r.FindByEmail(mixedCaseEmail.ToLower(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(WaitlistStatus.Pending)]
    [InlineData(WaitlistStatus.EmailConfirmed)]
    [InlineData(WaitlistStatus.Promoted)]
    [InlineData(WaitlistStatus.Cancelled)]
    public async Task Handle_ReturnsCorrectStatus(WaitlistStatus status)
    {
        // Arrange
        var email = "test@example.com";
        var query = new GetWaitlistStatusQuery(email);
        var entry = WaitlistEntity.Create(email, null, 1L);
        
        // Manually set status
        var reflectionProperty = entry.GetType().GetProperty("Status");
        reflectionProperty?.SetValue(entry, status);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockWaitlistRepo.Setup(r => r.GetTotalCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(status, result.Value!.Status);
    }
}
