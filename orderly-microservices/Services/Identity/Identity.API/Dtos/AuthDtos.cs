namespace Identity.API.Dtos;

public readonly record struct LoginRequest(string Email, string Password);

public readonly record struct RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber = null);

public readonly record struct RefreshTokenRequest(string RefreshToken);

public readonly record struct TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public readonly record struct RegisterResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName);
