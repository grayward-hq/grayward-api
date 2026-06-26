using Application.Features.Waitlist.Commands;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
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
    private readonly Mock<UserManager<UserEntity>> _mockUserManager;
    private readonly Mock<ILogger<JoinWaitlistHandler>> _mockLogger;
    private readonly JoinWaitlistHandler _handler;

    public JoinWaitlistHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockEmailService = new Mock<IEmailService>();
        _mockConfig = new Mock<IConfiguration>();
        _mockUserManager = MockUserManager();
        _mockLogger = new Mock<ILogger<JoinWaitlistHandler>>();

        _mockConfig.Setup(c => c["FrontendUrl:WaitlistVerify"]).Returns("http://localhost:3000/verify");

        _handler = new JoinWaitlistHandler(
            _mockWaitlistRepo.Object,
            _mockEmailService.Object,
            _mockConfig.Object,
            _mockUserManager.Object,
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

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test@example.com", result.Value!.Email);
        Assert.Equal(1L, result.Value.Position);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);
        Assert.False(result.Value.EmailConfirmed);
        Assert.NotNull(addedEntry);
        Assert.Equal(comments, addedEntry!.Comments);

        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockEmailService.Verify(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithDuplicateEmail_ReturnsGenericSuccess()
    {
        // Arrange
        var cmd = new JoinWaitlistCommand("test@example.com");
        var existingEntry = WaitlistEntity.Create("test@example.com", null, 100L);
        existingEntry.ConfirmEmail();

        _mockWaitlistRepo.Setup(r => r.FindByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test@example.com", result.Value!.Email);
        Assert.Equal(WaitlistStatus.Pending, result.Value.Status);

        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockEmailService.Verify(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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

        _mockWaitlistRepo.Verify(r => r.AddAsync(It.IsAny<WaitlistEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockEmailService.Verify(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
    }

    private static Mock<UserManager<UserEntity>> MockUserManager()
    {
        var store = new Mock<IUserStore<UserEntity>>();
        return new Mock<UserManager<UserEntity>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
