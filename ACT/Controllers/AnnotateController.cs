using ACT.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Controllers;

[ApiController]
[Route("annotate")]
public class AnnotateController : ControllerBase
{
    private readonly IAnnotateService _annotateService;
    private readonly ILogger<AnnotateController> _logger;

    public AnnotateController(IAnnotateService annotateService, ILogger<AnnotateController> logger)
    {
        _annotateService = annotateService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<AnnotateResponseDto>> Post([FromBody] AnnotateRequestDto request, CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request body required" });
        }
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "text required" });
        }
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return BadRequest(new { error = "runId required" });
        }

        try
        {
            var result = await _annotateService.AnnotateAsync(request.Text, request.RunId, request.Phase, ct);
            return Ok(result);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Annotate failed for runId={RunId} phase={Phase}", request.RunId, request.Phase);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class AnnotateRequestDto
{
    [Required]
    public string Text { get; set; } = string.Empty;

    [Required]
    public string RunId { get; set; } = string.Empty;

    public int Phase { get; set; }
}
