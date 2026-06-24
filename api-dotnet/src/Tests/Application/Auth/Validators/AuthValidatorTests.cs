using Application.Features.Auth;
using Application.Features.Scans;
using Domain.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Tests.Auth.Validators;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _sut = new();
 
    [Theory]
    [InlineData("tony@example.com", "P@ssw0rd1")]
    [InlineData("a@b.co", "anything")]
    public void Validate_ValidCommand_NoErrors(string email, string password)
    {
        var result = _sut.Validate(new LoginCommand(email, password));
        result.IsValid.Should().BeTrue();
    }
 
    [Fact]
    public void Validate_EmptyEmail_HasEmailError()
    {
        _sut.ShouldHaveValidationErrorFor(x => x.Email, new LoginCommand("", "P@ssw0rd1"));
    }
 
    [Fact]
    public void Validate_InvalidEmailFormat_HasEmailError()
    {
        _sut.ShouldHaveValidationErrorFor(x => x.Email, new LoginCommand("notanemail", "P@ssw0rd1"));
    }
 
    [Fact]
    public void Validate_EmptyPassword_HasPasswordError()
    {
        _sut.ShouldHaveValidationErrorFor(x => x.Password, new LoginCommand("tony@example.com", ""));
    }
}
 
