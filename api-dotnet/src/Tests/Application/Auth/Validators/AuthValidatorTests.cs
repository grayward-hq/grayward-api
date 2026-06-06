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
 
public class StartScanCommandValidatorTests
{
    private readonly StartScanCommandValidator _sut = new();

     
    [Fact]  
    public void Validate_InvalidCoverage_HasCoverageError()  
    {  
        _sut.ShouldHaveValidationErrorFor(  
            x => x.Coverage,  
            new StartScanCommand("example.com", (ScanCoverage)999, SurfaceType.Dns, Guid.NewGuid()));  
    }  
 
    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var result = _sut.Validate(new StartScanCommand(
            "example.com",
            ScanCoverage.Quick,
            SurfaceType.Dns | SurfaceType.Ssl,
            Guid.NewGuid()));
 
        result.IsValid.Should().BeTrue();
    }
 
    [Fact]
    public void Validate_EmptyDomain_HasDomainError()
    {
        _sut.ShouldHaveValidationErrorFor(x => x.Domain,
            new StartScanCommand("", ScanCoverage.Quick, SurfaceType.Dns, Guid.NewGuid()));
    }
 
    [Fact]
    public void Validate_InvalidDomainFormat_HasDomainError()
    {
        _sut.ShouldHaveValidationErrorFor(x => x.Domain,
            new StartScanCommand("not_a_domain", ScanCoverage.Quick, SurfaceType.Dns, Guid.NewGuid()));
    }
 
    [Fact]
    public void Validate_EmptyIdempotencyKey_HasIdempotencyError()
    {
        _sut.ShouldHaveValidationErrorFor(x => x.IdempotencyKey,
            new StartScanCommand("example.com", ScanCoverage.Quick, SurfaceType.Dns, Guid.Empty));
    }
 
    [Fact]
    public void Validate_DomainTooLong_HasDomainError()
    {
        _sut.ShouldHaveValidationErrorFor(x => x.Domain,
            new StartScanCommand(new string('a', 250) + ".com", ScanCoverage.Quick, SurfaceType.Dns, Guid.NewGuid()));
    }
}