using StaySphere.Application.Ai;
using StaySphere.Application.Common;
using StaySphere.Contracts.Auth;
using StaySphere.Contracts.Payments;

namespace StaySphere.UnitTests.Application;

public sealed class ApplicationTests
{
    [Theory]
    [InlineData("tok_4242", true)]
    [InlineData("4242424242424242", false)]
    [InlineData("4242 4242 4242 4242", false)]
    [InlineData("", false)]
    public void Payment_requests_only_accept_provider_tokens_never_card_numbers(string token, bool valid) =>
        new PayReservationRequestValidator().Validate(new PayReservationRequest(token)).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData("short", false)]
    [InlineData("alllowercase123", false)]
    [InlineData("Str0ngPassword!", true)]
    public void Registration_enforces_password_strength(string password, bool valid) =>
        new RegisterRequestValidator().Validate(new RegisterRequest("a@b.co", password, "Alex")).IsValid.ShouldBe(valid);

    [Fact]
    public void Assistant_redacts_pii_before_text_reaches_a_model()
    {
        var redacted = AssistantService.RedactPii("Email me at jane.doe@example.com, card 4111 1111 1111 1111, phone +44 20 7946 0958");

        redacted.ShouldNotContain("jane.doe@example.com");
        redacted.ShouldNotContain("4111");
        redacted.ShouldNotContain("7946");
        redacted.ShouldContain("[email]");
        redacted.ShouldContain("[card]");
    }

    [Theory]
    [InlineData("Book it from 2030-05-01 to 2030-05-04 for 2 guests")]
    [InlineData("dates 2030-05-01 2030-05-04")]
    public void Assistant_redaction_keeps_iso_dates(string message) => AssistantService.RedactPii(message).ShouldBe(message);

    [Fact]
    public void Token_hashing_is_deterministic_and_tokens_are_random()
    {
        var a = TokenHasher.GenerateToken();
        var b = TokenHasher.GenerateToken();

        a.ShouldNotBe(b);
        TokenHasher.Hash(a).ShouldBe(TokenHasher.Hash(a));
        TokenHasher.Hash(a).Length.ShouldBe(64);
    }
}
