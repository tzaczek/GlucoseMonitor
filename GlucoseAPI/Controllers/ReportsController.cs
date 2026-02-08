using GlucoseAPI.Application.Features.Reports;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("pdf")]
    public async Task<IActionResult> GeneratePdf(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct)
    {
        if (!DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
            return BadRequest(new { message = "Invalid date format. Use yyyy-MM-dd." });

        var result = await _mediator.Send(new GenerateReportQuery(fromDate, toDate), ct);

        if (!result.Success)
            return result.ErrorMessage!.Contains("before or equal") || result.ErrorMessage.Contains("Maximum")
                ? BadRequest(new { message = result.ErrorMessage })
                : StatusCode(500, new { message = result.ErrorMessage });

        return File(result.PdfBytes!, "application/pdf", result.FileName);
    }
}
