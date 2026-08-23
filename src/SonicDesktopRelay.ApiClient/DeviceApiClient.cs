using System.Net.Http.Json;
using System.Text.Json;

namespace SonicDesktopRelay.ApiClient;

public sealed class DeviceApiClient(HttpClient http)
{
    public async Task<BootstrapResponse> BootstrapAsync(string name, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/api/devices/bootstrap",
            new BootstrapRequest(name, DeviceConstants.DeviceType, DeviceConstants.Platform), ct);
        return await ApiResponse.ReadAsync<BootstrapResponse>(response, ct);
    }

    public async Task<TokenResponse> TokenAsync(Guid deviceId, string credentialSecret, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/api/devices/token",
            new TokenRequest(deviceId, credentialSecret), ct);
        return await ApiResponse.ReadAsync<TokenResponse>(response, ct);
    }
}

internal static class ApiResponse
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw Failure(response, body);

        var value = JsonSerializer.Deserialize<T>(body, Options);
        return value ?? throw new ApiException(response.StatusCode, null,
            $"The backend returned an empty body for {typeof(T).Name}.");
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        throw Failure(response, await response.Content.ReadAsStringAsync(ct));
    }

    private static ApiException Failure(HttpResponseMessage response, string body)
    {
        // The API answers failures with { "error": "...", "code": "..." }, but a proxy or an
        // unhandled exception can answer with anything at all. Parsing must not be the thing
        // that fails, or a 503 from a load balancer would surface as a JSON error.
        string? code = null;
        string message = $"The backend returned {(int)response.StatusCode}.";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out var codeElement))
                code = codeElement.GetString();
            if (document.RootElement.TryGetProperty("error", out var errorElement))
                message = errorElement.GetString() ?? message;
        }
        catch (JsonException)
        {
        }

        return new ApiException(response.StatusCode, code, message);
    }
}
