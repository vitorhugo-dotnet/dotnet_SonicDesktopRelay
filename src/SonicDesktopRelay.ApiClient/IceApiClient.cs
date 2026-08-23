namespace SonicDesktopRelay.ApiClient;

/// <summary>
/// <c>GET /api/webrtc/ice-servers</c>. The credentials TURN servers hand back are short-lived,
/// so this is fetched per session rather than cached for the life of the process.
/// </summary>
public sealed class IceApiClient(HttpClient http)
{
    public async Task<IceServersResponse> GetIceServersAsync(CancellationToken ct)
    {
        var response = await http.GetAsync("/api/webrtc/ice-servers", ct);
        return await ApiResponse.ReadAsync<IceServersResponse>(response, ct);
    }
}
