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
[Route("api/transfers")]
public sealed class TransfersController : ControllerBase
{
    private readonly ITransferService _transferService;

    public TransfersController(ITransferService transferService) => _transferService = transferService;

    [HttpPost]
    [Idempotent]
    [ProducesResponseType(typeof(TransferDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TransferDto>> Create(
        CreateTransferRequest request, CancellationToken cancellationToken)
    {
        var transfer = await _transferService.CreateAsync(
            User.GetUserId(),
            request.SourceAccountId,
            request.DestinationAccountId,
            request.Amount,
            request.Description,
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = transfer.Id }, transfer);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransferDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await _transferService.GetAsync(User.GetUserId(), id, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedResult<TransferDto>>> List(
        [FromQuery] Guid? accountId, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await _transferService.ListAsync(User.GetUserId(), accountId, page, pageSize, cancellationToken));
}
