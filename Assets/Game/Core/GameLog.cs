using System;
using System.IO;
using System.Text.RegularExpressions;
using Game.Contracts;

namespace Game.Core
{
    [Serializable]
    public sealed class LogRecord
    {
        public string timestamp, level, module, operation, correlationId, message, exception, clientVersion, codeVersion;
        public long durationMs;
    }
    public sealed class GameLog : IGameLog
    {
        readonly object gate = new object();
        readonly string path, client;
        readonly IJsonCodec json;
        readonly Action<string> console;
        readonly long maxBytes;
        public GameLogLevel MinimumLevel { get; set; }
        public string CodeVersion { get; set; } = "bootstrap";
        public string LastWriteError { get; private set; }
        static readonly Regex Secrets = new Regex(@"(?i)(password|token|secret|authorization)(\s*[:=]\s*)([^\s,;]+)");
        static readonly Regex Bearer = new Regex(@"(?i)\bBearer\s+[^\s,;]+");
        public GameLog(string path, string client, IJsonCodec json, Action<string> console, long maxBytes = 1024 * 1024)
        { this.path = path; this.client = client; this.json = json; this.console = console; this.maxBytes = maxBytes; }
        public void Write(GameLogLevel level, string module, string operation, string message,
            string correlationId = "", long durationMs = 0, Exception exception = null)
        {
            if (level < MinimumLevel) return;
            string Clean(string value) => Secrets.Replace(Bearer.Replace(value ?? "", "Bearer [REDACTED]"), "$1$2[REDACTED]");
            var row = new LogRecord { timestamp = DateTime.UtcNow.ToString("O"), level = level.ToString(),
                module = Clean(module), operation = Clean(operation), message = Clean(message), correlationId = Clean(correlationId),
                durationMs = durationMs, exception = Clean(exception?.ToString()), clientVersion = client, codeVersion = CodeVersion };
            string line = json.Encode(row);
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(path) && new FileInfo(path).Length >= maxBytes)
                    {
                        for (int i = 2; i >= 1; i--)
                        {
                            string next = path + "." + (i + 1), old = path + "." + i;
                            if (File.Exists(next)) File.Delete(next);
                            if (File.Exists(old)) File.Move(old, next);
                        }
                        File.Move(path, path + ".1");
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch (Exception e) { LastWriteError = e.Message; }
            }
            try { console?.Invoke(line); } catch { /* Diagnostic sinks must not terminate gameplay. */ }
        }
    }
}
