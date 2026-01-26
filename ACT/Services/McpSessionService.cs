using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ACT.Services
{
    public class McpSessionService : IMcpSessionService
    {
        private class SessionState
        {
            public string SessionId { get; set; }
            public DateTime LastSeen { get; set; }
            public string ClientInfo { get; set; }
            public int RequestCount { get; set; }
        }

        private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
        
        public void TouchSession(string sessionId, string clientInfo = null)
        {
            _sessions.AddOrUpdate(sessionId, 
                id => new SessionState 
                { 
                    SessionId = id, 
                    LastSeen = DateTime.Now, 
                    ClientInfo = clientInfo, 
                    RequestCount = 1 
                },
                (id, state) => 
                {
                    state.LastSeen = DateTime.Now;
                    state.RequestCount++;
                    if (!string.IsNullOrEmpty(clientInfo))
                    {
                        state.ClientInfo = clientInfo;
                    }
                    return state;
                });

            CleanupStaleSessions();
        }

        public IEnumerable<McpSessionInfo> GetActiveSessions()
        {
            return _sessions.Values
                .OrderByDescending(s => s.LastSeen)
                .Select(s => new McpSessionInfo(s.SessionId, s.LastSeen, s.ClientInfo, s.RequestCount));
        }

        private void CleanupStaleSessions()
        {
            // Simple cleanup: remove sessions older than 15 minutes
            // In a high traffic scenario, this should be a background task, 
            // but for this scale, checking occasionally is fine.
            // To avoid checking on every request, we could check only if count > N or throttle it.
            // For now, let's just do a quick check if collection is large or just randomly?
            // Let's just do it. It's concurrent dictionary, so safe.
            
            var threshold = DateTime.Now.AddMinutes(-15);
            foreach (var kvp in _sessions)
            {
                if (kvp.Value.LastSeen < threshold)
                {
                    _sessions.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
