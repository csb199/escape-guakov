using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Contracts
{
    // AOT 与热更业务共享契约。IGameEntry 是业务入口，IModule 是宿主生命周期；不要在此声明具体玩法。
    public interface IGameEntry : IDisposable
    {
        void RegisterModules(IModuleRegistry modules);
        Task StartAsync(CancellationToken cancellation);
        Task StopAsync(CancellationToken cancellation);
    }
    // 示例自动验收接缝；正式业务无需实现，Bootstrap 仅在 --self-test 下调用。
    public interface IDemoValidation { Task<string> ValidateAsync(CancellationToken cancellation); }
    public interface IModuleRegistry { void Add(IModule module); }
    public interface IModule : IDisposable
    {
        string Id { get; }
        IReadOnlyList<string> Dependencies { get; }
        Task InitializeAsync(CancellationToken cancellation);
        Task StartAsync(CancellationToken cancellation);
        Task StopAsync(CancellationToken cancellation);
    }
    public interface IJsonCodec
    {
        string Encode<T>(T value);
        T Decode<T>(string json);
    }
    public interface IFileTransport
    {
        Task DownloadAsync(string relativePath, string partialPath, Action<long> progress, CancellationToken cancellation);
    }
    public enum GameLogLevel { Debug, Information, Warning, Error, Critical }
    public interface IGameLog
    {
        void Write(GameLogLevel level, string module, string operation, string message,
            string correlationId = "", long durationMs = 0, Exception exception = null);
    }
}
