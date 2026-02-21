using GlucoseAPI.Application.Features.Food;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/food")]
public class FoodController : ControllerBase
{
    private readonly IMediator _mediator;

    public FoodController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetFoodItems(
        [FromQuery] string? search, [FromQuery] string? sortBy, [FromQuery] bool desc = true)
    {
        var result = await _mediator.Send(new GetFoodItemsQuery(search, sortBy, desc));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetFoodItem(int id)
    {
        var result = await _mediator.Send(new GetFoodItemDetailQuery(id));
        return result != null ? Ok(result) : NotFound();
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetFoodStats()
    {
        var result = await _mediator.Send(new GetFoodStatsQuery());
        return Ok(result);
    }

    [HttpPost("scan")]
    public async Task<IActionResult> TriggerScan()
    {
        var result = await _mediator.Send(new TriggerFoodScanCommand());
        return Ok(new { message = result });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteFoodItem(int id)
    {
        var result = await _mediator.Send(new DeleteFoodItemCommand(id));
        return result ? NoContent() : NotFound();
    }

    [HttpPut("{id:int}/rename")]
    public async Task<IActionResult> RenameFoodItem(int id, [FromBody] RenameRequest request)
    {
        var result = await _mediator.Send(new RenameFoodItemCommand(id, request.Name));
        return result ? Ok() : NotFound();
    }

    [HttpPost("merge")]
    public async Task<IActionResult> MergeFoodItems([FromBody] MergeRequest request)
    {
        var result = await _mediator.Send(new MergeFoodItemsCommand(request.TargetId, request.SourceIds));
        return result ? Ok() : NotFound();
    }

    public record RenameRequest(string Name);
    public record MergeRequest(int TargetId, List<int> SourceIds);
}
