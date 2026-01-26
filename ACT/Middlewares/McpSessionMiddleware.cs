using ACT.Services;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace ACT.Middlewares
{
    public class McpSessionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMcpSessionService _sessionService;

        public McpSessionMiddleware(RequestDelegate next, IMcpSessionService sessionService)
        {
            _next = next;
            _sessionService = sessionService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only track sessions for MCP endpoints
            if (!context.Request.Path.StartsWithSegments("/mcp"))
            {
                await _next(context);
                return;
            }

            // 1. Try to get Session ID from Header
            string sessionId = context.Request.Headers["Mcp-Session-Id"].FirstOrDefault();

            // 2. Try Query Parameter
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = context.Request.Query["sessionId"].FirstOrDefault();
            }

            // 3. Fallback to Connection ID
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = context.Connection.Id;
            }

            // Store in Items for easy access
            context.Items["McpSessionId"] = sessionId;

            // Track presence
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            _sessionService.TouchSession(sessionId, userAgent);

            await _next(context);
        }
    }
}
