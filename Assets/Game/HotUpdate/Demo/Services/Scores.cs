using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.HotUpdate
{
    public interface IScoreTransport
    {
        Task<int> SubmitAsync(string requestId, int score, CancellationToken cancellation);
    }
    public sealed class MockScoreTransport : IScoreTransport
    {
        readonly Dictionary<string, int> replies = new Dictionary<string, int>();
        public int DelayMs = 120;
        public int FailuresRemaining;
        public int UniqueSubmissions => replies.Count;
        public async Task<int> SubmitAsync(string requestId, int score, CancellationToken cancellation)
        {
            await Task.Delay(DelayMs, cancellation);
            if (FailuresRemaining > 0) { FailuresRemaining--; throw new IOException("Simulated transient network failure"); }
            if (!replies.TryGetValue(requestId, out int rank)) replies.Add(requestId, rank = Math.Max(1, 100 - score));
            return rank;
        }
    }
    public sealed class ScoreClient
    {
        readonly IScoreTransport transport;
        readonly IGameLog log;
        public ScoreClient(IScoreTransport transport, IGameLog log) { this.transport = transport; this.log = log; }
        public async Task<int> SubmitAsync(string requestId, int score, TimeSpan timeout, CancellationToken cancellation)
        {
            if (string.IsNullOrEmpty(requestId) || score < 0) throw new ArgumentException("Invalid score request");
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(timeout); var watch = Stopwatch.StartNew();
                try
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            int rank = await transport.SubmitAsync(requestId, score, deadline.Token);
                            log.Write(GameLogLevel.Information, "network", "score.submit", "rank=" + rank, requestId, watch.ElapsedMilliseconds); return rank;
                        }
                        catch (IOException e) when (attempt < 2)
                        { log.Write(GameLogLevel.Warning, "network", "score.retry", "Retrying idempotent request", requestId, watch.ElapsedMilliseconds, e); await Task.Delay(50 * (attempt + 1), deadline.Token); }
                    }
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("Leaderboard request timed out"); }
            }
        }
    }
}
