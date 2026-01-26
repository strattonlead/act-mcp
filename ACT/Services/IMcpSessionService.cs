using System;
using System.Collections.Generic;

namespace ACT.Services
{
    public record McpSessionInfo(
        string SessionId,
        DateTime LastSeen,
        string ClientInfo,
        int RequestCount
    );

    public interface IMcpSessionService
    {
        void TouchSession(string sessionId, string clientInfo = null);
        IEnumerable<McpSessionInfo> GetActiveSessions();
    }
}
