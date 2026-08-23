# SonicDesktopRelay app skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows app that registers itself with the SonicRelay backend on first run, creates a `screen_share` session and shows its code, joins another machine's session by code, and holds an authenticated signaling socket — with no media pipeline yet.

**Architecture:** Avalonia shell over a layered core. `Core` holds domain state and DPAPI-protected storage; `ApiClient` and `Signaling` speak to the backend; `Presentation` owns one state machine (`SessionRuntime`) that every screen reads from. Nothing above `Core` knows about Windows APIs. Media projects arrive in Phase 2.

**Tech Stack:** .NET 10, Avalonia 11.3.2, `System.Net.WebSockets.ClientWebSocket`, `System.Security.Cryptography.ProtectedData` (DPAPI), xunit.

**Spec:** `docs/superpowers/specs/2026-08-23-sonicdesktoprelay-design.md`

**Depends on:** `dotnet_SonicRelay/docs/superpowers/plans/2026-08-23-screen-share-sessions-backend.md` must be merged and deployed. Without it, `POST /api/devices/bootstrap` rejects `windows_desktop` and nothing in this plan can obtain a token.

## Global Constraints

- Device type string is exactly `windows_desktop`; platform is exactly `windows`; session mode is exactly `screen_share`.
- Target framework `net10.0` for every project in this phase. The Windows-specific TFM (`net10.0-windows10.0.19041.0`) arrives with `Media.Windows` in Phase 2 and must not be introduced here.
- No project in this repository may reference anything in `windows_SonicRelay`. The contract followed is `dotnet_SonicRelay/docs/protocol.md`.
- **No media.** No capture, encoder, decoder or peer connection in this phase. A task that reaches for one is out of scope.
- Never log SDP, ICE candidates, the credential secret, or the access token. Codes may be shown in the UI but not written to logs.
- Nullable reference types and implicit usings enabled everywhere.
- Run all tests with: `dotnet test SonicDesktopRelay.sln`
- Run one test with: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~<TestName>"`

## File Structure

| File | Responsibility |
|---|---|
| `SonicDesktopRelay.sln` | Solution |
| `Directory.Build.props` | Shared TFM, nullable, implicit usings |
| `src/SonicDesktopRelay.Core/BackendSettings.cs` | Backend base address and its validation |
| `src/SonicDesktopRelay.Core/Identity/DeviceCredential.cs` | The stored identity: id, secret, credential version |
| `src/SonicDesktopRelay.Core/Identity/IDeviceCredentialStore.cs` | Read/write/clear contract |
| `src/SonicDesktopRelay.Core/Identity/FileDeviceCredentialStore.cs` | DPAPI-protected file in the user profile |
| `src/SonicDesktopRelay.ApiClient/ApiContracts.cs` | Request/response records for every endpoint used |
| `src/SonicDesktopRelay.ApiClient/DeviceApiClient.cs` | Bootstrap and token exchange |
| `src/SonicDesktopRelay.ApiClient/SessionApiClient.cs` | Create, join, get participants, end |
| `src/SonicDesktopRelay.ApiClient/ApiException.cs` | Typed failure carrying status and error code |
| `src/SonicDesktopRelay.Core/Identity/DeviceIdentityService.cs` | Bootstrap-once, refresh-early, handle rotation |
| `src/SonicDesktopRelay.Signaling/SignalingEnvelope.cs` | The wire envelope and its message types |
| `src/SonicDesktopRelay.Signaling/ISignalingConnection.cs` | Connection contract the runtime consumes |
| `src/SonicDesktopRelay.Signaling/SignalingConnection.cs` | WebSocket implementation with reconnect |
| `src/SonicDesktopRelay.Presentation/SessionRuntime.cs` | The one state machine |
| `src/SonicDesktopRelay.Presentation/SessionSnapshot.cs` | Immutable projection the UI binds to |
| `src/SonicDesktopRelay.App/` | Avalonia shell, five pages |
| `tests/SonicDesktopRelay.Core.Tests/` | Storage, identity service |
| `tests/SonicDesktopRelay.ApiClient.Tests/` | HTTP clients against a fake handler |
| `tests/SonicDesktopRelay.Signaling.Tests/` | Envelope and connection behavior |
| `tests/SonicDesktopRelay.Presentation.Tests/` | State machine transitions |

---

### Task 1: Solution skeleton

**Files:**
- Create: `SonicDesktopRelay.sln`, `Directory.Build.props`, `global.json`
- Create: `src/SonicDesktopRelay.Core/SonicDesktopRelay.Core.csproj`
- Create: `tests/SonicDesktopRelay.Core.Tests/SonicDesktopRelay.Core.Tests.csproj`
- Create: `tests/SonicDesktopRelay.Core.Tests/SolutionSmokeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a solution where `dotnet test SonicDesktopRelay.sln` runs. Every later task adds projects to this solution.

- [ ] **Step 1: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `global.json`**

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 3: Create the projects**

```bash
dotnet new sln -n SonicDesktopRelay
dotnet new classlib -o src/SonicDesktopRelay.Core -n SonicDesktopRelay.Core
dotnet new xunit -o tests/SonicDesktopRelay.Core.Tests -n SonicDesktopRelay.Core.Tests
rm src/SonicDesktopRelay.Core/Class1.cs tests/SonicDesktopRelay.Core.Tests/UnitTest1.cs
dotnet sln add src/SonicDesktopRelay.Core tests/SonicDesktopRelay.Core.Tests
dotnet add tests/SonicDesktopRelay.Core.Tests reference src/SonicDesktopRelay.Core
```

- [ ] **Step 4: Write a test that proves the harness runs**

`tests/SonicDesktopRelay.Core.Tests/SolutionSmokeTests.cs`:

```csharp
using Xunit;

namespace SonicDesktopRelay.Core.Tests;

public sealed class SolutionSmokeTests
{
    [Fact]
    public void The_test_harness_runs()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 5: Run it**

Run: `dotnet test SonicDesktopRelay.sln`
Expected: PASS, 1 test.

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "chore: solution skeleton for SonicDesktopRelay"
```

---

### Task 2: Backend settings

**Files:**
- Create: `src/SonicDesktopRelay.Core/BackendSettings.cs`
- Test: `tests/SonicDesktopRelay.Core.Tests/BackendSettingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed record BackendSettings(Uri BaseAddress)` with `static BackendSettings? TryParse(string? value)` returning `null` for anything that is not an absolute `http`/`https` URI, and `Uri SignalingUri(Guid sessionId)` returning the `ws`/`wss` signaling URL.

- [ ] **Step 1: Write the failing tests**

```csharp
using Xunit;

namespace SonicDesktopRelay.Core.Tests;

public sealed class BackendSettingsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/api")]
    [InlineData("ftp://example.com")]
    public void Rejects_anything_that_is_not_an_absolute_http_url(string value)
    {
        Assert.Null(BackendSettings.TryParse(value));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.Null(BackendSettings.TryParse(null));
    }

    [Fact]
    public void Accepts_an_https_url_and_keeps_a_trailing_slash()
    {
        var settings = BackendSettings.TryParse("https://relay.example.com");

        Assert.NotNull(settings);
        Assert.Equal("https://relay.example.com/", settings!.BaseAddress.ToString());
    }

    [Fact]
    public void Signaling_over_https_uses_wss()
    {
        var settings = BackendSettings.TryParse("https://relay.example.com")!;
        var sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var uri = settings.SignalingUri(sessionId);

        Assert.Equal("wss://relay.example.com/ws/signaling?sessionId=11111111-2222-3333-4444-555555555555",
            uri.ToString());
    }

    [Fact]
    public void Signaling_over_http_uses_ws()
    {
        var settings = BackendSettings.TryParse("http://localhost:5080")!;

        var uri = settings.SignalingUri(Guid.Empty);

        Assert.StartsWith("ws://localhost:5080/ws/signaling?sessionId=", uri.ToString());
    }
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~BackendSettingsTests"`
Expected: compile error — `BackendSettings` does not exist.

- [ ] **Step 3: Implement**

`src/SonicDesktopRelay.Core/BackendSettings.cs`:

```csharp
namespace SonicDesktopRelay.Core;

/// <summary>
/// Where the SonicRelay backend lives. Parsed rather than constructed so a bad value typed
/// into Settings fails at the edge, with the UI still able to explain itself, instead of
/// throwing from inside an HTTP call later.
/// </summary>
public sealed record BackendSettings(Uri BaseAddress)
{
    public static BackendSettings? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return new BackendSettings(uri);
    }

    /// <summary>
    /// The authenticated signaling endpoint for one session. The scheme tracks the base
    /// address: an https backend must not be reached over a plaintext socket.
    /// </summary>
    public Uri SignalingUri(Guid sessionId)
    {
        var builder = new UriBuilder(BaseAddress)
        {
            Scheme = BaseAddress.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/ws/signaling",
            Query = $"sessionId={sessionId}"
        };
        return builder.Uri;
    }
}
```

- [ ] **Step 4: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~BackendSettingsTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/SonicDesktopRelay.Core tests/SonicDesktopRelay.Core.Tests
git commit -m "feat(core): backend settings with validated base address and signaling URI"
```

---

### Task 3: DPAPI-protected credential store

**Files:**
- Create: `src/SonicDesktopRelay.Core/Identity/DeviceCredential.cs`
- Create: `src/SonicDesktopRelay.Core/Identity/IDeviceCredentialStore.cs`
- Create: `src/SonicDesktopRelay.Core/Identity/FileDeviceCredentialStore.cs`
- Modify: `src/SonicDesktopRelay.Core/SonicDesktopRelay.Core.csproj`
- Test: `tests/SonicDesktopRelay.Core.Tests/FileDeviceCredentialStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `sealed record DeviceCredential(Guid DeviceId, string CredentialSecret, int CredentialVersion)`
  - `interface IDeviceCredentialStore { Task<DeviceCredential?> ReadAsync(CancellationToken ct); Task WriteAsync(DeviceCredential credential, CancellationToken ct); Task ClearAsync(CancellationToken ct); }`
  - `sealed class FileDeviceCredentialStore(string filePath) : IDeviceCredentialStore`

- [ ] **Step 1: Add the DPAPI package**

```bash
dotnet add src/SonicDesktopRelay.Core package System.Security.Cryptography.ProtectedData --version 10.0.0
```

- [ ] **Step 2: Write the failing tests**

`tests/SonicDesktopRelay.Core.Tests/FileDeviceCredentialStoreTests.cs`:

```csharp
using SonicDesktopRelay.Core.Identity;
using Xunit;

namespace SonicDesktopRelay.Core.Tests;

public sealed class FileDeviceCredentialStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sdr-{Guid.NewGuid():N}.bin");

    [Fact]
    public async Task Reading_before_anything_was_written_returns_null()
    {
        var store = new FileDeviceCredentialStore(_path);

        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_written_credential_round_trips()
    {
        var store = new FileDeviceCredentialStore(_path);
        var credential = new DeviceCredential(Guid.NewGuid(), "s3cr3t-value", 4);

        await store.WriteAsync(credential, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(credential, read);
    }

    [Fact]
    public async Task Writing_twice_replaces_rather_than_appends()
    {
        var store = new FileDeviceCredentialStore(_path);
        await store.WriteAsync(new DeviceCredential(Guid.NewGuid(), "first", 1), CancellationToken.None);
        var second = new DeviceCredential(Guid.NewGuid(), "second", 2);

        await store.WriteAsync(second, CancellationToken.None);

        Assert.Equal(second, await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_secret_is_not_readable_as_plain_text_on_disk()
    {
        var store = new FileDeviceCredentialStore(_path);
        await store.WriteAsync(new DeviceCredential(Guid.NewGuid(), "plain-secret-marker", 1), CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(_path);

        Assert.DoesNotContain("plain-secret-marker", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task A_corrupt_file_reads_as_no_credential_rather_than_throwing()
    {
        await File.WriteAllTextAsync(_path, "this is not protected data");
        var store = new FileDeviceCredentialStore(_path);

        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Clearing_removes_the_credential()
    {
        var store = new FileDeviceCredentialStore(_path);
        await store.WriteAsync(new DeviceCredential(Guid.NewGuid(), "gone", 1), CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~FileDeviceCredentialStoreTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 4: Implement the record and the contract**

`src/SonicDesktopRelay.Core/Identity/DeviceCredential.cs`:

```csharp
namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// The device's durable identity with the backend. The secret is shown by the API exactly
/// once, at bootstrap or when a token exchange rotates it, so losing this record means
/// re-registering as a new device and re-sharing codes.
/// </summary>
public sealed record DeviceCredential(Guid DeviceId, string CredentialSecret, int CredentialVersion);
```

`src/SonicDesktopRelay.Core/Identity/IDeviceCredentialStore.cs`:

```csharp
namespace SonicDesktopRelay.Core.Identity;

public interface IDeviceCredentialStore
{
    Task<DeviceCredential?> ReadAsync(CancellationToken ct);

    Task WriteAsync(DeviceCredential credential, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Implement the store**

`src/SonicDesktopRelay.Core/Identity/FileDeviceCredentialStore.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// Stores the device credential in one file under the user's profile, encrypted with DPAPI at
/// <see cref="DataProtectionScope.CurrentUser"/>. Another user on the same machine cannot
/// decrypt it, and a copied file is useless elsewhere.
/// </summary>
public sealed class FileDeviceCredentialStore(string filePath) : IDeviceCredentialStore
{
    // Bound to this application so a blob lifted from another DPAPI-using app cannot be fed in.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SonicDesktopRelay.DeviceCredential.v1");

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SonicDesktopRelay", "device.bin");

    public async Task<DeviceCredential?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(filePath, ct);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DeviceCredential>(plain);
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            // A file we cannot decrypt or parse is indistinguishable from no identity at all:
            // the caller's only sane response either way is to bootstrap a new device. Throwing
            // here would strand the app on a corrupt file with no path forward.
            return null;
        }
    }

    public async Task WriteAsync(DeviceCredential credential, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(credential);
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, protectedBytes, ct);
    }

    public Task ClearAsync(CancellationToken ct)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~FileDeviceCredentialStoreTests"`
Expected: PASS, 6 tests. These run on Windows only; DPAPI is a Windows API and this phase targets Windows.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(core): DPAPI-protected device credential store"
```

---

### Task 4: API contracts and the device client

**Files:**
- Create: `src/SonicDesktopRelay.ApiClient/SonicDesktopRelay.ApiClient.csproj`
- Create: `src/SonicDesktopRelay.ApiClient/ApiContracts.cs`
- Create: `src/SonicDesktopRelay.ApiClient/ApiException.cs`
- Create: `src/SonicDesktopRelay.ApiClient/DeviceApiClient.cs`
- Create: `tests/SonicDesktopRelay.ApiClient.Tests/SonicDesktopRelay.ApiClient.Tests.csproj`
- Create: `tests/SonicDesktopRelay.ApiClient.Tests/StubHttpMessageHandler.cs`
- Create: `tests/SonicDesktopRelay.ApiClient.Tests/DeviceApiClientTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `sealed record BootstrapResponse(Guid DeviceId, string CredentialSecret, int CredentialVersion)`
  - `sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string[] Scopes, Guid DeviceId, int CredentialVersion, string? RotatedCredentialSecret)`
  - `sealed class ApiException(HttpStatusCode statusCode, string? errorCode, string message) : Exception(message)` exposing `StatusCode` and `ErrorCode`
  - `sealed class DeviceApiClient(HttpClient http)` with `Task<BootstrapResponse> BootstrapAsync(string name, CancellationToken ct)` and `Task<TokenResponse> TokenAsync(Guid deviceId, string credentialSecret, CancellationToken ct)`

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/SonicDesktopRelay.ApiClient -n SonicDesktopRelay.ApiClient
dotnet new xunit -o tests/SonicDesktopRelay.ApiClient.Tests -n SonicDesktopRelay.ApiClient.Tests
rm src/SonicDesktopRelay.ApiClient/Class1.cs tests/SonicDesktopRelay.ApiClient.Tests/UnitTest1.cs
dotnet sln add src/SonicDesktopRelay.ApiClient tests/SonicDesktopRelay.ApiClient.Tests
dotnet add src/SonicDesktopRelay.ApiClient reference src/SonicDesktopRelay.Core
dotnet add tests/SonicDesktopRelay.ApiClient.Tests reference src/SonicDesktopRelay.ApiClient
```

- [ ] **Step 2: Write the stub handler**

`tests/SonicDesktopRelay.ApiClient.Tests/StubHttpMessageHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace SonicDesktopRelay.ApiClient.Tests;

/// <summary>
/// Answers each request from a queued script and records what was asked. Real HTTP would make
/// these tests slow and flaky; what is under test is the client's own behavior, not the wire.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public StubHttpMessageHandler Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, body) = _responses.Count > 0
            ? _responses.Dequeue()
            : (HttpStatusCode.InternalServerError, "{}");
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
```

- [ ] **Step 3: Write the failing tests**

`tests/SonicDesktopRelay.ApiClient.Tests/DeviceApiClientTests.cs`:

```csharp
using System.Net;
using Xunit;

namespace SonicDesktopRelay.ApiClient.Tests;

public sealed class DeviceApiClientTests
{
    [Fact]
    public async Task Bootstrap_posts_the_windows_desktop_type_and_the_windows_platform()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.Created,
            """{"deviceId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","credentialSecret":"abc","credentialVersion":1}""");
        var client = new DeviceApiClient(HttpClientFor(handler));

        var response = await client.BootstrapAsync("Desk PC", CancellationToken.None);

        Assert.Equal("/api/devices/bootstrap", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("\"deviceType\":\"windows_desktop\"", handler.RequestBodies[0]);
        Assert.Contains("\"platform\":\"windows\"", handler.RequestBodies[0]);
        Assert.Contains("\"name\":\"Desk PC\"", handler.RequestBodies[0]);
        Assert.Equal("abc", response.CredentialSecret);
        Assert.Equal(1, response.CredentialVersion);
    }

    [Fact]
    public async Task Token_returns_the_rotated_secret_when_the_backend_sends_one()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK,
            """
            {"accessToken":"jwt","expiresAt":"2026-08-23T14:05:00Z","scopes":["session:create"],
             "deviceId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","credentialVersion":2,
             "rotatedCredentialSecret":"new-secret"}
            """);
        var client = new DeviceApiClient(HttpClientFor(handler));

        var response = await client.TokenAsync(Guid.NewGuid(), "old-secret", CancellationToken.None);

        Assert.Equal("jwt", response.AccessToken);
        Assert.Equal("new-secret", response.RotatedCredentialSecret);
        Assert.Equal(2, response.CredentialVersion);
    }

    [Fact]
    public async Task Token_leaves_the_rotated_secret_null_when_the_backend_omits_it()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK,
            """
            {"accessToken":"jwt","expiresAt":"2026-08-23T14:05:00Z","scopes":[],
             "deviceId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","credentialVersion":1,
             "rotatedCredentialSecret":null}
            """);
        var client = new DeviceApiClient(HttpClientFor(handler));

        var response = await client.TokenAsync(Guid.NewGuid(), "secret", CancellationToken.None);

        Assert.Null(response.RotatedCredentialSecret);
    }

    [Fact]
    public async Task A_failure_becomes_an_ApiException_carrying_status_and_error_code()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.BadRequest,
            """{"error":"Unsupported device type.","code":"invalid_device_type"}""");
        var client = new DeviceApiClient(HttpClientFor(handler));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.BootstrapAsync("Desk PC", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("invalid_device_type", exception.ErrorCode);
    }

    [Fact]
    public async Task A_failure_without_a_code_still_produces_an_ApiException()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.ServiceUnavailable, "not json at all");
        var client = new DeviceApiClient(HttpClientFor(handler));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.BootstrapAsync("Desk PC", CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
    }

    private static HttpClient HttpClientFor(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://relay.example.com") };
}
```

- [ ] **Step 4: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~DeviceApiClientTests"`
Expected: compile error — `DeviceApiClient` does not exist.

- [ ] **Step 5: Implement the contracts**

`src/SonicDesktopRelay.ApiClient/ApiContracts.cs`:

```csharp
namespace SonicDesktopRelay.ApiClient;

public sealed record BootstrapRequest(string Name, string DeviceType, string Platform);

public sealed record BootstrapResponse(Guid DeviceId, string CredentialSecret, int CredentialVersion);

public sealed record TokenRequest(Guid DeviceId, string CredentialSecret);

/// <summary>
/// A non-null <see cref="RotatedCredentialSecret"/> means the backend replaced this identity:
/// <see cref="DeviceId"/> is a new id and the secret is its new one. Both must be persisted in
/// place of what was stored, because the previous pair no longer exists.
/// </summary>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string[] Scopes,
    Guid DeviceId,
    int CredentialVersion,
    string? RotatedCredentialSecret);

public static class DeviceConstants
{
    public const string DeviceType = "windows_desktop";
    public const string Platform = "windows";
}
```

`src/SonicDesktopRelay.ApiClient/ApiException.cs`:

```csharp
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
```

- [ ] **Step 6: Implement the client**

`src/SonicDesktopRelay.ApiClient/DeviceApiClient.cs`:

```csharp
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
```

- [ ] **Step 7: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~DeviceApiClientTests"`
Expected: PASS, 5 tests.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(api): device bootstrap and token exchange with typed failures"
```

---

### Task 5: Device identity service

**Files:**
- Create: `src/SonicDesktopRelay.Core/Identity/DeviceIdentityService.cs`
- Create: `src/SonicDesktopRelay.Core/Identity/IDeviceApi.cs`
- Modify: `src/SonicDesktopRelay.ApiClient/DeviceApiClient.cs` (implement `IDeviceApi`)
- Modify: `src/SonicDesktopRelay.Core/SonicDesktopRelay.Core.csproj` (no new reference — the interface lives in Core so `ApiClient` depends on `Core`, never the reverse)
- Test: `tests/SonicDesktopRelay.Core.Tests/DeviceIdentityServiceTests.cs`

**Interfaces:**
- Consumes: `IDeviceCredentialStore` and `DeviceCredential` (Task 3); `BootstrapResponse`/`TokenResponse` shapes (Task 4), redeclared in Core as `IDeviceApi` so the dependency points inward.
- Produces:
  - `interface IDeviceApi { Task<DeviceCredential> BootstrapAsync(string name, CancellationToken ct); Task<AccessToken> TokenAsync(Guid deviceId, string secret, CancellationToken ct); }`
  - `sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, Guid DeviceId, int CredentialVersion, string? RotatedCredentialSecret)`
  - `sealed class DeviceIdentityService(IDeviceCredentialStore store, IDeviceApi api, TimeProvider time)` with `Task<string> GetAccessTokenAsync(string deviceName, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests**

`tests/SonicDesktopRelay.Core.Tests/DeviceIdentityServiceTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using SonicDesktopRelay.Core.Identity;
using Xunit;

namespace SonicDesktopRelay.Core.Tests;

public sealed class DeviceIdentityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_call_bootstraps_and_persists_the_credential()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var service = new DeviceIdentityService(store, api, TimeFrom(Now));

        var token = await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal("token-1", token);
        Assert.Equal(1, api.BootstrapCalls);
        Assert.NotNull(await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_second_call_reuses_the_stored_credential_and_the_cached_token()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var service = new DeviceIdentityService(store, api, TimeFrom(Now));

        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal(1, api.BootstrapCalls);
        Assert.Equal(1, api.TokenCalls);
    }

    [Fact]
    public async Task The_token_is_refreshed_once_less_than_a_fifth_of_its_life_remains()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var time = TimeFrom(Now);
        var service = new DeviceIdentityService(store, api, time);
        // FakeDeviceApi issues tokens valid for 60 minutes; the refresh threshold is 12 left.
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(49));
        var token = await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal(2, api.TokenCalls);
        Assert.Equal("token-2", token);
    }

    [Fact]
    public async Task A_token_with_plenty_of_life_left_is_not_refreshed()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var time = TimeFrom(Now);
        var service = new DeviceIdentityService(store, api, time);
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(30));
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal(1, api.TokenCalls);
    }

    [Fact]
    public async Task A_rotated_secret_replaces_both_the_device_id_and_the_secret_on_disk()
    {
        var store = new InMemoryCredentialStore();
        var rotatedDeviceId = Guid.NewGuid();
        var api = new FakeDeviceApi
        {
            RotateOnCall = 1,
            RotatedDeviceId = rotatedDeviceId,
            RotatedSecret = "rotated-secret"
        };
        var service = new DeviceIdentityService(store, api, TimeFrom(Now));

        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        var stored = await store.ReadAsync(CancellationToken.None);
        Assert.Equal(rotatedDeviceId, stored!.DeviceId);
        Assert.Equal("rotated-secret", stored.CredentialSecret);
    }

    private static FakeTimeProvider TimeFrom(DateTimeOffset start) => new(start);

    private sealed class InMemoryCredentialStore : IDeviceCredentialStore
    {
        private DeviceCredential? _credential;

        public Task<DeviceCredential?> ReadAsync(CancellationToken ct) => Task.FromResult(_credential);

        public Task WriteAsync(DeviceCredential credential, CancellationToken ct)
        {
            _credential = credential;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct)
        {
            _credential = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceApi : IDeviceApi
    {
        public int BootstrapCalls { get; private set; }

        public int TokenCalls { get; private set; }

        public int RotateOnCall { get; init; }

        public Guid RotatedDeviceId { get; init; }

        public string? RotatedSecret { get; init; }

        public Task<DeviceCredential> BootstrapAsync(string name, CancellationToken ct)
        {
            BootstrapCalls++;
            return Task.FromResult(new DeviceCredential(Guid.NewGuid(), "secret", 1));
        }

        public Task<AccessToken> TokenAsync(Guid deviceId, string secret, CancellationToken ct)
        {
            TokenCalls++;
            var rotate = TokenCalls == RotateOnCall;
            return Task.FromResult(new AccessToken(
                $"token-{TokenCalls}",
                new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero).AddMinutes(60),
                rotate ? RotatedDeviceId : deviceId,
                1,
                rotate ? RotatedSecret : null));
        }
    }
}
```

The token's `ExpiresAt` is a fixed instant because `FakeTimeProvider` starts at the same one;
that keeps the refresh-threshold arithmetic in the test readable.

- [ ] **Step 2: Add the fake time package**

```bash
dotnet add tests/SonicDesktopRelay.Core.Tests package Microsoft.Extensions.TimeProvider.Testing --version 9.10.0
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~DeviceIdentityServiceTests"`
Expected: compile error — `IDeviceApi`, `AccessToken` and `DeviceIdentityService` do not exist.

- [ ] **Step 4: Implement the contract**

`src/SonicDesktopRelay.Core/Identity/IDeviceApi.cs`:

```csharp
namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// A short-lived DeviceBearer token. <see cref="RotatedCredentialSecret"/> being non-null
/// means the backend replaced the identity during this exchange.
/// </summary>
public sealed record AccessToken(
    string Value,
    DateTimeOffset ExpiresAt,
    Guid DeviceId,
    int CredentialVersion,
    string? RotatedCredentialSecret);

/// <summary>
/// The device endpoints, declared in Core so identity logic does not depend on the HTTP
/// layer. ApiClient implements it; the dependency points inward.
/// </summary>
public interface IDeviceApi
{
    Task<DeviceCredential> BootstrapAsync(string name, CancellationToken ct);

    Task<AccessToken> TokenAsync(Guid deviceId, string secret, CancellationToken ct);
}
```

- [ ] **Step 5: Implement the service**

`src/SonicDesktopRelay.Core/Identity/DeviceIdentityService.cs`:

```csharp
namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// Owns the device's identity: registers it once, keeps a valid access token, and survives
/// the backend rotating the identity underneath it. There is no login — the machine's own
/// registration is the account.
/// </summary>
public sealed class DeviceIdentityService(
    IDeviceCredentialStore store,
    IDeviceApi api,
    TimeProvider time)
{
    // Refresh once less than a fifth of the token's lifetime remains. Waiting for a 401
    // would mean discovering the expiry mid-session, and the cheapest moment to renew is
    // any moment that is not "while a screen session is negotiating".
    private const double RefreshWhenRemainingFraction = 0.2;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccessToken? _token;
    private DateTimeOffset _tokenIssuedAt;

    public async Task<string> GetAccessTokenAsync(string deviceName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (_token is not null && !NeedsRefresh(_token, now)) return _token.Value;

            var credential = await store.ReadAsync(ct);
            if (credential is null)
            {
                credential = await api.BootstrapAsync(deviceName, ct);
                await store.WriteAsync(credential, ct);
            }

            var token = await api.TokenAsync(credential.DeviceId, credential.CredentialSecret, ct);
            if (token.RotatedCredentialSecret is not null)
            {
                // The identity we held no longer exists: the next call using the old id would
                // get a 401. Persist the replacement before anything else can read the store.
                await store.WriteAsync(
                    new DeviceCredential(token.DeviceId, token.RotatedCredentialSecret, token.CredentialVersion), ct);
            }

            _token = token;
            _tokenIssuedAt = now;
            return token.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the next call to exchange a fresh token, after a 401 for instance.</summary>
    public void Invalidate() => _token = null;

    private bool NeedsRefresh(AccessToken token, DateTimeOffset now)
    {
        var lifetime = token.ExpiresAt - _tokenIssuedAt;
        if (lifetime <= TimeSpan.Zero) return true;
        var remaining = token.ExpiresAt - now;
        return remaining <= lifetime * RefreshWhenRemainingFraction;
    }
}
```

- [ ] **Step 6: Make `DeviceApiClient` implement `IDeviceApi`**

In `src/SonicDesktopRelay.ApiClient/DeviceApiClient.cs`, change the declaration and add the
adapter methods. Keep the existing HTTP-shaped methods — the tests from Task 4 assert on them:

```csharp
public sealed class DeviceApiClient(HttpClient http) : Core.Identity.IDeviceApi
{
    // ... BootstrapAsync(string, CancellationToken) and TokenAsync(Guid, string, CancellationToken)
    // as written in Task 4, renamed to BootstrapRawAsync / TokenRawAsync ...

    async Task<Core.Identity.DeviceCredential> Core.Identity.IDeviceApi.BootstrapAsync(
        string name, CancellationToken ct)
    {
        var response = await BootstrapRawAsync(name, ct);
        return new Core.Identity.DeviceCredential(
            response.DeviceId, response.CredentialSecret, response.CredentialVersion);
    }

    async Task<Core.Identity.AccessToken> Core.Identity.IDeviceApi.TokenAsync(
        Guid deviceId, string secret, CancellationToken ct)
    {
        var response = await TokenRawAsync(deviceId, secret, ct);
        return new Core.Identity.AccessToken(
            response.AccessToken, response.ExpiresAt, response.DeviceId,
            response.CredentialVersion, response.RotatedCredentialSecret);
    }
}
```

Rename the two methods from Task 4 to `BootstrapRawAsync` and `TokenRawAsync`, and update
`DeviceApiClientTests` to call the new names. The explicit interface implementation keeps both
shapes available without an ambiguous overload.

- [ ] **Step 7: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln`
Expected: PASS — 5 device-api tests (renamed calls) plus 5 identity tests.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(core): device identity service with proactive refresh and rotation handling"
```

---

### Task 6: Session API client

**Files:**
- Create: `src/SonicDesktopRelay.ApiClient/SessionApiClient.cs`
- Modify: `src/SonicDesktopRelay.ApiClient/ApiContracts.cs`
- Test: `tests/SonicDesktopRelay.ApiClient.Tests/SessionApiClientTests.cs`

**Interfaces:**
- Consumes: `ApiException`, `ApiResponse` (Task 4).
- Produces:
  - `sealed record SessionResponse(Guid Id, Guid SourceDeviceId, string Status, string Mode, int MaxViewers, DateTimeOffset CodeExpiresAt, string? Code)`
  - `sealed record ParticipantResponse(Guid ParticipantId, string Role, string Status, bool IsSelf)`
  - `sealed record ParticipantsResponse(Guid SessionId, string Mode, ParticipantResponse[] Participants)`
  - `sealed class SessionApiClient(HttpClient http)` with `CreateScreenShareAsync(int maxViewers, CancellationToken)`, `JoinAsync(string code, CancellationToken)`, `GetParticipantsAsync(Guid sessionId, CancellationToken)`, `EndAsync(Guid sessionId, CancellationToken)`

- [ ] **Step 1: Write the failing tests**

`tests/SonicDesktopRelay.ApiClient.Tests/SessionApiClientTests.cs`:

```csharp
using System.Net;
using Xunit;

namespace SonicDesktopRelay.ApiClient.Tests;

public sealed class SessionApiClientTests
{
    private const string CreatedBody = """
        {"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","sourceDeviceId":"6f9619ff-8b86-d011-b42d-00cf4fc964fe",
         "status":"waiting","mode":"screen_share","maxViewers":3,
         "codeExpiresAt":"2026-08-23T13:00:00Z","code":"AB12CD"}
        """;

    [Fact]
    public async Task Create_requests_the_screen_share_mode_and_returns_the_code()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.Created, CreatedBody);
        var client = new SessionApiClient(HttpClientFor(handler));

        var session = await client.CreateScreenShareAsync(3, CancellationToken.None);

        Assert.Equal("/api/sessions", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("\"mode\":\"screen_share\"", handler.RequestBodies[0]);
        Assert.Contains("\"maxViewers\":3", handler.RequestBodies[0]);
        Assert.Equal("AB12CD", session.Code);
        Assert.Equal("screen_share", session.Mode);
    }

    [Fact]
    public async Task Join_uppercases_and_trims_the_code_before_sending_it()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, CreatedBody);
        var client = new SessionApiClient(HttpClientFor(handler));

        await client.JoinAsync("  ab12cd  ", CancellationToken.None);

        Assert.Equal("/api/sessions/join", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("\"code\":\"AB12CD\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Join_surfaces_device_type_not_allowed_as_the_error_code()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.Forbidden,
            """{"error":"This session type is not available for this device.","code":"device_type_not_allowed"}""");
        var client = new SessionApiClient(HttpClientFor(handler));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.JoinAsync("AB12CD", CancellationToken.None));

        Assert.Equal("device_type_not_allowed", exception.ErrorCode);
    }

    [Fact]
    public async Task Join_surfaces_invalid_code_as_the_error_code()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.NotFound,
            """{"error":"Invalid or expired session code.","code":"invalid_code"}""");
        var client = new SessionApiClient(HttpClientFor(handler));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.JoinAsync("AB12CD", CancellationToken.None));

        Assert.Equal("invalid_code", exception.ErrorCode);
    }

    [Fact]
    public async Task Participants_are_returned_with_their_roles()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK,
            """
            {"sessionId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","mode":"screen_share",
             "participants":[
               {"participantId":"6f9619ff-8b86-d011-b42d-00cf4fc96401","role":"publisher","status":"connected","isSelf":true},
               {"participantId":"6f9619ff-8b86-d011-b42d-00cf4fc96402","role":"viewer","status":"connected","isSelf":false}]}
            """);
        var client = new SessionApiClient(HttpClientFor(handler));

        var participants = await client.GetParticipantsAsync(
            Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"), CancellationToken.None);

        Assert.Equal(2, participants.Participants.Length);
        Assert.Single(participants.Participants, p => p.Role == "publisher" && p.IsSelf);
    }

    [Fact]
    public async Task Ending_a_session_posts_to_the_end_route()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, CreatedBody);
        var client = new SessionApiClient(HttpClientFor(handler));
        var sessionId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        await client.EndAsync(sessionId, CancellationToken.None);

        Assert.Equal($"/api/sessions/{sessionId}/end", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    private static HttpClient HttpClientFor(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://relay.example.com") };
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SessionApiClientTests"`
Expected: compile error — `SessionApiClient` does not exist.

- [ ] **Step 3: Add the contracts**

Append to `src/SonicDesktopRelay.ApiClient/ApiContracts.cs`:

```csharp
public sealed record CreateSessionRequest(int MaxViewers, string Mode);

public sealed record JoinSessionRequest(string Code);

/// <summary>
/// <c>Code</c> is present only on the responses that issue one (create and rotate); reading a
/// session back never re-exposes it.
/// </summary>
public sealed record SessionResponse(
    Guid Id,
    Guid SourceDeviceId,
    string Status,
    string Mode,
    int MaxViewers,
    DateTimeOffset CodeExpiresAt,
    string? Code);

public sealed record ParticipantResponse(Guid ParticipantId, string Role, string Status, bool IsSelf);

public sealed record ParticipantsResponse(Guid SessionId, string Mode, ParticipantResponse[] Participants);

public static class SessionModes
{
    public const string ScreenShare = "screen_share";
}

public static class ApiErrorCodes
{
    public const string InvalidCode = "invalid_code";
    public const string NotPaired = "not_paired";
    public const string DeviceTypeNotAllowed = "device_type_not_allowed";
    public const string InvalidSessionMode = "invalid_session_mode";
}
```

- [ ] **Step 4: Implement the client**

`src/SonicDesktopRelay.ApiClient/SessionApiClient.cs`:

```csharp
using System.Net.Http.Json;

namespace SonicDesktopRelay.ApiClient;

public sealed class SessionApiClient(HttpClient http)
{
    public async Task<SessionResponse> CreateScreenShareAsync(int maxViewers, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest(maxViewers, SessionModes.ScreenShare), ct);
        return await ApiResponse.ReadAsync<SessionResponse>(response, ct);
    }

    /// <summary>
    /// The backend trims and uppercases the code itself, but doing it here too means the app
    /// never sends what the user's keyboard happened to produce, and the request logged on a
    /// failure matches what was actually evaluated.
    /// </summary>
    public async Task<SessionResponse> JoinAsync(string code, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/api/sessions/join",
            new JoinSessionRequest(code.Trim().ToUpperInvariant()), ct);
        return await ApiResponse.ReadAsync<SessionResponse>(response, ct);
    }

    public async Task<ParticipantsResponse> GetParticipantsAsync(Guid sessionId, CancellationToken ct)
    {
        var response = await http.GetAsync($"/api/sessions/{sessionId}/participants", ct);
        return await ApiResponse.ReadAsync<ParticipantsResponse>(response, ct);
    }

    public async Task EndAsync(Guid sessionId, CancellationToken ct)
    {
        var response = await http.PostAsync($"/api/sessions/{sessionId}/end", content: null, ct);
        await ApiResponse.EnsureSuccessAsync(response, ct);
    }
}
```

- [ ] **Step 5: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SessionApiClientTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(api): screen-share session create, join, participants and end"
```

---

### Task 7: Signaling envelope

**Files:**
- Create: `src/SonicDesktopRelay.Signaling/SonicDesktopRelay.Signaling.csproj`
- Create: `src/SonicDesktopRelay.Signaling/SignalingEnvelope.cs`
- Create: `src/SonicDesktopRelay.Signaling/SignalingMessageTypes.cs`
- Create: `tests/SonicDesktopRelay.Signaling.Tests/SonicDesktopRelay.Signaling.Tests.csproj`
- Create: `tests/SonicDesktopRelay.Signaling.Tests/SignalingEnvelopeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `sealed record SignalingEnvelope(string Type, Guid? MessageId, Guid? SessionId, Guid? From, Guid? To, DateTimeOffset? Timestamp, JsonElement? Payload)`
  - `static class SignalingEnvelope.Serializer` with `string ToJson(string type, Guid? to, object? payload)` and `SignalingEnvelope? TryParse(string json)`
  - `static class SignalingMessageTypes` with the exact wire strings.

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/SonicDesktopRelay.Signaling -n SonicDesktopRelay.Signaling
dotnet new xunit -o tests/SonicDesktopRelay.Signaling.Tests -n SonicDesktopRelay.Signaling.Tests
rm src/SonicDesktopRelay.Signaling/Class1.cs tests/SonicDesktopRelay.Signaling.Tests/UnitTest1.cs
dotnet sln add src/SonicDesktopRelay.Signaling tests/SonicDesktopRelay.Signaling.Tests
dotnet add src/SonicDesktopRelay.Signaling reference src/SonicDesktopRelay.Core
dotnet add tests/SonicDesktopRelay.Signaling.Tests reference src/SonicDesktopRelay.Signaling
```

- [ ] **Step 2: Write the failing tests**

`tests/SonicDesktopRelay.Signaling.Tests/SignalingEnvelopeTests.cs`:

```csharp
using System.Text.Json;
using Xunit;

namespace SonicDesktopRelay.Signaling.Tests;

public sealed class SignalingEnvelopeTests
{
    [Fact]
    public void An_outbound_message_carries_only_type_to_and_payload()
    {
        var to = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        var json = SignalingEnvelope.Serializer.ToJson(SignalingMessageTypes.ViewerReady, to, new { });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("viewer.ready", root.GetProperty("type").GetString());
        Assert.Equal(to, root.GetProperty("to").GetGuid());
        Assert.True(root.TryGetProperty("payload", out _));
        // sessionId, from and timestamp are the server's to assign; sending them is at best
        // ignored and at worst a client claiming an identity it does not have.
        Assert.False(root.TryGetProperty("sessionId", out _));
        Assert.False(root.TryGetProperty("from", out _));
        Assert.False(root.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public void A_broadcast_message_omits_the_recipient()
    {
        var json = SignalingEnvelope.Serializer.ToJson(SignalingMessageTypes.Ping, to: null, payload: null);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("to", out _));
    }

    [Fact]
    public void A_server_frame_parses_into_its_fields()
    {
        const string frame = """
            {"type":"session.joined","messageId":"6f9619ff-8b86-d011-b42d-00cf4fc96401",
             "sessionId":"6f9619ff-8b86-d011-b42d-00cf4fc96402","from":null,
             "to":"6f9619ff-8b86-d011-b42d-00cf4fc96403","timestamp":"2026-08-23T14:00:00Z",
             "payload":{"participantId":"6f9619ff-8b86-d011-b42d-00cf4fc96403","role":"publisher"}}
            """;

        var envelope = SignalingEnvelope.Serializer.TryParse(frame);

        Assert.NotNull(envelope);
        Assert.Equal("session.joined", envelope!.Type);
        Assert.Null(envelope.From);
        Assert.Equal(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96403"), envelope.To);
        Assert.Equal("publisher", envelope.Payload!.Value.GetProperty("role").GetString());
    }

    [Fact]
    public void An_unparseable_frame_returns_null_rather_than_throwing()
    {
        Assert.Null(SignalingEnvelope.Serializer.TryParse("{ this is not json"));
    }

    [Fact]
    public void A_frame_without_a_type_returns_null()
    {
        Assert.Null(SignalingEnvelope.Serializer.TryParse("""{"messageId":"x"}"""));
    }

    [Fact]
    public void An_unknown_type_still_parses_so_the_app_can_ignore_it_deliberately()
    {
        var envelope = SignalingEnvelope.Serializer.TryParse("""{"type":"something.new","payload":{}}""");

        Assert.NotNull(envelope);
        Assert.Equal("something.new", envelope!.Type);
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SignalingEnvelopeTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 4: Implement the message types**

`src/SonicDesktopRelay.Signaling/SignalingMessageTypes.cs`:

```csharp
namespace SonicDesktopRelay.Signaling;

/// <summary>
/// The wire strings from dotnet_SonicRelay/docs/protocol.md. Server-generated types are
/// listed for recognition only — sending one is rejected by the server.
/// </summary>
public static class SignalingMessageTypes
{
    public const string SessionJoined = "session.joined";
    public const string SessionLeft = "session.left";
    public const string SessionEnded = "session.ended";
    public const string ParticipantDisconnected = "participant.disconnected";
    public const string ParticipantReconnected = "participant.reconnected";
    public const string ParticipantCapabilities = "participant.capabilities";
    public const string Error = "error";

    public const string PublisherReady = "publisher.ready";
    public const string ViewerReady = "viewer.ready";
    public const string WebRtcOffer = "webrtc.offer";
    public const string WebRtcAnswer = "webrtc.answer";
    public const string WebRtcIceCandidate = "webrtc.ice_candidate";
    public const string WebRtcRenegotiate = "webrtc.renegotiate";
    public const string Ping = "ping";
    public const string Pong = "pong";
}
```

- [ ] **Step 5: Implement the envelope**

`src/SonicDesktopRelay.Signaling/SignalingEnvelope.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonicDesktopRelay.Signaling;

/// <summary>
/// One signaling frame. Inbound, every metadata field is the server's word. Outbound, only
/// <c>type</c>, <c>to</c> and <c>payload</c> are ours to set — the server overwrites
/// <c>from</c> with the authenticated participant and assigns its own timestamp, so sending
/// them would be noise at best.
/// </summary>
public sealed record SignalingEnvelope(
    string Type,
    Guid? MessageId,
    Guid? SessionId,
    Guid? From,
    Guid? To,
    DateTimeOffset? Timestamp,
    JsonElement? Payload)
{
    public static class Serializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string ToJson(string type, Guid? to, object? payload) =>
            JsonSerializer.Serialize(new OutboundFrame(type, to, payload), Options);

        public static SignalingEnvelope? TryParse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeElement)) return null;
                var type = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(type)) return null;

                return new SignalingEnvelope(
                    type,
                    ReadGuid(root, "messageId"),
                    ReadGuid(root, "sessionId"),
                    ReadGuid(root, "from"),
                    ReadGuid(root, "to"),
                    ReadTimestamp(root),
                    root.TryGetProperty("payload", out var payload) ? payload.Clone() : null);
            }
            catch (JsonException)
            {
                // A frame we cannot parse is a frame we ignore. Throwing here would tear down
                // a healthy socket over one malformed message.
                return null;
            }
        }

        private static Guid? ReadGuid(JsonElement root, string name) =>
            root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out var value)
                ? value
                : null;

        private static DateTimeOffset? ReadTimestamp(JsonElement root) =>
            root.TryGetProperty("timestamp", out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), out var value)
                ? value
                : null;

        private sealed record OutboundFrame(
            [property: JsonPropertyName("type")] string Type,
            [property: JsonPropertyName("to")] Guid? To,
            [property: JsonPropertyName("payload")] object? Payload);
    }
}
```

- [ ] **Step 6: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SignalingEnvelopeTests"`
Expected: PASS, 6 tests.

Note: the `ping` test asserts `to` is absent, which `JsonIgnoreCondition.WhenWritingNull`
gives us. `payload: null` is likewise omitted, which the server accepts for `ping`.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(signaling): wire envelope and message types"
```

---

### Task 8: Signaling connection

**Files:**
- Create: `src/SonicDesktopRelay.Signaling/ISignalingConnection.cs`
- Create: `src/SonicDesktopRelay.Signaling/IWebSocketAdapter.cs`
- Create: `src/SonicDesktopRelay.Signaling/ClientWebSocketAdapter.cs`
- Create: `src/SonicDesktopRelay.Signaling/SignalingConnection.cs`
- Test: `tests/SonicDesktopRelay.Signaling.Tests/SignalingConnectionTests.cs`

**Interfaces:**
- Consumes: `SignalingEnvelope`, `SignalingMessageTypes` (Task 7).
- Produces:
  - `interface IWebSocketAdapter : IAsyncDisposable { Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct); Task SendAsync(string json, CancellationToken ct); Task<string?> ReceiveAsync(CancellationToken ct); }` — `ReceiveAsync` returns `null` when the socket closes.
  - `interface ISignalingConnection : IAsyncDisposable { event Action<SignalingEnvelope>? FrameReceived; SignalingState State { get; } event Action<SignalingState>? StateChanged; Task StartAsync(Guid sessionId, CancellationToken ct); Task SendAsync(string type, Guid? to, object? payload, CancellationToken ct); }`
  - `enum SignalingState { Disconnected, Connecting, Connected, Reconnecting, Terminated }`
  - `sealed class SignalingConnection(IWebSocketAdapter socket, BackendSettings settings, Func<CancellationToken, Task<string>> tokenProvider) : ISignalingConnection`

- [ ] **Step 1: Write the failing tests**

`tests/SonicDesktopRelay.Signaling.Tests/SignalingConnectionTests.cs`:

```csharp
using System.Threading.Channels;
using SonicDesktopRelay.Core;
using Xunit;

namespace SonicDesktopRelay.Signaling.Tests;

public sealed class SignalingConnectionTests
{
    private static readonly Guid SessionId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
    private static readonly BackendSettings Settings = BackendSettings.TryParse("https://relay.example.com")!;

    [Fact]
    public async Task Starting_connects_to_the_session_signaling_uri_with_the_token()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));

        await connection.StartAsync(SessionId, CancellationToken.None);

        Assert.Equal($"wss://relay.example.com/ws/signaling?sessionId={SessionId}", socket.ConnectedTo!.ToString());
        Assert.Equal("jwt", socket.AccessToken);
        Assert.Equal(SignalingState.Connected, connection.State);
    }

    [Fact]
    public async Task Received_frames_are_surfaced_as_envelopes()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        var received = new List<SignalingEnvelope>();
        connection.FrameReceived += received.Add;
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Push("""{"type":"session.joined","payload":{"role":"publisher"}}""");
        await socket.DrainAsync();

        Assert.Single(received);
        Assert.Equal("session.joined", received[0].Type);
    }

    [Fact]
    public async Task An_unparseable_frame_is_dropped_without_closing_the_connection()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        var received = new List<SignalingEnvelope>();
        connection.FrameReceived += received.Add;
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Push("{ garbage");
        socket.Push("""{"type":"ping"}""");
        await socket.DrainAsync();

        Assert.Single(received);
        Assert.Equal(SignalingState.Connected, connection.State);
    }

    [Fact]
    public async Task Session_ended_is_terminal_and_stops_the_connection()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Push("""{"type":"session.ended","payload":{}}""");
        await socket.DrainAsync();

        Assert.Equal(SignalingState.Terminated, connection.State);
    }

    [Fact]
    public async Task Sending_writes_the_outbound_envelope_shape()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        await connection.StartAsync(SessionId, CancellationToken.None);
        var to = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96401");

        await connection.SendAsync(SignalingMessageTypes.ViewerReady, to, new { }, CancellationToken.None);

        Assert.Contains("\"type\":\"viewer.ready\"", socket.Sent[0]);
        Assert.Contains(to.ToString(), socket.Sent[0]);
    }

    [Fact]
    public async Task A_dropped_socket_moves_to_reconnecting_and_reconnects()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"))
        {
            ReconnectDelay = TimeSpan.Zero
        };
        var states = new List<SignalingState>();
        connection.StateChanged += states.Add;
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Close();
        await socket.WaitForReconnectAsync();

        Assert.Contains(SignalingState.Reconnecting, states);
        Assert.Equal(2, socket.ConnectCount);
    }

    private sealed class FakeWebSocket : IWebSocketAdapter
    {
        private Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
        private TaskCompletionSource _reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri? ConnectedTo { get; private set; }

        public string? AccessToken { get; private set; }

        public int ConnectCount { get; private set; }

        public List<string> Sent { get; } = [];

        public Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct)
        {
            ConnectedTo = uri;
            AccessToken = accessToken;
            ConnectCount++;
            if (ConnectCount > 1)
            {
                _inbound = Channel.CreateUnbounded<string?>();
                _reconnected.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task SendAsync(string json, CancellationToken ct)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken ct) =>
            await _inbound.Reader.ReadAsync(ct);

        public void Push(string frame) => _inbound.Writer.TryWrite(frame);

        public void Close() => _inbound.Writer.TryWrite(null);

        /// <summary>Yields until the receive loop has drained everything pushed so far.</summary>
        public async Task DrainAsync()
        {
            while (_inbound.Reader.Count > 0) await Task.Delay(1);
            await Task.Delay(20);
        }

        public Task WaitForReconnectAsync() => _reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Reference Core from the test project**

```bash
dotnet add tests/SonicDesktopRelay.Signaling.Tests reference src/SonicDesktopRelay.Core
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SignalingConnectionTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 4: Implement the adapter contracts**

`src/SonicDesktopRelay.Signaling/IWebSocketAdapter.cs`:

```csharp
namespace SonicDesktopRelay.Signaling;

/// <summary>
/// The socket, behind a seam. ClientWebSocket cannot be driven from a test without a real
/// listener, and what needs testing is the reconnect and dispatch logic above it.
/// </summary>
public interface IWebSocketAdapter : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct);

    Task SendAsync(string json, CancellationToken ct);

    /// <summary>Returns the next text frame, or null once the socket has closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken ct);
}
```

`src/SonicDesktopRelay.Signaling/ISignalingConnection.cs`:

```csharp
namespace SonicDesktopRelay.Signaling;

public enum SignalingState
{
    Disconnected,
    Connecting,
    Connected,

    /// <summary>
    /// The socket dropped but the session is still live. The server holds the participant for
    /// its grace period, so peers are told to wait rather than to tear anything down.
    /// </summary>
    Reconnecting,

    /// <summary>The session is over. No further attempt will be made.</summary>
    Terminated
}

public interface ISignalingConnection : IAsyncDisposable
{
    SignalingState State { get; }

    event Action<SignalingEnvelope>? FrameReceived;

    event Action<SignalingState>? StateChanged;

    Task StartAsync(Guid sessionId, CancellationToken ct);

    Task SendAsync(string type, Guid? to, object? payload, CancellationToken ct);
}
```

- [ ] **Step 5: Implement the connection**

`src/SonicDesktopRelay.Signaling/SignalingConnection.cs`:

```csharp
using SonicDesktopRelay.Core;

namespace SonicDesktopRelay.Signaling;

public sealed class SignalingConnection(
    IWebSocketAdapter socket,
    BackendSettings settings,
    Func<CancellationToken, Task<string>> tokenProvider) : ISignalingConnection
{
    private readonly CancellationTokenSource _stopping = new();
    private SignalingState _state = SignalingState.Disconnected;
    private Task? _receiveLoop;
    private Guid _sessionId;

    /// <summary>Kept settable so tests do not have to wait out a real backoff.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    public SignalingState State => _state;

    public event Action<SignalingEnvelope>? FrameReceived;

    public event Action<SignalingState>? StateChanged;

    public async Task StartAsync(Guid sessionId, CancellationToken ct)
    {
        _sessionId = sessionId;
        SetState(SignalingState.Connecting);
        await ConnectAsync(ct);
        SetState(SignalingState.Connected);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_stopping.Token), CancellationToken.None);
    }

    public Task SendAsync(string type, Guid? to, object? payload, CancellationToken ct) =>
        socket.SendAsync(SignalingEnvelope.Serializer.ToJson(type, to, payload), ct);

    private async Task ConnectAsync(CancellationToken ct)
    {
        var token = await tokenProvider(ct);
        await socket.ConnectAsync(settings.SignalingUri(_sessionId), token, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _state != SignalingState.Terminated)
        {
            string? frame;
            try
            {
                frame = await socket.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (frame is null)
            {
                if (!await TryReconnectAsync(ct)) return;
                continue;
            }

            var envelope = SignalingEnvelope.Serializer.TryParse(frame);
            if (envelope is null) continue;

            if (envelope.Type == SignalingMessageTypes.SessionEnded)
            {
                FrameReceived?.Invoke(envelope);
                SetState(SignalingState.Terminated);
                return;
            }

            FrameReceived?.Invoke(envelope);
        }
    }

    private async Task<bool> TryReconnectAsync(CancellationToken ct)
    {
        // The session outlives a dropped socket: the server holds the participant for its
        // grace period and reports a reconnect as participant.reconnected rather than a new
        // join. Terminal conditions — session.ended, or the app stopping — never get here.
        if (_state == SignalingState.Terminated || ct.IsCancellationRequested) return false;

        SetState(SignalingState.Reconnecting);
        try
        {
            if (ReconnectDelay > TimeSpan.Zero) await Task.Delay(ReconnectDelay, ct);
            await ConnectAsync(ct);
            SetState(SignalingState.Connected);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void SetState(SignalingState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }
        await socket.DisposeAsync();
        _stopping.Dispose();
    }
}
```

- [ ] **Step 6: Implement the real adapter**

`src/SonicDesktopRelay.Signaling/ClientWebSocketAdapter.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;

namespace SonicDesktopRelay.Signaling;

public sealed class ClientWebSocketAdapter : IWebSocketAdapter
{
    private ClientWebSocket? _socket;

    public async Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct)
    {
        if (_socket is not null) await DisposeSocketAsync();
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        await socket.ConnectAsync(uri, ct);
        _socket = socket;
    }

    public Task SendAsync(string json, CancellationToken ct)
    {
        var socket = _socket ?? throw new InvalidOperationException("The signaling socket is not connected.");
        return socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, ct)
            .AsTask();
    }

    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var socket = _socket ?? throw new InvalidOperationException("The signaling socket is not connected.");
        var buffer = new byte[8192];
        var builder = new StringBuilder();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (WebSocketException)
            {
                // An abrupt close is reported the same way as a graceful one: null means
                // "the socket is gone", and deciding what to do about it belongs upstairs.
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close) return null;
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) return builder.ToString();
        }
    }

    public async ValueTask DisposeAsync() => await DisposeSocketAsync();

    private async Task DisposeSocketAsync()
    {
        if (_socket is null) return;
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
        catch (WebSocketException)
        {
        }
        _socket.Dispose();
        _socket = null;
    }
}
```

- [ ] **Step 7: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SignalingConnectionTests"`
Expected: PASS, 6 tests.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(signaling): authenticated websocket connection with grace-period reconnect"
```

---

### Task 9: The session state machine

**Files:**
- Create: `src/SonicDesktopRelay.Presentation/SonicDesktopRelay.Presentation.csproj`
- Create: `src/SonicDesktopRelay.Presentation/SessionSnapshot.cs`
- Create: `src/SonicDesktopRelay.Presentation/SessionRuntime.cs`
- Create: `tests/SonicDesktopRelay.Presentation.Tests/SonicDesktopRelay.Presentation.Tests.csproj`
- Create: `tests/SonicDesktopRelay.Presentation.Tests/SessionRuntimeTests.cs`

**Interfaces:**
- Consumes: `SessionApiClient` shapes (Task 6) behind a Core-side interface; `ISignalingConnection` (Task 8).
- Produces:
  - `enum SessionMode { Idle, Sharing, Watching }`
  - `enum SessionPhase { Idle, Preparing, Sharing, Joining, Watching, Ending, Failed }`
  - `sealed record SessionSnapshot(SessionPhase Phase, string? Code, Guid? SessionId, int ViewerCount, SignalingState Signaling, string? Error)`
  - `sealed class SessionRuntime(ISessionApi api, Func<ISignalingConnection> connectionFactory)` with `SessionSnapshot Snapshot`, `event Action<SessionSnapshot>? Changed`, `Task StartSharingAsync(int maxViewers, CancellationToken)`, `Task StartWatchingAsync(string code, CancellationToken)`, `Task StopAsync(CancellationToken)`
  - `interface ISessionApi` in Presentation mirroring the four session calls.

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/SonicDesktopRelay.Presentation -n SonicDesktopRelay.Presentation
dotnet new xunit -o tests/SonicDesktopRelay.Presentation.Tests -n SonicDesktopRelay.Presentation.Tests
rm src/SonicDesktopRelay.Presentation/Class1.cs tests/SonicDesktopRelay.Presentation.Tests/UnitTest1.cs
dotnet sln add src/SonicDesktopRelay.Presentation tests/SonicDesktopRelay.Presentation.Tests
dotnet add src/SonicDesktopRelay.Presentation reference src/SonicDesktopRelay.Core src/SonicDesktopRelay.Signaling
dotnet add tests/SonicDesktopRelay.Presentation.Tests reference src/SonicDesktopRelay.Presentation
```

- [ ] **Step 2: Write the failing tests**

`tests/SonicDesktopRelay.Presentation.Tests/SessionRuntimeTests.cs`:

```csharp
using SonicDesktopRelay.Signaling;
using Xunit;

namespace SonicDesktopRelay.Presentation.Tests;

public sealed class SessionRuntimeTests
{
    private static readonly Guid SessionId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    [Fact]
    public void A_fresh_runtime_is_idle()
    {
        var runtime = new SessionRuntime(new FakeSessionApi(), () => new FakeConnection());

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
        Assert.Null(runtime.Snapshot.Code);
    }

    [Fact]
    public async Task Sharing_creates_the_session_and_exposes_its_code()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartSharingAsync(3, CancellationToken.None);

        Assert.Equal(SessionPhase.Sharing, runtime.Snapshot.Phase);
        Assert.Equal("AB12CD", runtime.Snapshot.Code);
        Assert.Equal(SessionId, runtime.Snapshot.SessionId);
        Assert.Equal(3, api.RequestedMaxViewers);
    }

    [Fact]
    public async Task Sharing_passes_through_preparing()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        var phases = new List<SessionPhase>();
        runtime.Changed += snapshot => phases.Add(snapshot.Phase);

        await runtime.StartSharingAsync(3, CancellationToken.None);

        Assert.Equal([SessionPhase.Preparing, SessionPhase.Sharing], phases);
    }

    [Fact]
    public async Task Watching_joins_with_the_code_and_reaches_watching()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartWatchingAsync("ab12cd", CancellationToken.None);

        Assert.Equal(SessionPhase.Watching, runtime.Snapshot.Phase);
        Assert.Equal("ab12cd", api.JoinedWithCode);
    }

    [Fact]
    public async Task A_join_failure_lands_in_failed_with_the_error_code()
    {
        var api = new FakeSessionApi { JoinFailureCode = "invalid_code" };
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartWatchingAsync("ZZZZZZ", CancellationToken.None);

        Assert.Equal(SessionPhase.Failed, runtime.Snapshot.Phase);
        Assert.Equal("invalid_code", runtime.Snapshot.Error);
    }

    [Fact]
    public async Task Failing_leaves_no_session_behind()
    {
        var api = new FakeSessionApi { JoinFailureCode = "device_type_not_allowed" };
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartWatchingAsync("AB12CD", CancellationToken.None);

        Assert.Null(runtime.Snapshot.SessionId);
    }

    [Fact]
    public async Task Sharing_while_already_sharing_is_refused_rather_than_stacking_sessions()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        await runtime.StartSharingAsync(3, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.StartWatchingAsync("AB12CD", CancellationToken.None));

        Assert.Equal(SessionPhase.Sharing, runtime.Snapshot.Phase);
        Assert.Equal(1, api.CreateCalls);
    }

    [Fact]
    public async Task Stopping_a_shared_session_ends_it_and_returns_to_idle()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        await runtime.StartSharingAsync(3, CancellationToken.None);

        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
        Assert.Equal(1, api.EndCalls);
        Assert.Null(runtime.Snapshot.Code);
    }

    [Fact]
    public async Task Stopping_a_watched_session_does_not_end_it_for_everyone()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        await runtime.StartWatchingAsync("AB12CD", CancellationToken.None);

        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
        // Only the publishing device may end a session; a viewer leaving just disconnects.
        Assert.Equal(0, api.EndCalls);
    }

    [Fact]
    public async Task A_session_ended_frame_returns_a_viewer_to_idle()
    {
        var api = new FakeSessionApi();
        var connection = new FakeConnection();
        var runtime = new SessionRuntime(api, () => connection);
        await runtime.StartWatchingAsync("AB12CD", CancellationToken.None);

        connection.Emit(SignalingMessageTypes.SessionEnded);

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
    }

    [Fact]
    public async Task Viewers_joining_and_leaving_move_the_viewer_count()
    {
        var api = new FakeSessionApi();
        var connection = new FakeConnection();
        var runtime = new SessionRuntime(api, () => connection);
        await runtime.StartSharingAsync(3, CancellationToken.None);

        connection.Emit(SignalingMessageTypes.SessionJoined);
        connection.Emit(SignalingMessageTypes.SessionJoined);
        connection.Emit(SignalingMessageTypes.SessionLeft);

        Assert.Equal(1, runtime.Snapshot.ViewerCount);
    }

    private sealed class FakeSessionApi : ISessionApi
    {
        public int CreateCalls { get; private set; }

        public int EndCalls { get; private set; }

        public int RequestedMaxViewers { get; private set; }

        public string? JoinedWithCode { get; private set; }

        public string? JoinFailureCode { get; init; }

        public Task<CreatedSession> CreateScreenShareAsync(int maxViewers, CancellationToken ct)
        {
            CreateCalls++;
            RequestedMaxViewers = maxViewers;
            return Task.FromResult(new CreatedSession(SessionId, "AB12CD"));
        }

        public Task<Guid> JoinAsync(string code, CancellationToken ct)
        {
            JoinedWithCode = code;
            if (JoinFailureCode is not null)
                throw new SessionApiFailure(JoinFailureCode, "Join refused.");
            return Task.FromResult(SessionId);
        }

        public Task EndAsync(Guid sessionId, CancellationToken ct)
        {
            EndCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnection : ISignalingConnection
    {
        public SignalingState State { get; private set; } = SignalingState.Disconnected;

        public event Action<SignalingEnvelope>? FrameReceived;

        public event Action<SignalingState>? StateChanged;

        public Task StartAsync(Guid sessionId, CancellationToken ct)
        {
            State = SignalingState.Connected;
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public Task SendAsync(string type, Guid? to, object? payload, CancellationToken ct) =>
            Task.CompletedTask;

        public void Emit(string type) =>
            FrameReceived?.Invoke(new SignalingEnvelope(type, null, null, null, null, null, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SessionRuntimeTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 4: Implement the snapshot and contracts**

`src/SonicDesktopRelay.Presentation/SessionSnapshot.cs`:

```csharp
using SonicDesktopRelay.Signaling;

namespace SonicDesktopRelay.Presentation;

public enum SessionPhase
{
    Idle,
    Preparing,
    Sharing,
    Joining,
    Watching,
    Ending,
    Failed
}

/// <summary>
/// Everything the UI is allowed to know, as one immutable value. Screens bind to this rather
/// than reaching into the runtime, so no page can hold a stale private copy of the state.
/// </summary>
public sealed record SessionSnapshot(
    SessionPhase Phase,
    string? Code,
    Guid? SessionId,
    int ViewerCount,
    SignalingState Signaling,
    string? Error)
{
    public static SessionSnapshot Idle { get; } =
        new(SessionPhase.Idle, null, null, 0, SignalingState.Disconnected, null);

    public bool IsBusy => Phase is SessionPhase.Preparing or SessionPhase.Joining or SessionPhase.Ending;
}

public sealed record CreatedSession(Guid SessionId, string Code);

/// <summary>
/// A backend refusal, carrying the API's machine-readable code so the runtime can put it in
/// the snapshot without the presentation layer depending on HTTP types.
/// </summary>
public sealed class SessionApiFailure(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ISessionApi
{
    Task<CreatedSession> CreateScreenShareAsync(int maxViewers, CancellationToken ct);

    Task<Guid> JoinAsync(string code, CancellationToken ct);

    Task EndAsync(Guid sessionId, CancellationToken ct);
}
```

- [ ] **Step 5: Implement the runtime**

`src/SonicDesktopRelay.Presentation/SessionRuntime.cs`:

```csharp
using SonicDesktopRelay.Signaling;

namespace SonicDesktopRelay.Presentation;

/// <summary>
/// The single answer to "what is this app doing right now". One machine covers both roles,
/// because in this phase a device shares or watches, never both — and one machine is what
/// keeps the Diagnostics page honest instead of inventing a second version of the truth.
/// </summary>
public sealed class SessionRuntime(ISessionApi api, Func<ISignalingConnection> connectionFactory)
{
    private ISignalingConnection? _connection;
    private bool _isOwner;

    public SessionSnapshot Snapshot { get; private set; } = SessionSnapshot.Idle;

    public event Action<SessionSnapshot>? Changed;

    public async Task StartSharingAsync(int maxViewers, CancellationToken ct)
    {
        RequireIdle();
        Publish(Snapshot with { Phase = SessionPhase.Preparing, Error = null });
        try
        {
            var created = await api.CreateScreenShareAsync(maxViewers, ct);
            _isOwner = true;
            await AttachAsync(created.SessionId, ct);
            Publish(new SessionSnapshot(SessionPhase.Sharing, created.Code, created.SessionId, 0,
                _connection!.State, null));
        }
        catch (SessionApiFailure failure)
        {
            await FailAsync(failure.Code);
        }
    }

    public async Task StartWatchingAsync(string code, CancellationToken ct)
    {
        RequireIdle();
        Publish(Snapshot with { Phase = SessionPhase.Joining, Error = null });
        try
        {
            var sessionId = await api.JoinAsync(code, ct);
            _isOwner = false;
            await AttachAsync(sessionId, ct);
            Publish(new SessionSnapshot(SessionPhase.Watching, null, sessionId, 0, _connection!.State, null));
        }
        catch (SessionApiFailure failure)
        {
            await FailAsync(failure.Code);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (Snapshot.Phase == SessionPhase.Idle) return;
        Publish(Snapshot with { Phase = SessionPhase.Ending });

        // Only the publishing device may end a session for everyone; a viewer leaving simply
        // drops its own connection, and calling end as a viewer would be a 403 at best.
        if (_isOwner && Snapshot.SessionId is { } sessionId)
        {
            try
            {
                await api.EndAsync(sessionId, ct);
            }
            catch (SessionApiFailure)
            {
                // The session may already be over. Stopping locally must still succeed.
            }
        }

        await DetachAsync();
        Publish(SessionSnapshot.Idle);
    }

    private async Task AttachAsync(Guid sessionId, CancellationToken ct)
    {
        var connection = connectionFactory();
        connection.FrameReceived += OnFrame;
        _connection = connection;
        await connection.StartAsync(sessionId, ct);

        // Subscribed only after the initial connect: the state change that connecting itself
        // produces is already reflected in the snapshot the caller is about to publish, and
        // reacting to it here would emit a redundant intermediate snapshot to the UI.
        connection.StateChanged += OnSignalingState;
    }

    private async Task DetachAsync()
    {
        if (_connection is null) return;
        _connection.FrameReceived -= OnFrame;
        _connection.StateChanged -= OnSignalingState;
        await _connection.DisposeAsync();
        _connection = null;
        _isOwner = false;
    }

    private void OnFrame(SignalingEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case SignalingMessageTypes.SessionJoined when Snapshot.Phase == SessionPhase.Sharing:
                Publish(Snapshot with { ViewerCount = Snapshot.ViewerCount + 1 });
                break;
            case SignalingMessageTypes.SessionLeft when Snapshot.Phase == SessionPhase.Sharing:
                Publish(Snapshot with { ViewerCount = Math.Max(0, Snapshot.ViewerCount - 1) });
                break;
            case SignalingMessageTypes.SessionEnded:
                _ = DetachAsync();
                Publish(SessionSnapshot.Idle);
                break;
        }
    }

    private void OnSignalingState(SignalingState state) => Publish(Snapshot with { Signaling = state });

    private async Task FailAsync(string code)
    {
        await DetachAsync();
        Publish(new SessionSnapshot(SessionPhase.Failed, null, null, 0, SignalingState.Disconnected, code));
    }

    private void RequireIdle()
    {
        if (Snapshot.Phase is SessionPhase.Idle or SessionPhase.Failed) return;
        throw new InvalidOperationException(
            $"Cannot start a session while the runtime is {Snapshot.Phase}. Stop the current one first.");
    }

    private void Publish(SessionSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(snapshot);
    }
}
```

- [ ] **Step 6: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SessionRuntimeTests"`
Expected: PASS, 11 tests.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(presentation): one session state machine for sharing and watching"
```

---

### Task 10: Avalonia shell

**Files:**
- Create: `src/SonicDesktopRelay.App/SonicDesktopRelay.App.csproj`, `App.axaml`, `App.axaml.cs`, `Program.cs`
- Create: `src/SonicDesktopRelay.App/Styles/Tokens.axaml`
- Create: `src/SonicDesktopRelay.App/Views/MainWindow.axaml`, `MainWindow.axaml.cs`
- Create: `src/SonicDesktopRelay.App/Views/HomeView.axaml`, `ShareView.axaml`, `WatchView.axaml`, `SettingsView.axaml`, `DiagnosticsView.axaml` (with their `.axaml.cs`)
- Create: `src/SonicDesktopRelay.Presentation/MainWindowViewModel.cs` — in `Presentation`, **not** `App`, so it is testable without an Avalonia UI thread
- Create: `src/SonicDesktopRelay.App/AppComposition.cs`
- Test: `tests/SonicDesktopRelay.Presentation.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `SessionRuntime`, `SessionSnapshot` (Task 9); `DeviceIdentityService` (Task 5); `BackendSettings` (Task 2); `SessionApiClient` (Task 6); `SignalingConnection` (Task 8).
- Produces: a runnable `dotnet run --project src/SonicDesktopRelay.App` that opens the five-page shell. `MainWindowViewModel` exposes `Page CurrentPage`, `bool CanShare`, `bool CanWatch`, `string StatusText`, and `void Apply(SessionSnapshot)`.

- [ ] **Step 1: Create the app project**

```bash
dotnet new install Avalonia.Templates
dotnet new avalonia.app -o src/SonicDesktopRelay.App -n SonicDesktopRelay.App
dotnet sln add src/SonicDesktopRelay.App
dotnet add src/SonicDesktopRelay.App reference src/SonicDesktopRelay.Core src/SonicDesktopRelay.ApiClient src/SonicDesktopRelay.Signaling src/SonicDesktopRelay.Presentation
```

Set these properties in `src/SonicDesktopRelay.App/SonicDesktopRelay.App.csproj`:

```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AssemblyTitle>SonicDesktopRelay</AssemblyTitle>
    <Description>Share a Windows screen with other Windows machines over SonicRelay.</Description>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
```

Pin `Tmds.DBus.Protocol` to `0.21.3` if the Avalonia template pulls a transitive `0.21.2`
(advisory GHSA-xrw6-gwf8-vvr9). It is Linux-only and harmless on Windows, but
`TreatWarningsAsErrors` will surface the advisory.

- [ ] **Step 2: Write the failing view-model test**

`tests/SonicDesktopRelay.Presentation.Tests/MainWindowViewModelTests.cs` — put the view model
in `Presentation`, not `App`, so it is testable without a UI thread:

```csharp
using SonicDesktopRelay.Signaling;
using Xunit;

namespace SonicDesktopRelay.Presentation.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void An_idle_app_can_start_either_role()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(SessionSnapshot.Idle);

        Assert.True(viewModel.CanShare);
        Assert.True(viewModel.CanWatch);
    }

    [Fact]
    public void While_sharing_neither_role_can_be_started_again()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Sharing, "AB12CD",
            Guid.NewGuid(), 2, SignalingState.Connected, null));

        Assert.False(viewModel.CanShare);
        Assert.False(viewModel.CanWatch);
    }

    [Fact]
    public void While_busy_neither_role_can_be_started()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Joining, null, null, 0,
            SignalingState.Connecting, null));

        Assert.False(viewModel.CanShare);
        Assert.False(viewModel.CanWatch);
    }

    [Fact]
    public void A_failure_is_reported_in_words_rather_than_as_an_error_code()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Failed, null, null, 0,
            SignalingState.Disconnected, "device_type_not_allowed"));

        Assert.Equal("This session only accepts Windows computers running SonicDesktopRelay.",
            viewModel.StatusText);
    }

    [Fact]
    public void An_invalid_code_is_reported_without_hinting_at_which_part_was_wrong()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Failed, null, null, 0,
            SignalingState.Disconnected, "invalid_code"));

        Assert.Equal("That code is not valid, or the session has ended.", viewModel.StatusText);
    }

    [Fact]
    public void An_unrecognised_error_code_still_produces_a_usable_message()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Failed, null, null, 0,
            SignalingState.Disconnected, "something_new"));

        Assert.Equal("Something went wrong. Try again.", viewModel.StatusText);
    }

    [Fact]
    public void Sharing_reports_the_viewer_count()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Sharing, "AB12CD",
            Guid.NewGuid(), 2, SignalingState.Connected, null));

        Assert.Equal("Sharing — 2 watching", viewModel.StatusText);
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: compile error — `MainWindowViewModel` does not exist.

- [ ] **Step 4: Implement the view model**

`src/SonicDesktopRelay.Presentation/MainWindowViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SonicDesktopRelay.Presentation;

public enum Page
{
    Home,
    Share,
    Watch,
    Settings,
    Diagnostics
}

/// <summary>
/// The shell's projection of <see cref="SessionSnapshot"/>. It holds no state of its own
/// beyond the selected page: everything else is derived, so the UI cannot disagree with the
/// runtime about what is happening.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private SessionSnapshot _snapshot = SessionSnapshot.Idle;
    private Page _currentPage = Page.Home;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Page CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage == value) return;
            _currentPage = value;
            Raise();
        }
    }

    public bool CanShare => _snapshot.Phase is SessionPhase.Idle or SessionPhase.Failed;

    public bool CanWatch => CanShare;

    public bool CanStop => _snapshot.Phase is SessionPhase.Sharing or SessionPhase.Watching;

    public string? Code => _snapshot.Code;

    public string StatusText => _snapshot.Phase switch
    {
        SessionPhase.Idle => "Ready",
        SessionPhase.Preparing => "Preparing to share…",
        SessionPhase.Sharing => $"Sharing — {_snapshot.ViewerCount} watching",
        SessionPhase.Joining => "Joining…",
        SessionPhase.Watching => "Watching",
        SessionPhase.Ending => "Ending…",
        SessionPhase.Failed => FailureText(_snapshot.Error),
        _ => "Ready"
    };

    public void Apply(SessionSnapshot snapshot)
    {
        _snapshot = snapshot;
        Raise(nameof(CanShare));
        Raise(nameof(CanWatch));
        Raise(nameof(CanStop));
        Raise(nameof(Code));
        Raise(nameof(StatusText));
    }

    // The API's codes are deliberately vague about *why* a code failed, and so is this: a
    // message that distinguished "expired" from "wrong" would help someone guessing codes.
    private static string FailureText(string? code) => code switch
    {
        "device_type_not_allowed" => "This session only accepts Windows computers running SonicDesktopRelay.",
        "invalid_code" => "That code is not valid, or the session has ended.",
        "not_paired" => "That code is not valid, or the session has ended.",
        "session_full" => "That session is already full.",
        _ => "Something went wrong. Try again."
    };

    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
```

The viewer-limit case: the backend answers `409` with `{ "error": "Session viewer limit
reached." }` and no `code` field, so `SessionApiFailure` must be constructed with the literal
`"session_full"` when the status is `409`. Do that in the adapter written in Step 6.

- [ ] **Step 5: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: PASS, 7 tests.

- [ ] **Step 6: Write the composition root**

`src/SonicDesktopRelay.App/AppComposition.cs`:

```csharp
using System.Net;
using SonicDesktopRelay.ApiClient;
using SonicDesktopRelay.Core;
using SonicDesktopRelay.Core.Identity;
using SonicDesktopRelay.Presentation;
using SonicDesktopRelay.Signaling;

namespace SonicDesktopRelay.App;

/// <summary>
/// The one place that knows about concrete implementations. Everything below this file talks
/// to interfaces, which is what lets the whole app be tested without Windows or a network.
/// </summary>
public sealed class AppComposition
{
    public AppComposition(BackendSettings settings)
    {
        Settings = settings;
        var store = new FileDeviceCredentialStore(FileDeviceCredentialStore.DefaultPath);
        var deviceHttp = new HttpClient { BaseAddress = settings.BaseAddress };
        var deviceApi = new DeviceApiClient(deviceHttp);
        Identity = new DeviceIdentityService(store, deviceApi, TimeProvider.System);

        var sessionHttp = new HttpClient(new BearerTokenHandler(Identity, Environment.MachineName))
        {
            BaseAddress = settings.BaseAddress
        };
        Runtime = new SessionRuntime(
            new SessionApiAdapter(new SessionApiClient(sessionHttp)),
            () => new SignalingConnection(
                new ClientWebSocketAdapter(),
                settings,
                ct => Identity.GetAccessTokenAsync(Environment.MachineName, ct)));
    }

    public BackendSettings Settings { get; }

    public DeviceIdentityService Identity { get; }

    public SessionRuntime Runtime { get; }
}

/// <summary>Attaches the DeviceBearer token to every call, refreshing it before it lapses.</summary>
internal sealed class BearerTokenHandler(DeviceIdentityService identity, string deviceName)
    : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await identity.GetAccessTokenAsync(deviceName, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        // The proactive refresh missed — a clock skew or a credential rotation elsewhere.
        // One retry with a fresh token, then the failure is real.
        identity.Invalidate();
        var retryToken = await identity.GetAccessTokenAsync(deviceName, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", retryToken);
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Maps HTTP failures onto the presentation layer's own failure type.</summary>
internal sealed class SessionApiAdapter(SessionApiClient client) : ISessionApi
{
    public async Task<CreatedSession> CreateScreenShareAsync(int maxViewers, CancellationToken ct)
    {
        try
        {
            var session = await client.CreateScreenShareAsync(maxViewers, ct);
            return new CreatedSession(session.Id, session.Code
                ?? throw new SessionApiFailure("no_code", "The backend created a session without a code."));
        }
        catch (ApiException e)
        {
            throw Translate(e);
        }
    }

    public async Task<Guid> JoinAsync(string code, CancellationToken ct)
    {
        try
        {
            return (await client.JoinAsync(code, ct)).Id;
        }
        catch (ApiException e)
        {
            throw Translate(e);
        }
    }

    public async Task EndAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await client.EndAsync(sessionId, ct);
        }
        catch (ApiException e)
        {
            throw Translate(e);
        }
    }

    // The viewer-limit refusal is the one failure the API answers without a machine-readable
    // code, so the status is what names it.
    private static SessionApiFailure Translate(ApiException e) =>
        new(e.ErrorCode ?? (e.StatusCode == HttpStatusCode.Conflict ? "session_full" : "unknown"), e.Message);
}
```

Because `SessionApiAdapter` needs `SessionApiFailure`, add the project reference:

```bash
dotnet add src/SonicDesktopRelay.App reference src/SonicDesktopRelay.Presentation
```

(already added in Step 1; verify rather than duplicate).

- [ ] **Step 7: Build the five pages**

`src/SonicDesktopRelay.App/Views/MainWindow.axaml` hosts a left navigation rail with the five
entries bound to `CurrentPage`, and a content area. Each page is a `UserControl`:

- **HomeView** — the current `StatusText`, and two buttons: "Share my screen" (enabled by
  `CanShare`) and "Watch a screen" (`CanWatch`), plus "Stop" (`CanStop`).
- **ShareView** — a monitor picker showing a single disabled placeholder entry reading
  "Primary monitor (capture arrives in the next phase)", the session `Code` in a large
  selectable font with a copy button, and the viewer count.
- **WatchView** — a six-character code entry that accepts letters and digits, uppercases as
  the user types, and a "Watch" button enabled only at exactly six characters.
- **SettingsView** — the backend address bound to a text box validated with
  `BackendSettings.TryParse`, and the device name (defaulting to `Environment.MachineName`).
- **DiagnosticsView** — a read-only list of the last snapshots: phase, signaling state,
  session id and viewer count. No media metrics exist yet; the page is the surface the later
  phases fill in.

Every visual value comes from `Styles/Tokens.axaml`; components hard-code no colors, spacing
or font sizes.

- [ ] **Step 8: Verify the app runs**

Run: `dotnet run --project src/SonicDesktopRelay.App`
Expected: the window opens on Home. With a reachable backend configured in Settings, "Share my
screen" produces a six-character code, and a second machine entering that code reaches
"Watching" with the first machine reporting "Sharing — 1 watching".

- [ ] **Step 9: Run the whole suite**

Run: `dotnet test SonicDesktopRelay.sln`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat(app): Avalonia shell with home, share, watch, settings and diagnostics"
```

---

## Done when

- `dotnet test SonicDesktopRelay.sln` passes.
- A first run on a clean machine registers a `windows_desktop` device with no login prompt, and the credential file on disk contains no readable secret.
- One machine creates a `screen_share` session and shows a six-character code; a second machine enters that code and both hold an open signaling socket, with the sharer reporting one viewer.
- Killing the network briefly moves the signaling state to `Reconnecting` and back to `Connected` without either side losing its session.
- Ending the session from the sharer returns both machines to Idle.
- No project references `windows_SonicRelay`, and no capture, encoder or peer connection exists yet.
