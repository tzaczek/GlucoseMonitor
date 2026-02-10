using GlucoseAPI.Application.Features.Comparisons;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComparisonController : ControllerBase
{
    private readonly IMediator _mediator;
    public ComparisonController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetComparisons([FromQuery] int? limit = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetComparisonsQuery(limit), ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetComparison(int id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetComparisonDetailQuery(id), ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult> CreateComparison([FromBody] CreateComparisonRequest request, CancellationToken ct = default)
    {
        var command = new CreateComparisonCommand(
            request.Name,
            request.PeriodAStart,
            request.PeriodAEnd,
            request.PeriodALabel,
            request.PeriodBStart,
            request.PeriodBEnd,
            request.PeriodBLabel
        );
        var result = await _mediator.Send(command, ct);
        if (!result.Success) return BadRequest(new { result.Message });
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteComparison(int id, CancellationToken ct = default)
    {
        var deleted = await _mediator.Send(new DeleteComparisonCommand(id), ct);
        if (!deleted) return NotFound();
        return Ok(new { message = "Comparison deleted." });
    }
}

public record CreateComparisonRequest(
    string? Name,
    DateTime PeriodAStart,
    DateTime PeriodAEnd,
    string? PeriodALabel,
    DateTime PeriodBStart,
    DateTime PeriodBEnd,
    string? PeriodBLabel
);
