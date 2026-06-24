using Application.Features.Waitlist.DTOs;
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

        RuleFor(x => x.CompanyName)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.CompanyName))
            .WithMessage("Company name must not exceed 200 characters.");

        RuleFor(x => x.Comments)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrWhiteSpace(x.Comments))
            .WithMessage("Comments must not exceed 2000 characters.");
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
