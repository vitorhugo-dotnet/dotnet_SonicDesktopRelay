using System.Net;

namespace SonicDesktopRelay.ApiClient;

/// <summary>
/// A failed backend call. <see cref="ErrorCode"/> is the API's machine-readable
/// <c>code</c> field when present — "invalid_code", "not_paired",
/// "device_type_not_allowed" — which is what the UI branches on rather than the message.
/// </summary>
public sealed class ApiException(HttpStatusCode statusCode, string? errorCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? ErrorCode { get; } = errorCode;
}
