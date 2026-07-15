using Application.Features.Auth;
using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Tests.Auth.Handlers;

public class RegisterHandlerTests
{
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<INotificationPreferencesRepository> _notifPrefs;
    private readonly Mock<IEmailService> _email;
    private readonly Mock<IUserService> _user;
    private readonly Mock<IConfiguration> _config;
    private readonly Mock<ILogger<RegisterHandler>> _logger;
    private readonly RegisterHandler _sut;

    public RegisterHandlerTests()
    {
        _userManager = MockUserManagerFactory.Create();
        _notifPrefs = new Mock<INotificationPreferencesRepository>();
        _email = new Mock<IEmailService>();
        _user = new Mock<IUserService>();
        _user.Setup(s => s.ProvisionNewUser(
                It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _config = new Mock<IConfiguration>();
        _logger = new Mock<ILogger<RegisterHandler>>();

        _config.Setup(c => c["FrontendUrl:Verify"]).Returns("https://app.example.com/verify");

        _sut = new RegisterHandler(
            _userManager.Object,
            _notifPrefs.Object,
            _user.Object,
            _email.Object,
            _config.Object,
            _logger.Object);
    }

    [Fact]
    public async Task Handle_NewEmail_CreatesUserAndSendsVerificationEmail()
    {
        _userManager.Setup(m => m.FindByEmailAsync("new@example.com"))
            .ReturnsAsync((User?)null);
        _userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), "P@ssw0rd!"))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<User>()))
            .ReturnsAsync("token123");
        _notifPrefs.Setup(r => r.AddAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _notifPrefs.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Handle(
            new RegisterCommand("new@example.com", "P@ssw0rd!", "Tony", "Dev"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Message.Should().Contain("Verification link");
        _email.Verify(e => e.SendAsync("new@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ReturnsConflict()
    {
        _userManager.Setup(m => m.FindByEmailAsync("existing@example.com"))
            .ReturnsAsync(Fakes.TestUser("existing@example.com"));

        var result = await _sut.Handle(
            new RegisterCommand("existing@example.com", "P@ssw0rd!", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Conflict);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IdentityFailure_ReturnsValidationError()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((User?)null);
        _userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak." }));

        var result = await _sut.Handle(
            new RegisterCommand("new@example.com", "weak", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Validation);
        result.Error.Message.Should().Contain("Password too weak.");
    }
}

public class LoginHandlerTests
{
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepo;
    private readonly Mock<IConfiguration> _config;
    private readonly Mock<IJwtService> _jwt;
    private readonly LoginHandler _sut;

    public LoginHandlerTests()
    {
        _userManager = MockUserManagerFactory.Create();
        _refreshTokenRepo = new Mock<IRefreshTokenRepository>();
        _config = new Mock<IConfiguration>();
        _jwt = new Mock<IJwtService>();

        _config.Setup(c => c["Jwt:RefreshTokenExpiryDays"]).Returns("7");

        _sut = new LoginHandler(_userManager.Object, _refreshTokenRepo.Object, _config.Object, _jwt.Object);
    }

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsTokens()
    {
        var user = Fakes.TestUser();
        _userManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "P@ssw0rd!")).ReturnsAsync(true);
        _jwt.Setup(j => j.GenerateToken(user, It.IsAny<Guid?>())).Returns("access_token");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh_token");
        _refreshTokenRepo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _refreshTokenRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Handle(new LoginCommand(user.Email!, "P@ssw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("access_token");
        result.Value.RefreshToken.Should().Be("refresh_token");
        _refreshTokenRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokenRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WrongPassword_ReturnsUnauthorized()
    {
        var user = Fakes.TestUser();
        _userManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);

        var result = await _sut.Handle(new LoginCommand(user.Email!, "wrong"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Unauthorized);
    }

    [Fact]
    public async Task Handle_UnknownEmail_ReturnsUnauthorized()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var result = await _sut.Handle(new LoginCommand("ghost@example.com", "P@ssw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Unauthorized);
    }

    [Fact]
    public async Task Handle_UnverifiedEmail_ReturnsForbidden()
    {
        var user = Fakes.TestUser(emailConfirmed: false);
        _userManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "P@ssw0rd!")).ReturnsAsync(true);

        var result = await _sut.Handle(new LoginCommand(user.Email!, "P@ssw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Forbidden);
    }
}

public class ChangePasswordHandlerTests
{
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<ICurrentUser> _currentUser;
    private readonly ChangePasswordHandler _sut;

    public ChangePasswordHandlerTests()
    {
        _userManager = MockUserManagerFactory.Create();
        _currentUser = new Mock<ICurrentUser>();
        _sut = new ChangePasswordHandler(_userManager.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidChange_Succeeds()
    {
        var user = Fakes.TestUser();
        _currentUser.Setup(c => c.UserId).Returns(user.Id);
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.HasPasswordAsync(user)).ReturnsAsync(true);
        _userManager.Setup(m => m.ChangePasswordAsync(user, "OldP@ss1!", "NewP@ss1!"))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _sut.Handle(
            new ChangePasswordCommand("OldP@ss1!", "NewP@ss1!", "NewP@ss1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Message.Should().Contain("successfully");
    }

    [Fact]
    public async Task Handle_PasswordMismatch_ReturnsValidation()
    {
        var result = await _sut.Handle(
            new ChangePasswordCommand("OldP@ss1!", "NewP@ss1!", "DifferentP@ss1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Validation);
        result.Error.Message.Should().Contain("match");
    }

    [Fact]
    public async Task Handle_SameAsCurrentPassword_ReturnsValidation()
    {
        var result = await _sut.Handle(
            new ChangePasswordCommand("SameP@ss1!", "SameP@ss1!", "SameP@ss1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Validation);
        result.Error.Message.Should().Contain("different");
    }

    [Fact]
    public async Task Handle_GoogleOnlyAccount_ReturnsValidation()
    {
        var user = Fakes.TestUser();
        _currentUser.Setup(c => c.UserId).Returns(user.Id);
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.HasPasswordAsync(user)).ReturnsAsync(false);

        var result = await _sut.Handle(
            new ChangePasswordCommand("OldP@ss1!", "NewP@ss1!", "NewP@ss1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Validation);
        result.Error.Message.Should().Contain("Google");
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFound()
    {
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var result = await _sut.Handle(
            new ChangePasswordCommand("OldP@ss1!", "NewP@ss1!", "NewP@ss1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
}

public class VerifyTokenHandlerTests
{
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<INotificationPreferencesRepository> _notifPrefs;
    private readonly VerifyTokenHandler _sut;

    public VerifyTokenHandlerTests()
    {
        _userManager = MockUserManagerFactory.Create();
        _notifPrefs = new Mock<INotificationPreferencesRepository>();
        _notifPrefs.Setup(r => r.AddAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _notifPrefs.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sut = new VerifyTokenHandler(_userManager.Object, _notifPrefs.Object);
    }

    [Fact]
    public async Task Handle_ValidToken_ConfirmsEmail()
    {
        var user = Fakes.TestUser(emailConfirmed: false);
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.ConfirmEmailAsync(user, "validtoken"))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await _sut.Handle(
            new VerifyTokenCommand(user.Id.ToString(), "validtoken"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Message.Should().Contain("verified");
    }

    [Fact]
    public async Task Handle_InvalidToken_ReturnsValidationError()
    {
        var user = Fakes.TestUser(emailConfirmed: false);
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.ConfirmEmailAsync(user, "badtoken"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token." }));

        var result = await _sut.Handle(
            new VerifyTokenCommand(user.Id.ToString(), "badtoken"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Validation);
    }

    [Fact]
    public async Task Handle_AlreadyConfirmed_SkipsConfirmationAndSucceeds()
    {
        var user = Fakes.TestUser(emailConfirmed: true);
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);

        var result = await _sut.Handle(
            new VerifyTokenCommand(user.Id.ToString(), "anytoken"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userManager.Verify(m => m.ConfirmEmailAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFound()
    {
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var result = await _sut.Handle(
            new VerifyTokenCommand(Guid.NewGuid().ToString(), "token"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
}

// Shared helper — UserManager has no public parameterless constructor
internal static class MockUserManagerFactory
{
     public static Mock<UserManager<User>> Create()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}