using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.Reports;

public record GenerateReportQuery(DateTime From, DateTime To) : IRequest<GenerateReportResult>;

public record GenerateReportResult(bool Success, byte[]? PdfBytes, string? FileName, string? ErrorMessage);

public class GenerateReportHandler : IRequestHandler<GenerateReportQuery, GenerateReportResult>
{
    private readonly ReportService _reportService;
    private readonly ILogger<GenerateReportHandler> _logger;

    public GenerateReportHandler(ReportService reportService, ILogger<GenerateReportHandler> logger)
    {
        _reportService = reportService;
        _logger = logger;
    }

    public async Task<GenerateReportResult> Handle(GenerateReportQuery request, CancellationToken ct)
    {
        if (request.From > request.To)
            return new GenerateReportResult(false, null, null, "'from' date must be before or equal to 'to' date.");

        if ((request.To - request.From).TotalDays > 90)
            return new GenerateReportResult(false, null, null, "Maximum report period is 90 days.");

        try
        {
            _logger.LogInformation("Generating PDF report from {From} to {To}.",
                request.From.ToString("yyyy-MM-dd"), request.To.ToString("yyyy-MM-dd"));

            var pdfBytes = await _reportService.GenerateReportAsync(request.From, request.To, ct);
            var fileName = $"glucose_report_{request.From:yyyyMMdd}_{request.To:yyyyMMdd}.pdf";

            return new GenerateReportResult(true, pdfBytes, fileName, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate PDF report.");
            return new GenerateReportResult(false, null, null, $"Failed to generate report: {ex.Message}");
        }
    }
}
