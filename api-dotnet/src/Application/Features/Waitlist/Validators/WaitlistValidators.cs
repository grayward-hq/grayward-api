using Application.Features.Waitlist.DTOs;
using Application.Features.Waitlist.Commands;
using FluentValidation;

namespace Application.Features.Waitlist.Validators;

public class JoinWaitlistValidator : AbstractValidator<JoinWaitlistRequest>
{
    public JoinWaitlistValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress()
            .WithMessage("Invalid email format.");

        // Free-text fields are stored and later shown in the admin UI, so reject payloads that carry
        // markup/control characters at the boundary as defense-in-depth against stored XSS. (Output
        // encoding at the render site remains the primary defense — this just narrows the input.)
        RuleFor(x => x.CompanyName)
            .MaximumLength(200)
            .WithMessage("Company name must not exceed 200 characters.")
            .Must(HasNoAngleBrackets)
            .WithMessage("Company name must not contain '<' or '>'.")
            .Must(HasNoControlCharacters)
            .WithMessage("Company name contains invalid control characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.CompanyName));

        RuleFor(x => x.Comments)
            .MaximumLength(2000)
            .WithMessage("Comments must not exceed 2000 characters.")
            .Must(HasNoControlCharacters)
            .WithMessage("Comments contain invalid control characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Comments));

        // A referral code we issue is alphanumeric; constraining the inbound value to the same shape
        // rejects anything that could be an injection payload rather than a real code.
        RuleFor(x => x.ReferralCode)
            .MaximumLength(32)
            .WithMessage("Referral code must not exceed 32 characters.")
            .Matches("^[A-Za-z0-9]+$")
            .WithMessage("Referral code must be alphanumeric.")
            .When(x => !string.IsNullOrWhiteSpace(x.ReferralCode));
    }

    private static bool HasNoAngleBrackets(string? value)
        => value is null || (!value.Contains('<') && !value.Contains('>'));

    // Rejects control characters except the ordinary whitespace that free text may legitimately use.
    private static bool HasNoControlCharacters(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        foreach (var c in value)
        {
            if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
                return false;
        }

        return true;
    }
}

public class VerifyWaitlistEmailValidator : AbstractValidator<VerifyWaitlistEmailRequest>
{
    public VerifyWaitlistEmailValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress()
            .WithMessage("Invalid email format.");

        RuleFor(x => x.Token)
            .NotEmpty()
            .MinimumLength(32)
            .WithMessage("Invalid verification token.");
    }
}

public class CancelWaitlistValidator : AbstractValidator<CancelWaitlistRequest>
{
    public CancelWaitlistValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress()
            .WithMessage("Invalid email format.");

        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("Cancellation token is required.");
    }
}

public class RequestWaitlistCancellationValidator : AbstractValidator<RequestWaitlistCancellationCommand>
{
    public RequestWaitlistCancellationValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress()
            .WithMessage("Invalid email format.");
    }
}
