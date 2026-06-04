using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Tests.Integration;

public class DomainControllerTests : IClassFixture<VulnWatchWebAppFactory>
{
    private readonly VulnWatchWebAppFactory _factory;

    public DomainControllerTests(VulnWatchWebAppFactory factory)
    {
        _factory = factory;
    }

    // POST /api/domains ───────────────────────────────────────────────────────

    [Fact]
    public async Task POST_domains_Unauthenticated_Returns401()
    {
        var client   = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/domains", new { domainName = "example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_domains_ValidDomain_Returns200()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"dom_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.PostAsJsonAsync("/api/domains", new
        {
            domain = $"test-{Guid.NewGuid():N}.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task POST_domains_DuplicateDomain_Returns409()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"domdup_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);
        var domainName           = $"dup-{Guid.NewGuid():N}.com";

        var first = await authClient.PostAsJsonAsync("/api/domains", new { domain = domainName });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await authClient.PostAsJsonAsync("/api/domains", new { domain = domainName });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // GET /api/domains ────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_domains_Unauthenticated_Returns401()
    {
        var client   = _factory.CreateClient();
        var response = await client.GetAsync("/api/domains");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_domains_Authenticated_Returns200WithList()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"domlist_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.GetAsync("/api/domains");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    // GET /api/domains/{domainId} ─────────────────────────────────────────────

    [Fact]
    public async Task GET_domains_ById_NotFound_Returns404()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"domget_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.GetAsync($"/api/domains/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_domains_ById_OwnDomain_Returns200()
    {
        var email            = $"domgetok_{Guid.NewGuid():N}@example.com";
        var (user, token)    = await _factory.CreateAuthenticatedUserAsync(email);
        var domainId         = await _factory.CreateVerifiedDomainAsync(user.Id, $"{Guid.NewGuid():N}.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.GetAsync($"/api/domains/{domainId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // DELETE /api/domains/{domainId} ──────────────────────────────────────────

    [Fact]
    public async Task DELETE_domains_ById_NotOwner_Returns403Or404()
    {
        var (ownerUser, _)         = await _factory.CreateAuthenticatedUserAsync($"owner_{Guid.NewGuid():N}@example.com");
        var domainId               = await _factory.CreateVerifiedDomainAsync(ownerUser.Id, $"{Guid.NewGuid():N}.com");

        var (_, otherToken)        = await _factory.CreateAuthenticatedUserAsync($"other_{Guid.NewGuid():N}@example.com");
        using var otherClient      = _factory.CreateAuthenticatedClient(otherToken);

        var response = await otherClient.DeleteAsync($"/api/domains/{domainId}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DELETE_domains_ById_OwnDomain_Returns200()
    {
        var email            = $"domdel_{Guid.NewGuid():N}@example.com";
        var (user, token)    = await _factory.CreateAuthenticatedUserAsync(email);
        var domainId         = await _factory.CreateVerifiedDomainAsync(user.Id, $"{Guid.NewGuid():N}.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.DeleteAsync($"/api/domains/{domainId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // PUT /api/domains/{id}/verify ────────────────────────────────────────────

    [Fact]
    public async Task PUT_domains_Verify_Unauthenticated_Returns401()
    {
        var client   = _factory.CreateClient();
        var response = await client.PutAsync($"/api/domains/{Guid.NewGuid()}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_domains_Verify_UnknownDomain_Returns404()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"domverify_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.PutAsync($"/api/domains/{Guid.NewGuid()}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // POST /api/domains/resendtoken ───────────────────────────────────────────

    [Fact]
    public async Task POST_domains_ResendToken_Unauthenticated_Returns401()
    {
        var client   = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/domains/resend-token", new { domainId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}