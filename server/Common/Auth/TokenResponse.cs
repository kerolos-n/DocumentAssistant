namespace DocumentAssistant.Common.Auth;

public sealed record TokenResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string UserId,
    string Email);