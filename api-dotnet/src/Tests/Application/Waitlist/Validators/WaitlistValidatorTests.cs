using Application.Features.Waitlist.DTOs;
using Application.Features.Waitlist.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Tests.Application.Waitlist.Validators;

public class JoinWaitlistValidatorTests
{
    private readonly JoinWaitlistValidator _sut = new();

    private static JoinWaitlistRequest Request(
        string email = "user@example.com",
        string? company = null,
        string? comments = null,
        string? referralCode = null)
        => new(email, company, comments, referralCode);

    [Fact]
    public void Validate_CleanRequest_NoErrors()
    {
        var result = _sut.Validate(Request(
            company: "Acme Corp",
            comments: "Looking forward to it.\nThanks!",
            referralCode: "ABC123def"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Acme <b>Corp</b>")]
    public void Validate_CompanyNameWithAngleBrackets_HasError(string company)
    {
        _sut.ShouldHaveValidationErrorFor(x => x.CompanyName, Request(company: company));
    }

    [Fact]
    public void Validate_CompanyNameWithControlChar_HasError()
    {
        // Embedded control character (bell, U+0007) — no legitimate use, common obfuscation vector.
        _sut.ShouldHaveValidationErrorFor(x => x.CompanyName, Request(company: "Acme" + (char)7 + "Corp"));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("I want <b>scans</b>")]
    public void Validate_CommentsWithAngleBrackets_HasError(string comments)
    {
        _sut.ShouldHaveValidationErrorFor(x => x.Comments, Request(comments: comments));
    }

    [Fact]
    public void Validate_CommentsWithControlChar_HasError()
    {
        // Embedded NUL byte (U+0000).
        _sut.ShouldHaveValidationErrorFor(x => x.Comments, Request(comments: "hello" + (char)0 + "world"));
    }

    [Fact]
    public void Validate_CommentsWithNewlinesAndTabs_NoError()
    {
        // Ordinary whitespace is legitimate in free text and must not be rejected.
        _sut.ShouldNotHaveValidationErrorFor(x => x.Comments, Request(comments: "line one\r\nline two\tindented"));
    }

    [Theory]
    [InlineData("ABC-123")]
    [InlineData("code with space")]
    [InlineData("<inject>")]
    public void Validate_NonAlphanumericReferralCode_HasError(string referralCode)
    {
        _sut.ShouldHaveValidationErrorFor(x => x.ReferralCode, Request(referralCode: referralCode));
    }

    [Fact]
    public void Validate_AlphanumericReferralCode_NoError()
    {
        _sut.ShouldNotHaveValidationErrorFor(x => x.ReferralCode, Request(referralCode: "A1B2C3d4"));
    }
}
