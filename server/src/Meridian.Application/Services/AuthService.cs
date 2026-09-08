using Meridian.Application.Abstractions;
using Meridian.Application.Common;
using Meridian.Application.Dtos;
using Meridian.Application.Exceptions;
using Meridian.Domain.Entities;

namespace Meridian.Application.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
}

public sealed class AuthService : IAuthService
{
    public const string DefaultCurrency = "BRL";
    public const decimal OpeningBalance = 1000.00m;

    private readonly IUserRepository _users;
    private readonly IAccountRepository _accounts;
    private readonly ITransferRepository _transfers;
    private readonly ILedgerEntryRepository _ledger;
    private readonly IOutboxRepository _outbox;
    private readonly ISystemAccountProvider _systemAccounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;

    public AuthService(
        IUserRepository users,
        IAccountRepository accounts,
        ITransferRepository transfers,
        ILedgerEntryRepository ledger,
        IOutboxRepository outbox,
        ISystemAccountProvider systemAccounts,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IClock clock)
    {
        _users = users;
        _accounts = accounts;
        _transfers = transfers;
        _ledger = ledger;
        _outbox = outbox;
        _systemAccounts = systemAccounts;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _clock = clock;
    }

    public Task<AuthResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        return ConcurrencyRetry.ExecuteAsync(
            async () =>
            {
                if (await _users.GetByEmailAsync(email, cancellationToken) is not null)
                    throw new ConflictException("Email is already registered.");

                var systemAccount = await _systemAccounts.GetOrCreateAsync(DefaultCurrency, cancellationToken);

                var now = _clock.UtcNow;
                var user = User.Create(email, _passwordHasher.Hash(password), now);
                _users.Add(user);

                var mainAccount = Account.CreateForUser(user.Id, "Main", DefaultCurrency, now);
                _accounts.Add(mainAccount);

                var result = Domain.Entities.Transfer.Execute(
                    systemAccount, mainAccount, OpeningBalance, "Opening balance", now);

                _transfers.Add(result.Transfer);
                _ledger.AddRange(new[] { result.DebitEntry, result.CreditEntry });
                OutboxWriter.Enqueue(_outbox, result.Event);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return new AuthResult(_tokenService.CreateToken(user));
            },
            onConflict: _unitOfWork.ClearTracking,
            maxAttempts: SystemAccountProvider.ContentionMaxAttempts);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(email, cancellationToken)
            ?? throw new InvalidCredentialsException();

        if (!_passwordHasher.Verify(user.PasswordHash, password))
            throw new InvalidCredentialsException();

        return new AuthResult(_tokenService.CreateToken(user));
    }
}
