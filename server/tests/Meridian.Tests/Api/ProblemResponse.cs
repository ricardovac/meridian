namespace Meridian.Tests.Api;

public sealed record ProblemResponse(string? Type, string? Title, int? Status, string? Detail);
