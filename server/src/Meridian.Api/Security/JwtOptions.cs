namespace Meridian.Api.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string DevFallbackKey = "meridian-dev-only-signing-key-change-me-0123456789";

    public string Key { get; init; } = DevFallbackKey;
    public string Issuer { get; init; } = "meridian";
    public string Audience { get; init; } = "meridian";
    public int ExpiryHours { get; init; } = 8;
}
