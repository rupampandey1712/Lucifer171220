using FluentValidation;
using StaySphere.Contracts.Admin;
using StaySphere.Contracts.Ai;
using StaySphere.Contracts.Auth;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Engagement;
using StaySphere.Contracts.Payments;
using StaySphere.Contracts.Properties;
using StaySphere.Contracts.Reviews;
using StaySphere.Contracts.Support;

namespace StaySphere.Application.Common;

internal static class Rules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(10).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");

    public static IRuleBuilderOptions<T, string> Currency<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Length(3).Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter ISO code.");
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword().NotEqual(x => x.CurrentPassword).WithMessage("Choose a different password.");
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Bio).MaximumLength(1000);
        RuleFor(x => x.PreferredCurrency).Currency();
    }
}

public sealed class CreatePropertyRequestValidator : AbstractValidator<CreatePropertyRequest>
{
    public CreatePropertyRequestValidator()
    {
        RuleFor(x => x.PropertyType).NotEmpty().IsEnumName(typeof(Domain.Catalog.PropertyType), caseSensitive: false);
        RuleFor(x => x.RoomType).NotEmpty().IsEnumName(typeof(Domain.Catalog.RoomType), caseSensitive: false);
        RuleFor(x => x.Title).MaximumLength(120);
    }
}

public sealed class UpdatePropertyRequestValidator : AbstractValidator<UpdatePropertyRequest>
{
    public UpdatePropertyRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.PropertyType).IsEnumName(typeof(Domain.Catalog.PropertyType), caseSensitive: false).When(x => x.PropertyType is not null);
        RuleFor(x => x.RoomType).IsEnumName(typeof(Domain.Catalog.RoomType), caseSensitive: false).When(x => x.RoomType is not null);
        RuleFor(x => x.MaxGuests).InclusiveBetween(1, 50).When(x => x.MaxGuests is not null);
        RuleFor(x => x.Bedrooms).InclusiveBetween(0, 50).When(x => x.Bedrooms is not null);
        RuleFor(x => x.Beds).InclusiveBetween(1, 100).When(x => x.Beds is not null);
        RuleFor(x => x.Bathrooms).InclusiveBetween(0, 50).When(x => x.Bathrooms is not null);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x.Address!.City).NotEmpty().MaximumLength(100).When(x => x.Address is not null);
        RuleFor(x => x.Address!.CountryCode).Length(2).When(x => x.Address is not null);
        RuleFor(x => x.Address!.Line1).NotEmpty().MaximumLength(200).When(x => x.Address is not null);
        RuleFor(x => x.Amenities).Must(a => a!.Count <= 60).When(x => x.Amenities is not null);
        When(x => x.Pricing is not null, () =>
        {
            RuleFor(x => x.Pricing!.BasePrice).GreaterThan(0).LessThanOrEqualTo(100_000);
            RuleFor(x => x.Pricing!.CleaningFee).GreaterThanOrEqualTo(0).LessThanOrEqualTo(10_000);
            RuleFor(x => x.Pricing!.Currency).Currency();
        });
        When(x => x.Rules is not null, () =>
        {
            RuleFor(x => x.Rules!.HouseRules).MaximumLength(2000);
            RuleFor(x => x.Rules!.CheckInTime).Matches(@"^\d{2}:\d{2}$");
            RuleFor(x => x.Rules!.CheckOutTime).Matches(@"^\d{2}:\d{2}$");
            RuleFor(x => x.Rules!.CancellationPolicy).IsEnumName(typeof(Domain.Catalog.CancellationPolicy), caseSensitive: false);
        });
    }
}

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn).WithMessage("Check-out must be after check-in.");
        RuleFor(x => x.Guests).InclusiveBetween(1, 50);
        RuleFor(x => x.CouponCode).MaximumLength(40);
    }
}

public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn).WithMessage("Check-out must be after check-in.");
        RuleFor(x => x.Guests).InclusiveBetween(1, 50);
        RuleFor(x => x.QuoteToken).NotEmpty().MaximumLength(2000);
    }
}

public sealed class BlockDatesRequestValidator : AbstractValidator<BlockDatesRequest>
{
    public BlockDatesRequestValidator()
    {
        RuleFor(x => x.Block).Must(b => b.Count <= 366);
        RuleFor(x => x.Unblock).Must(b => b.Count <= 366);
    }
}

public sealed class SeasonalPriceRequestValidator : AbstractValidator<SeasonalPriceRequest>
{
    public SeasonalPriceRequestValidator()
    {
        RuleFor(x => x.End).GreaterThan(x => x.Start);
        RuleFor(x => x.NightlyPrice).GreaterThan(0).LessThanOrEqualTo(100_000);
    }
}

public sealed class PayReservationRequestValidator : AbstractValidator<PayReservationRequest>
{
    public PayReservationRequestValidator()
    {
        // Only provider tokens are accepted. Anything that looks like a raw card number is rejected outright.
        RuleFor(x => x.PaymentMethodToken).NotEmpty().MaximumLength(100).Matches("^tok_[A-Za-z0-9_]+$")
            .WithMessage("Provide a payment token (e.g. tok_4242), never a card number.");
    }
}

public sealed class PayoutAccountRequestValidator : AbstractValidator<PayoutAccountRequest>
{
    public PayoutAccountRequestValidator()
    {
        RuleFor(x => x.AccountHolder).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Iban).NotEmpty().MaximumLength(42);
        RuleFor(x => x.Country).NotEmpty().Length(2);
    }
}

public sealed class DeclineRequestValidator : AbstractValidator<DeclineRequest>
{
    public DeclineRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
{
    public CreateReviewRequestValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.Overall).InclusiveBetween(1, 5);
        RuleFor(x => x.Cleanliness).InclusiveBetween(1, 5);
        RuleFor(x => x.Accuracy).InclusiveBetween(1, 5);
        RuleFor(x => x.Communication).InclusiveBetween(1, 5);
        RuleFor(x => x.Location).InclusiveBetween(1, 5);
        RuleFor(x => x.CheckIn).InclusiveBetween(1, 5);
        RuleFor(x => x.Value).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).NotEmpty().MinimumLength(10).MaximumLength(2000);
    }
}

public sealed class ReviewResponseRequestValidator : AbstractValidator<ReviewResponseRequest>
{
    public ReviewResponseRequestValidator() => RuleFor(x => x.Response).NotEmpty().MaximumLength(1000);
}

public sealed class StartConversationRequestValidator : AbstractValidator<StartConversationRequest>
{
    public StartConversationRequestValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
    }
}

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
}

public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
    }
}

public sealed class CreateReportRequestValidator : AbstractValidator<CreateReportRequest>
{
    public CreateReportRequestValidator()
    {
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CreateCouponRequestValidator : AbstractValidator<CreateCouponRequest>
{
    public CreateCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.PercentOff).InclusiveBetween(0, 100);
        RuleFor(x => x.MaxRedemptions).GreaterThan(0);
    }
}

public sealed class AssistantMessageRequestValidator : AbstractValidator<AssistantMessageRequest>
{
    public AssistantMessageRequestValidator() => RuleFor(x => x.Message).NotEmpty().MaximumLength(1000);
}
