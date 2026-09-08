using Meridian.Api.Contracts;
using Meridian.Api.Filters;
using Meridian.Api.Security;
using Meridian.Application.Common;
using Meridian.Application.Dtos;
using Meridian.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;

    public AccountsController(IAccountService accountService) => _accountService = accountService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _accountService.ListAsync(User.GetUserId(), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AccountDto>> Create(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await _accountService.CreateAsync(
            User.GetUserId(), request.Name, request.Currency, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await _accountService.GetAsync(User.GetUserId(), id, cancellationToken));

    [HttpGet("{id:guid}/entries")]
    public async Task<ActionResult<PagedResult<LedgerEntryDto>>> GetEntries(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await _accountService.GetEntriesAsync(User.GetUserId(), id, page, pageSize, cancellationToken));

    [HttpPost("{id:guid}/deposit")]
    [Idempotent]
    [ProducesResponseType(typeof(TransferDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TransferDto>> Deposit(
        Guid id, DepositRequest request, CancellationToken cancellationToken)
    {
        var transfer = await _accountService.DepositAsync(
            User.GetUserId(), id, request.Amount, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, transfer);
    }
}
