using System.ComponentModel.DataAnnotations;

namespace Meridian.Api.Contracts;

public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password);

public sealed record LoginRequest(
    [Required] string Email,
    [Required] string Password);

public sealed record CreateAccountRequest(
    [Required, MaxLength(100)] string Name,
    [Required, StringLength(3, MinimumLength = 3)] string Currency);

public sealed record DepositRequest(
    [Range(0.01, 1_000_000_000)] decimal Amount);

public sealed record CreateTransferRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    [Range(0.01, 1_000_000_000)] decimal Amount,
    [MaxLength(500)] string? Description);
