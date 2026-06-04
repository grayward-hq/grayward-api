using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Tests.Integration;


public class AuthControllerTests : IClassFixture<VulnWatchWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly VulnWatchWebAppFactory _factory;

    public AuthControllerTests(VulnWatchWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // POST /api/auth/register ─────────────────────────────────────────────────

    [Fact]
    public async Task POST_auth_register_ValidPayload_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"new_{Guid.NewGuid():N}@example.com",
            password = "P@ssw0rd123!",
            firstName = "Tony",
            lastName = "Dev"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task POST_auth_register_DuplicateEmail_Returns409()
    {
        var email = $"dup_{Guid.NewGuid():N}@example.com";
        var payload = new { email, password = "P@ssw0rd123!", firstName = "Tony", lastName = "Dev" };

        var first = await _client.PostAsJsonAsync("/api/auth/register", payload);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task POST_auth_register_WeakPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"weak_{Guid.NewGuid():N}@example.com",
            password = "123",
            firstName = "Tony",
            lastName = "Dev"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_auth_register_MissingEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            password = "P@ssw0rd123!",
            firstName = "Tony",
            lastName = "Dev"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // POST /api/auth/login ────────────────────────────────────────────────────

    [Fact]
    public async Task POST_auth_login_ValidCredentials_Returns200WithToken()
    {
        var email = $"login_{Guid.NewGuid():N}@example.com";
        var password = "P@ssw0rd123!";
        await _factory.CreateAuthenticatedUserAsync(email, password);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        body.GetProperty("value").GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task POST_auth_login_WrongPassword_Returns401()
    {
        var email = $"badpw_{Guid.NewGuid():N}@example.com";
        await _factory.CreateAuthenticatedUserAsync(email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "WrongPassword!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_auth_login_UnknownEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "ghost@example.com",
            password = "P@ssw0rd123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // POST /api/auth/verify ───────────────────────────────────────────────────

    [Fact]
    public async Task GET_auth_verify_UnknownUser_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/auth/verify?userId={Guid.NewGuid()}&token=invalid-token");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // POST /api/auth/resend ───────────────────────────────────────────────────

    [Fact]
    public async Task POST_auth_resend_UnknownEmail_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/resend", new
        {
            email = "nobody@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // src/Tests/Web/AuthIntegrationTests.cs — add to AuthControllerTests

    [Fact]
    public async Task POST_auth_refreshToken_ValidToken_Returns200WithNewTokens()
    {
        var email = $"refresh_{Guid.NewGuid():N}@example.com";
        var password = "P@ssw0rd123!";

        // Login to get a real refresh token
        var (_, _) = await _factory.CreateAuthenticatedUserAsync(email, password);
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = loginBody
            .GetProperty("value")
            .GetProperty("refreshToken")
            .GetString();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh-token",
            new { refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        body.GetProperty("value").GetProperty("accessToken").GetString()
            .Should().NotBeNullOrWhiteSpace();
        body.GetProperty("value").GetProperty("refreshToken").GetString()
            .Should().NotBe(refreshToken); // rotation — new token issued
    }

    [Fact]
    public async Task POST_auth_refreshToken_InvalidToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh-token",
            new { refreshToken = "not-a-real-token" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_auth_refreshToken_ReusedToken_Returns401()
    {
        var email = $"reuse_{Guid.NewGuid():N}@example.com";
        var password = "P@ssw0rd123!";

        var (_, _) = await _factory.CreateAuthenticatedUserAsync(email, password);
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password });
        
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = loginBody
            .GetProperty("value")
            .GetProperty("refreshToken")
            .GetString();

        refreshToken.Should().NotBeNullOrWhiteSpace();

        // Use it once — succeeds
        var first = await _client.PostAsJsonAsync("/api/auth/refresh-token",
            new { refreshToken });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Use it again — should fail because it was rotated
        var second = await _client.PostAsJsonAsync("/api/auth/refresh-token",
            new { refreshToken });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // POST /api/auth/forgot-password ─────────────────────────────────────────

    [Fact]
    public async Task POST_auth_forgotPassword_KnownEmail_Returns200()
    {
        var email = $"forgot_{Guid.NewGuid():N}@example.com";
        await _factory.CreateAuthenticatedUserAsync(email);

        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_auth_forgotPassword_UnknownEmail_Returns200()
    {
        // Returns 200 regardless of whether the email exists —
        // prevents user enumeration attacks.
        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new
        {
            email = "ghost@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    // POST /api/auth/change-password ─────────────────────────────────────────

    [Fact]
    public async Task POST_auth_changePassword_Unauthenticated_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "P@ssw0rd123!",
            newPassword = "NewP@ssw0rd123!",
            confirmNewPassword = "NewP@ssw0rd123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_auth_changePassword_WrongCurrentPassword_Returns400()
    {
        var email = $"chpw_{Guid.NewGuid():N}@example.com";
        var (_, token) = await _factory.CreateAuthenticatedUserAsync(email);
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "WrongCurrent!",
            newPassword = "NewP@ssw0rd123!",
            confirmNewPassword = "NewP@ssw0rd123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_auth_changePassword_ValidPayload_Returns200()
    {
        var email = $"chpw_ok_{Guid.NewGuid():N}@example.com";
        var password = "P@ssw0rd123!";
        var (_, token) = await _factory.CreateAuthenticatedUserAsync(email, password);
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = password,
            newPassword = "NewP@ssw0rd123!",
            confirmNewPassword = "NewP@ssw0rd123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
