using Application.Features.Waitlist.Commands;
using Application.Interfaces;
using Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using WaitlistEntity = global::Domain.Entities.Waitlist;

namespace Tests.Application.Waitlist.Commands;

public class RequestWaitlistCancellationHandlerTests
{
    private const string GenericMessage =
        "If this email is on the waitlist, a cancellation link has been sent.";

    private readonly Mock<IWaitlistRepository> _mockWaitlistRepo;
    private readonly Mock<IWaitlistCancellationTokenService> _mockTokenService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<RequestWaitlistCancellationHandler>> _mockLogger;
    private readonly RequestWaitlistCancellationHandler _handler;

    public RequestWaitlistCancellationHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockTokenService = new Mock<IWaitlistCancellationTokenService>();
        _mockEmailService = new Mock<IEmailService>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<RequestWaitlistCancellationHandler>>();

        _mockConfig.Setup(c => c["FrontendUrl:WaitlistCancel"])
            .Returns("https://app.example.com/waitlist/cancel");
        _mockTokenService
            .Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("cancel token");
        _mockEmailService
            .Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _handler = new RequestWaitlistCancellationHandler(
            _mockWaitlistRepo.Object,
            _mockTokenService.Object,
            _mockEmailService.Object,
            _mockConfig.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithPendingEntry_SendsCancellationLinkAndReturnsGenericSuccess()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand(email),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(GenericMessage, result.Value!.Message);

        _mockTokenService.Verify(s => s.GenerateToken(entry.Id, email), Times.Once);
        _mockEmailService.Verify(es => es.SendAsync(
            email,
            "Cancel your Vulnwatch waitlist spot",
            It.Is<string>(body => body.Contains("https://app.example.com/waitlist/cancel?email=test%40example.com&token=cancel%20token"))),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentEntry_ReturnsGenericSuccessAndDoesNotSendEmail()
    {
        // Arrange
        var email = "missing@example.com";

        _mockWaitlistRepo.Setup(r => r.FindByEmail(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntity?)null);

        // Act
        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand(email),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(GenericMessage, result.Value!.Message);

        _mockTokenService.Verify(
            s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
        _mockEmailService.Verify(
            es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithAlreadyCancelledEntry_ReturnsGenericSuccessAndDoesNotSendEmail()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        entry.MarkCancelled();

        _mockWaitlistRepo.Setup(r => r.FindByEmail(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand(email),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(WaitlistStatus.Cancelled, entry.Status);

        _mockTokenService.Verify(
            s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
        _mockEmailService.Verify(
            es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithPromotedEntry_ReturnsGenericSuccessAndDoesNotSendEmail()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);
        entry.MarkPromoted(Guid.NewGuid());

        _mockWaitlistRepo.Setup(r => r.FindByEmail(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand(email),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(WaitlistStatus.Promoted, entry.Status);

        _mockTokenService.Verify(
            s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
        _mockEmailService.Verify(
            es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmailServiceFails_ReturnsGenericSuccess()
    {
        // Arrange
        var email = "test@example.com";
        var entry = WaitlistEntity.Create(email, null, 1L);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockEmailService
            .Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        // Act
        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand(email),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(GenericMessage, result.Value!.Message);
    }

    [Fact]
    public async Task Handle_NormalizesEmailBeforeLookupAndTokenGeneration()
    {
        // Arrange
        var lowerEmail = "test@example.com";
        var mixedCaseEmail = " Test@Example.Com ";
        var entry = WaitlistEntity.Create(lowerEmail, null, 1L);

        _mockWaitlistRepo.Setup(r => r.FindByEmail(lowerEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand(mixedCaseEmail),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _mockWaitlistRepo.Verify(r => r.FindByEmail(lowerEmail, It.IsAny<CancellationToken>()), Times.Once);
        _mockTokenService.Verify(s => s.GenerateToken(entry.Id, lowerEmail), Times.Once);
    }
}
