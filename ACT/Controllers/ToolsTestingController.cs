using ACT.Tools;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Controllers;

[ApiController]
[Route("api/tools-testing")]
public class ToolsTestingController : ControllerBase
{
    private readonly ActTool _actTool;

    public ToolsTestingController(ActTool actTool)
    {
        _actTool = actTool;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeConversation([FromBody] string conversationText, CancellationToken ct)
    {
        var result = await _actTool.AnalyzeConversationAsync(conversationText, ct);
        return Ok(result);
    }

    [HttpGet("calculate")]
    public async Task<IActionResult> CalculateInteraction(string actor, string behavior, string obj, CancellationToken ct)
    {
        var result = await _actTool.CalculateInteractionAsync(actor, behavior, obj, ct);
        return Ok(result);
    }
    
    [HttpGet("list-dictionaries")]
    public async Task<IActionResult> ListDictionaries(CancellationToken ct)
    {
        var result = await _actTool.ListDictionariesAsync(ct);
        return Ok(result);
    }
}
