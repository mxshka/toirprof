using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ToirProf.Mobile.Models;

namespace ToirProf.Mobile.Services;

/// <summary>Ответ API: данные, сообщение и код. StatusCode 0 — нет связи с сервером.</summary>
public sealed record ApiResult<T>(bool Success, T? Data, string Message, int StatusCode)
{
    public bool IsOffline => StatusCode == 0;
}

/// <summary>
/// Клиент REST API для мобильного приложения. Работает только через API:
/// токен JWT в заголовке Authorization, ответы в обёртке ApiResponse.
/// </summary>
public sealed class MobileApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public string BaseUrl { get; private set; } = "http://localhost:5080";

    public string? Token { get; set; }

    public bool IsOnline { get; private set; } = true;

    public void SetServer(string url)
    {
        BaseUrl = url.Trim().TrimEnd('/');
    }

    public Task<ApiResult<T>> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path, null);

    public Task<ApiResult<T>> PostAsync<T>(string path, object? body) => SendAsync<T>(HttpMethod.Post, path, body);

    /// <summary>Отправка заранее сериализованного тела (из очереди офлайн-операций).</summary>
    public Task<ApiResult<JsonElement>> PostRawAsync(string path, string json) =>
        SendAsync<JsonElement>(HttpMethod.Post, path, new RawJson(json));

    public async Task<ApiResult<LoginResult>> LoginAsync(string login, string password)
    {
        var result = await PostAsync<LoginResult>("/api/auth/login", new { username = login, password });
        if (result.Success && result.Data is not null)
        {
            Token = result.Data.Token;
        }

        return result;
    }

    public async Task<bool> PingAsync()
    {
        var result = await GetAsync<JsonElement>("/api/health");
        return result.Success;
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body)
    {
        try
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            if (!string.IsNullOrEmpty(Token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            }

            if (body is RawJson raw)
            {
                request.Content = new StringContent(raw.Text, Encoding.UTF8, "application/json");
            }
            else if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request);
            IsOnline = true;
            var text = await response.Content.ReadAsStringAsync();
            return Parse<T>(response.StatusCode, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            IsOnline = false;
            return new ApiResult<T>(false, default, "Нет связи с сервером. Данные сохранены на телефоне.", 0);
        }
    }

    private static ApiResult<T> Parse<T>(HttpStatusCode status, string text)
    {
        var code = (int)status;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var root = doc.RootElement;
            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? string.Empty
                : string.Empty;
            T? data = default;
            if (root.TryGetProperty("data", out var d) && d.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                data = d.Deserialize<T>(Json);
            }

            if (code == 401)
            {
                message = "Сеанс истёк — войдите заново.";
            }

            return new ApiResult<T>(success && code < 400, data, message, code);
        }
        catch (JsonException)
        {
            return new ApiResult<T>(false, default, $"Сервер ответил {code}.", code);
        }
    }

    private sealed record RawJson(string Text);
}
