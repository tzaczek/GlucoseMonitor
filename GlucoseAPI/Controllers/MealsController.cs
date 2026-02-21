using GlucoseAPI.Application.Features.Meals;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MealsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MealsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetMeals(
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool desc = true,
        [FromQuery] string? classification = null,
        [FromQuery] int? limit = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetMealsQuery(search, sortBy, desc, classification, limit), ct);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMealStatsQuery(), ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetMeal(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMealDetailQuery(id), ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("compare")]
    public async Task<ActionResult> Compare([FromBody] CompareMealsRequest request, CancellationToken ct)
    {
        if (request.Ids == null || request.Ids.Count < 2)
            return BadRequest(new { message = "Select at least 2 meals to compare." });

        if (request.Ids.Count > 10)
            return BadRequest(new { message = "Maximum 10 meals can be compared." });

        var result = await _mediator.Send(new CompareMealsQuery(request.Ids), ct);
        return Ok(result);
    }
}

public record CompareMealsRequest(List<int> Ids);
