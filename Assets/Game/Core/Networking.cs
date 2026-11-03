using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    // Windows baseline: transport uses managed streams and never calls Unity APIs.
    public sealed class FileTransport : IFileTransport, IDisposable
    {
        readonly string directory;
        readonly Uri baseUri;
        readonly HttpClient http;
        public FileTransport(string source)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            {
                if (uri.Scheme != "https" && !uri.IsLoopback) throw new ArgumentException("Remote packages require HTTPS");
                baseUri = new Uri(source.TrimEnd('/') + "/");
                http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            }
            else directory = Path.GetFullPath(source);
        }
        public async Task<string> ManifestAsync(CancellationToken cancellation)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                try { return await ReadManifestAsync(deadline.Token); }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("Manifest download timed out"); }
            }
        }
        async Task<string> ReadManifestAsync(CancellationToken cancellation)
        {
            if (directory != null)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.Combine(directory, "manifest.json");
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Oversized manifest");
                return File.ReadAllText(path);
            }
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "manifest.json")))
            using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync()) using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192]; int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellation)) != 0)
                    {
                        if (output.Length + count > 1024 * 1024) throw new InvalidDataException("Oversized manifest");
                        output.Write(buffer, 0, count);
                    }
                    return System.Text.Encoding.UTF8.GetString(output.ToArray());
                }
            }
        }
        public async Task DownloadAsync(string relativePath, string partialPath, Action<long> progress, CancellationToken cancellation)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                try { await DownloadFileAsync(relativePath, partialPath, progress, deadline.Token); }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("Package file download timed out"); }
            }
        }
        async Task DownloadFileAsync(string relativePath, string partialPath, Action<long> progress, CancellationToken cancellation)
        {
            ReleaseVerifier.ValidateName(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath));
            long offset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (directory != null)
            {
                using (var input = File.OpenRead(Path.Combine(directory, relativePath)))
                {
                    if (offset > input.Length) offset = 0;
                    input.Position = offset;
                    await CopyAsync(input, partialPath, offset, progress, cancellation);
                }
                return;
            }
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath)))
            {
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation))
                {
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(partialPath); throw new IOException("Range no longer available"); }
                    response.EnsureSuccessStatusCode();
                    if (response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        if (response.Content.Headers.ContentRange?.From != offset) throw new InvalidDataException("Invalid range response");
                    }
                    else offset = 0; // Servers may ignore Range; never append their full response.
                    using (var input = await response.Content.ReadAsStreamAsync()) await CopyAsync(input, partialPath, offset, progress, cancellation);
                }
            }
        }
        static async Task CopyAsync(Stream input, string target, long offset, Action<long> progress, CancellationToken cancellation)
        {
            using (var output = new FileStream(target, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536]; int count; long total = offset;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellation)) != 0)
                {
                    total += count;
                    if (total > 512L * 1024 * 1024) throw new InvalidDataException("Download exceeds package file limit");
                    await output.WriteAsync(buffer, 0, count, cancellation); progress?.Invoke(total);
                }
                output.Flush(true);
            }
        }
        public void Dispose() => http?.Dispose();
    }

}
