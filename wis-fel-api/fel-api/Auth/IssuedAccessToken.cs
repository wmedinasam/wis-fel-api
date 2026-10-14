namespace fel_api.Auth;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAtUtc);