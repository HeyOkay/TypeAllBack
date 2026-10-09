using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace LayoutPreviewPrototype;

internal sealed record UpdateResult(string Message, Uri? ReleasePage = null);
internal sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CancellationTokenSource _stop = new();
    private int _busy;
    internal static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    internal static bool Https(string? value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0;
    internal static UpdateResult Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String ||
            !Version.TryParse(version.GetString(), out var latest)) throw new InvalidDataException();
        if (latest <= current) return new("Установлена последняя версия.");
        if (!root.TryGetProperty("releasePage", out var page) || page.ValueKind != JsonValueKind.String ||
            !Https(page.GetString(), out var url)) throw new InvalidDataException();
        return new($"Доступна версия {latest}. Открыть страницу загрузки?", url);
    }
    internal async Task<UpdateResult?> CheckAsync(string feed)
    {
        if (!Https(feed, out var url)) return new("Источник обновлений пока не настроен.");
        if (Interlocked.Exchange(ref _busy, 1) != 0) return null;
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _stop.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException();
            await using var stream = await response.Content.ReadAsStreamAsync(_stop.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, _stop.Token)) > 0)
            {
                if (bytes.Length + count > 65536) throw new InvalidDataException();
                bytes.Write(buffer, 0, count);
            }
            return Parse(System.Text.Encoding.UTF8.GetString(bytes.ToArray()), Current);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        { return new("Не удалось проверить обновления. Попробуйте позже."); }
        finally { Volatile.Write(ref _busy, 0); }
    }
    public void Dispose() { _stop.Cancel(); _client.Dispose(); _stop.Dispose(); }
}
