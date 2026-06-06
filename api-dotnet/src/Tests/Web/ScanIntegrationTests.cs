using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Tests.Integration;

public class ScanControllerTests : IClassFixture<VulnWatchWebAppFactory>
{
    private readonly VulnWatchWebAppFactory _factory;

    public ScanControllerTests(VulnWatchWebAppFactory factory)
    {
        _factory = factory;
    }

    // POST /api/scans ─────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_scans_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scans", new
        {
            domain = "existing-domain.com",
            surfaceTypes = "Dns",
            coverage = "Full"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_scans_UnknownDomain_Returns404()
    {
        var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"scan_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/scans")
        {
            Content = JsonContent.Create(new
            {
                domain       = "nonexistent-domain-that-does-not-exist.com",
                surfaceTypes = "Dns",
                coverage     = "Full"
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await authClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // [Fact]
    // public async Task POST_scans_UnknownDomain_Debug()
    // {
    //     var (_, token)       = await _factory.CreateAuthenticatedUserAsync($"scan_{Guid.NewGuid():N}@example.com");
    //     using var authClient = _factory.CreateAuthenticatedClient(token);

    //     var response = await authClient.PostAsJsonAsync("/api/scans", new
    //     {
    //         domain = "nonexistent-domain-that-does-not-exist.com",
    //         surfaceTypes = "Dns",
    //         coverage = "Full"
    //     });

    //     var body = await response.Content.ReadAsStringAsync();
    //     Console.WriteLine($"Status: {response.StatusCode}");
    //     Console.WriteLine($"Body: {body}");
    // }


    // GET /api/scans/{domainId}/history ───────────────────────────────────────

    [Fact]
    public async Task GET_scans_History_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/scans/{Guid.NewGuid()}/history");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_scans_History_UnknownDomain_Returns404()
    {
        var (_, token) = await _factory.CreateAuthenticatedUserAsync($"hist_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.GetAsync($"/api/scans/{Guid.NewGuid()}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_scans_History_OwnDomain_Returns200()
    {
        var email = $"histok_{Guid.NewGuid():N}@example.com";
        var (user, token) = await _factory.CreateAuthenticatedUserAsync(email);
        var domainId = await _factory.CreateVerifiedDomainAsync(user.Id, $"{Guid.NewGuid():N}.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.GetAsync($"/api/scans/{domainId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // GET /api/scans/{scanId}/report ──────────────────────────────────────────

    [Fact]
    public async Task GET_scans_Report_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/scans/{Guid.NewGuid()}/report");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_scans_Report_UnknownScan_Returns404()
    {
        var (_, token) = await _factory.CreateAuthenticatedUserAsync($"rep_{Guid.NewGuid():N}@example.com");
        using var authClient = _factory.CreateAuthenticatedClient(token);

        var response = await authClient.GetAsync($"/api/scans/{Guid.NewGuid()}/report");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}