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

public class PromoteWaitlistHandlerTests
{
    private readonly Mock<IWaitlistRepository> _mockWaitlistRepo;
    private readonly Mock<UserManager<UserEntity>> _mockUserManager;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<PromoteWaitlistHandler>> _mockLogger;
    private readonly PromoteWaitlistHandler _handler;

    public PromoteWaitlistHandlerTests()
    {
        _mockWaitlistRepo = new Mock<IWaitlistRepository>();
        _mockUserManager = MockUserManager();
        _mockEmailService = new Mock<IEmailService>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<PromoteWaitlistHandler>>();

        _mockConfig.Setup(c => c["FrontendUrl:PasswordReset"])
            .Returns("https://app.example.com/set-password");

        _handler = new PromoteWaitlistHandler(
            _mockWaitlistRepo.Object,
            _mockUserManager.Object,
            _mockEmailService.Object,
            _mockConfig.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithConfirmedWaitlist_ConfirmsUserEmailAndSendsPasswordSetupInvite()
    {
        // Arrange
        var entry = WaitlistEntity.Create("test@example.com", null);
        entry.ConfirmEmail(1, "PROMOCODE1");
        UserEntity? createdUser = null;

        _mockWaitlistRepo.Setup(r => r.GetById(entry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockUserManager.Setup(um => um.FindByEmailAsync(entry.Email))
            .ReturnsAsync((UserEntity?)null);
        _mockUserManager.Setup(um => um.CreateAsync(It.IsAny<UserEntity>()))
            .Callback<UserEntity>(user => createdUser = user)
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(um => um.GenerateEmailConfirmationTokenAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync("confirm-token");
        _mockUserManager.Setup(um => um.ConfirmEmailAsync(It.IsAny<UserEntity>(), "confirm-token"))
            .Callback<UserEntity, string>((user, _) => user.EmailConfirmed = true)
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(um => um.GeneratePasswordResetTokenAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync("reset token");
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new PromoteWaitlistCommand(entry.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(createdUser);
        Assert.True(createdUser!.EmailConfirmed);
        Assert.Equal(WaitlistStatus.Promoted, entry.Status);
        Assert.Equal(createdUser.Id, entry.PromotedUserId);

        _mockUserManager.Verify(um => um.CreateAsync(It.Is<UserEntity>(u => u.Email == entry.Email)), Times.Once);
        _mockUserManager.Verify(um => um.CreateAsync(It.IsAny<UserEntity>(), It.IsAny<string>()), Times.Never);
        _mockUserManager.Verify(um => um.GenerateEmailConfirmationTokenAsync(createdUser), Times.Once);
        _mockUserManager.Verify(um => um.ConfirmEmailAsync(createdUser, "confirm-token"), Times.Once);
        _mockUserManager.Verify(um => um.GeneratePasswordResetTokenAsync(createdUser), Times.Once);
        _mockWaitlistRepo.Verify(r => r.Update(entry), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockEmailService.Verify(es => es.SendAsync(
            entry.Email,
            "Welcome to Vulnwatch - Set Your Password",
            It.Is<string>(body => body.Contains("https://app.example.com/set-password/?email=test%40example.com&token=reset%20token"))),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithSendInvitationEmailFalse_ConfirmsUserEmailWithoutSendingPasswordSetupInvite()
    {
        // Arrange
        var entry = WaitlistEntity.Create("test@example.com", null);
        entry.ConfirmEmail(1, "PROMOCODE1");
        UserEntity? createdUser = null;

        _mockWaitlistRepo.Setup(r => r.GetById(entry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockUserManager.Setup(um => um.FindByEmailAsync(entry.Email))
            .ReturnsAsync((UserEntity?)null);
        _mockUserManager.Setup(um => um.CreateAsync(It.IsAny<UserEntity>()))
            .Callback<UserEntity>(user => createdUser = user)
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(um => um.GenerateEmailConfirmationTokenAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync("confirm-token");
        _mockUserManager.Setup(um => um.ConfirmEmailAsync(It.IsAny<UserEntity>(), "confirm-token"))
            .Callback<UserEntity, string>((user, _) => user.EmailConfirmed = true)
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(
            new PromoteWaitlistCommand(entry.Id, SendInvitationEmail: false),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(createdUser);
        Assert.True(createdUser!.EmailConfirmed);
        Assert.Equal(WaitlistStatus.Promoted, entry.Status);
        Assert.Equal(createdUser.Id, entry.PromotedUserId);

        _mockUserManager.Verify(um => um.CreateAsync(It.Is<UserEntity>(u => u.Email == entry.Email)), Times.Once);
        _mockUserManager.Verify(um => um.GenerateEmailConfirmationTokenAsync(createdUser), Times.Once);
        _mockUserManager.Verify(um => um.ConfirmEmailAsync(createdUser, "confirm-token"), Times.Once);
        _mockUserManager.Verify(um => um.GeneratePasswordResetTokenAsync(It.IsAny<UserEntity>()), Times.Never);
        _mockEmailService.Verify(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockWaitlistRepo.Verify(r => r.Update(entry), Times.Once);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailConfirmationFails_RollsBackUserAndDoesNotPromote()
    {
        // Arrange
        var entry = WaitlistEntity.Create("test@example.com", null);
        entry.ConfirmEmail(1, "PROMOCODE1");
        UserEntity? createdUser = null;

        _mockWaitlistRepo.Setup(r => r.GetById(entry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockUserManager.Setup(um => um.FindByEmailAsync(entry.Email))
            .ReturnsAsync((UserEntity?)null);
        _mockUserManager.Setup(um => um.CreateAsync(It.IsAny<UserEntity>()))
            .Callback<UserEntity>(user => createdUser = user)
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(um => um.GenerateEmailConfirmationTokenAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync("confirm-token");
        _mockUserManager.Setup(um => um.ConfirmEmailAsync(It.IsAny<UserEntity>(), "confirm-token"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token." }));
        _mockUserManager.Setup(um => um.DeleteAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(new PromoteWaitlistCommand(entry.Id), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.NotNull(createdUser);
        Assert.Equal(WaitlistStatus.EmailConfirmed, entry.Status);
        Assert.Null(entry.PromotedUserId);

        _mockUserManager.Verify(um => um.DeleteAsync(createdUser!), Times.Once);
        _mockWaitlistRepo.Verify(r => r.Update(It.IsAny<WaitlistEntity>()), Times.Never);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mockUserManager.Verify(um => um.GeneratePasswordResetTokenAsync(It.IsAny<UserEntity>()), Times.Never);
        _mockEmailService.Verify(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenInvitationEmailFails_RollsBackUserAndDoesNotPromote()
    {
        // Arrange
        var entry = WaitlistEntity.Create("test@example.com", null);
        entry.ConfirmEmail(1, "PROMOCODE1");
        UserEntity? createdUser = null;

        _mockWaitlistRepo.Setup(r => r.GetById(entry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _mockUserManager.Setup(um => um.FindByEmailAsync(entry.Email))
            .ReturnsAsync((UserEntity?)null);
        _mockUserManager.Setup(um => um.CreateAsync(It.IsAny<UserEntity>()))
            .Callback<UserEntity>(user => createdUser = user)
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(um => um.GenerateEmailConfirmationTokenAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync("confirm-token");
        _mockUserManager.Setup(um => um.ConfirmEmailAsync(It.IsAny<UserEntity>(), "confirm-token"))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(um => um.GeneratePasswordResetTokenAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync("reset-token");
        _mockUserManager.Setup(um => um.DeleteAsync(It.IsAny<UserEntity>()))
            .ReturnsAsync(IdentityResult.Success);
        _mockEmailService.Setup(es => es.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        // Act
        var result = await _handler.Handle(new PromoteWaitlistCommand(entry.Id), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.NotNull(createdUser);
        Assert.Equal(WaitlistStatus.EmailConfirmed, entry.Status);
        Assert.Null(entry.PromotedUserId);

        _mockUserManager.Verify(um => um.DeleteAsync(createdUser!), Times.Once);
        _mockWaitlistRepo.Verify(r => r.Update(It.IsAny<WaitlistEntity>()), Times.Never);
        _mockWaitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<UserManager<UserEntity>> MockUserManager()
    {
        var store = new Mock<IUserStore<UserEntity>>();
        return new Mock<UserManager<UserEntity>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
