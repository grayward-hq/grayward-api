using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Tests.Integration;

public class ProfileControllerTests : IClassFixture<VulnWatchWebAppFactory>
{
    private readonly VulnWatchWebAppFactory _factory;

    public ProfileControllerTests(VulnWatchWebAppFactory factory)
    {
        _factory = factory;
    }

    // GET /api/profile ────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_profile_Unauthenticated_Returns401()
    {
        var client   = _factory.CreateClient();
        var response = await client.GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_profile_Authenticated_Returns200()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"prof_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    // PUT /api/profile ────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_profile_Unauthenticated_Returns401()
    {
        var client   = _factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/profile", new { firstName = "Tony" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_profile_ValidPayload_Returns200()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"profupd_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.PutAsJsonAsync("/api/profile", new
        {
            firstName = "Updated",
            lastName  = "Name"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}