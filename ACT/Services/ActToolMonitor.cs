using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ACT.Services
{
    public record ToolExecutionLog(
        string ToolName,
        string Arguments,
        string Result,
        bool IsSuccess,
        DateTime Timestamp,
        string SessionId,
        string ErrorMessage = null
    );

    public interface IActToolMonitor
    {
        event Action OnExecution;
        void RecordCall(string sessionId, string toolName, string arguments, string result, bool isSuccess, string errorMessage = null);
        IEnumerable<ToolExecutionLog> GetRecentLogs();
    }

    public class ActToolMonitor : IActToolMonitor
    {
        private readonly ConcurrentQueue<ToolExecutionLog> _logs = new();
        private const int MaxLogs = 50;

        public event Action OnExecution;

        public void RecordCall(string sessionId, string toolName, string arguments, string result, bool isSuccess, string errorMessage = null)
        {
            var log = new ToolExecutionLog(
                ToolName: toolName,
                Arguments: arguments,
                Result: result,
                IsSuccess: isSuccess,
                Timestamp: DateTime.Now,
                SessionId: sessionId,
                ErrorMessage: errorMessage
            );

            _logs.Enqueue(log);

            while (_logs.Count > MaxLogs)
            {
                _logs.TryDequeue(out _);
            }

            OnExecution?.Invoke();
        }

        public IEnumerable<ToolExecutionLog> GetRecentLogs()
        {
            return _logs.ToList().OrderByDescending(x => x.Timestamp);
        }
    }
}
