using FluentValidation.TestHelper;
using Identity.API.Validators;

namespace Identity.API.Tests.Validators;

/// <summary>
/// Covers every rule on the three auth validators. Validators are the first line
/// of defense against malformed input reaching the handlers, and they are the
/// easiest place to introduce regressions — a typo in <c>RuleFor</c> silently
/// disables a check. These tests pin each rule individually so any weakening is
/// caught at build time.
/// </summary>
public sealed class AuthValidatorsTests
{
    // -------- LoginRequestValidator --------

    /// <summary>
    /// Happy path: a syntactically valid email and a non-empty password pass
    /// every rule. This is the contract the login flow depends on — if this
    /// fails, no caller can ever log in.
    /// </summary>
    [Fact]
    public void LoginRequest_WithValidPair_IsValid()
    {
        var validator = new LoginRequestValidator();
        var result = validator.TestValidate(new LoginRequest("user@test.com", "p@ssword1"));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Empty email → <c>NotEmpty</c> rule fires. Without this rule, the request
    /// would proceed to <c>UserManager.FindByEmailAsync("")</c> and waste a DB
    /// round-trip on a known-bad input.
    /// </summary>
    [Fact]
    public void LoginRequest_WithEmptyEmail_HasNotEmptyError()
    {
        var validator = new LoginRequestValidator();
        var result = validator.TestValidate(new LoginRequest(string.Empty, "p@ssword1"));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    /// <summary>
    /// Malformed email → <c>EmailAddress</c> rule fires. Two flavors are checked:
    /// a single token (<c>"abc"</c>) and an at-sign without a domain
    /// (<c>"abc@"</c>). Both should fail; if either passes, downstream token
    /// issuance could throw an unhandled exception.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("abc@")]
    [InlineData("@missing-local.com")]
    public void LoginRequest_WithMalformedEmail_HasEmailAddressError(string email)
    {
        var validator = new LoginRequestValidator();
        var result = validator.TestValidate(new LoginRequest(email, "p@ssword1"));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    /// <summary>
    /// Empty password → <c>NotEmpty</c> rule fires. The login flow already
    /// checks the password against the hash, but rejecting the empty case at
    /// the validator boundary saves a password-hash computation per request.
    /// </summary>
    [Fact]
    public void LoginRequest_WithEmptyPassword_HasNotEmptyError()
    {
        var validator = new LoginRequestValidator();
        var result = validator.TestValidate(new LoginRequest("user@test.com", string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    // -------- RegisterRequestValidator --------

    /// <summary>
    /// Happy path: every required field populated, email valid, password meets
    /// the 8-character minimum, and an optional phone number within the
    /// 20-character limit. Locks in the contract that the seeder / register
    /// endpoint accepts a typical user payload.
    /// </summary>
    [Fact]
    public void RegisterRequest_WithValidPayload_IsValid()
    {
        var validator = new RegisterRequestValidator();
        var result = validator.TestValidate(new RegisterRequest(
            "new@test.com", "p@ssword1", "Jane", "Doe", "+15551234567"));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Email over 256 characters → <c>MaximumLength</c> rule fires. The DB
    /// column is sized to 256 (default ASP.NET Identity); a longer value would
    /// either be truncated silently or throw at SaveChanges — neither is
    /// acceptable behavior for a public endpoint.
    /// </summary>
    [Fact]
    public void RegisterRequest_WithTooLongEmail_HasMaxLengthError()
    {
        var validator = new RegisterRequestValidator();
        var longEmail = new string('a', 250) + "@test.com"; // 259 chars total
        var result = validator.TestValidate(new RegisterRequest(
            longEmail, "p@ssword1", "Jane", "Doe", null));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    /// <summary>
    /// Password under 8 characters → <c>MinimumLength</c> rule fires. The
    /// UserManager password policy also enforces 8 chars, but the validator
    /// must catch it first so the user gets a 400 instead of an Identity error.
    /// </summary>
    [Fact]
    public void RegisterRequest_WithShortPassword_HasMinLengthError()
    {
        var validator = new RegisterRequestValidator();
        var result = validator.TestValidate(new RegisterRequest(
            "new@test.com", "short", "Jane", "Doe", null));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    /// <summary>
    /// Missing first or last name → <c>NotEmpty</c> rule fires. These fields are
    /// surfaced on the audit trail and on the profile view; an empty value would
    /// render as a blank in the UI and break name search in the admin listing.
    /// </summary>
    [Fact]
    public void RegisterRequest_WithMissingFirstName_HasNotEmptyError()
    {
        var validator = new RegisterRequestValidator();
        var result = validator.TestValidate(new RegisterRequest(
            "new@test.com", "p@ssword1", string.Empty, "Doe", null));
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    /// <summary>
    /// Phone number over 20 chars → <c>MaximumLength</c> rule fires, but only
    /// when the value is non-null (the <c>When</c> clause). A null phone number
    /// must always be valid; an over-long non-null phone number must always be
    /// rejected.
    /// </summary>
    [Fact]
    public void RegisterRequest_WithTooLongPhoneNumber_HasMaxLengthError()
    {
        var validator = new RegisterRequestValidator();
        var result = validator.TestValidate(new RegisterRequest(
            "new@test.com", "p@ssword1", "Jane", "Doe", new string('5', 21)));
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    /// <summary>
    /// <c>PhoneNumber = null</c> must bypass the length check entirely. Without
    /// this contract, every caller who legitimately doesn't supply a phone
    /// number would receive a spurious validation error.
    /// </summary>
    [Fact]
    public void RegisterRequest_WithNullPhoneNumber_IsValid()
    {
        var validator = new RegisterRequestValidator();
        var result = validator.TestValidate(new RegisterRequest(
            "new@test.com", "p@ssword1", "Jane", "Doe", null));
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
        result.IsValid.Should().BeTrue();
    }

    // -------- RefreshTokenRequestValidator --------

    /// <summary>
    /// Non-empty token → valid. Refresh-token validation is trivial by design;
    /// the real check happens in OpenIddict. The validator only enforces that
    /// a token was actually sent.
    /// </summary>
    [Fact]
    public void RefreshTokenRequest_WithNonEmptyToken_IsValid()
    {
        var validator = new RefreshTokenRequestValidator();
        var result = validator.TestValidate(new RefreshTokenRequest("some-jwt"));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Empty token → <c>NotEmpty</c> rule fires. The token endpoint would
    /// reject this anyway, but a 400 from the validator gives a clearer error
    /// to the caller than the OAuth2 <c>invalid_request</c> response shape.
    /// </summary>
    [Fact]
    public void RefreshTokenRequest_WithEmptyToken_HasNotEmptyError()
    {
        var validator = new RefreshTokenRequestValidator();
        var result = validator.TestValidate(new RefreshTokenRequest(string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.RefreshToken);
    }

    /// <summary>
    /// Whitespace-only token → <c>NotEmpty</c> rule fires. <c>NotEmpty</c> on
    /// FluentValidation 12 rejects whitespace; this guards against callers
    /// sending tokens from a stubbed storage that returned a single space.
    /// </summary>
    [Fact]
    public void RefreshTokenRequest_WithWhitespaceToken_HasNotEmptyError()
    {
        var validator = new RefreshTokenRequestValidator();
        var result = validator.TestValidate(new RefreshTokenRequest("   "));
        result.ShouldHaveValidationErrorFor(x => x.RefreshToken);
    }
}