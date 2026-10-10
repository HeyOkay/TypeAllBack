using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LayoutPreviewPrototype;

internal sealed record UpdatePackage(Version Version, Uri DownloadUrl, string Sha256, long Size);
internal sealed record DownloadProgress(long Bytes, long Total);
internal sealed record UpdateResult(string Message, Uri? ReleasePage = null, UpdatePackage? Package = null);
internal sealed class UpdateChecker : IDisposable
{
    internal const string DefaultFeed = "https://api.github.com/repos/HeyOkay/TypeAllBack/releases/latest";
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _stop = new();
    private int _busy;
    internal UpdateChecker(HttpMessageHandler? transport = null)
    {
        _client = new HttpClient(transport ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("TypeAllBack/" + Current.ToString(3));
    }
    internal static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    internal static bool Https(string? value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0;
    internal static bool DownloadAllowed(Uri uri) => uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/HeyOkay/TypeAllBack/releases/download/", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    internal static UpdateResult Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        string? text = root.TryGetProperty("tag_name", out var tag) ? tag.GetString()?.TrimStart('v') : root.GetProperty("version").GetString();
        if (!Version.TryParse(text, out var version) || version.Build < 0) throw new InvalidDataException();
        if (Normalize(version) <= Normalize(current)) return new("Установлена последняя версия.");
        Uri? page = null; string? address = null, digest = null; long size = 0;
        if (root.TryGetProperty("assets", out var assets))
        {
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() || root.TryGetProperty("prerelease", out var preview) && preview.GetBoolean()) throw new InvalidDataException();
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != $"TypeAllBack-{version.ToString(3)}-win-x64.msi") continue;
                if (address != null) throw new InvalidDataException();
                address = asset.GetProperty("browser_download_url").GetString();
                digest = asset.GetProperty("digest").GetString();
                size = asset.GetProperty("size").GetInt64();
            }
            Https(root.GetProperty("html_url").GetString(), out page);
        }
        else
        {
            address = root.GetProperty("downloadUrl").GetString(); digest = root.GetProperty("sha256").GetString(); size = root.GetProperty("size").GetInt64();
            if (root.TryGetProperty("releasePage", out var link)) Https(link.GetString(), out page);
        }
        if (digest?.StartsWith("sha256:", StringComparison.Ordinal) == true) digest = digest[7..];
        if (!Https(address, out var url) || !DownloadAllowed(url!) || digest == null || !Regex.IsMatch(digest, "\\A[0-9a-fA-F]{64}\\z") || size <= 0 || size > 256 * 1024 * 1024) throw new InvalidDataException();
        return new($"Доступна версия {version.ToString(3)}. Скачать и установить? Приложение закроется на время установки и запустится снова.", page, new(version, url!, digest, size));
    }
    internal async Task<UpdateResult?> CheckAsync(string feed)
    {
        if (!Https(feed, out var url)) return new("Источник обновлений пока не настроен.");
        if (Interlocked.Exchange(ref _busy, 1) != 0) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new("Опубликованных обновлений пока нет.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[4096]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + count > 65536) throw new InvalidDataException(); bytes.Write(buffer, 0, count);
            }
            return Parse(System.Text.Encoding.UTF8.GetString(bytes.ToArray()), Current);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return new("Не удалось проверить обновления. Попробуйте позже."); }
        finally { Volatile.Write(ref _busy, 0); }
    }
    internal async Task<string> DownloadAsync(UpdatePackage package, IProgress<DownloadProgress> progress, CancellationToken cancel, string? outputRoot = null)
    {
        if (!DownloadAllowed(package.DownloadUrl)) throw new InvalidDataException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel, _stop.Token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        string folder = Path.Combine(outputRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeAllBack", "Updates"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); string file = Path.Combine(folder, $"TypeAllBack-{package.Version.ToString(3)}-win-x64.msi"), partial = file + ".part";
        try
        {
            Uri uri = package.DownloadUrl; HttpResponseMessage? response = null;
            for (int hop = 0; hop < 5; hop++)
            {
                response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location; response.Dispose(); response = null;
                    if (location == null) throw new InvalidDataException();
                    uri = new Uri(uri, location);
                    if (uri.Scheme != "https" || uri.UserInfo.Length != 0 || !(uri.Host == "github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException();
                    continue;
                }
                break;
            }
            using (response)
            {
                if (response == null || !response.IsSuccessStatusCode) throw new HttpRequestException();
                if (response.Content.Headers.ContentLength is long length && length != package.Size) throw new InvalidDataException();
                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    byte[] buffer = new byte[65536]; long total = 0, lastReport = 0; int count;
                    while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        total += count; if (total > package.Size) throw new InvalidDataException();
                        hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        if (Environment.TickCount64 - lastReport >= 100) { lastReport = Environment.TickCount64; progress.Report(new(total, package.Size)); }
                    }
                    if (total != package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checksum mismatch");
                    progress.Report(new(total, total));
                }
            }
            token.ThrowIfCancellationRequested(); File.Move(partial, file); return file;
        }
        catch { if (File.Exists(partial)) File.Delete(partial); throw; }
    }
    public void Dispose() { _stop.Cancel(); _client.Dispose(); _stop.Dispose(); }
}
