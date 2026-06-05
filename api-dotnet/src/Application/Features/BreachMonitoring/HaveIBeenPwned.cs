using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace Application.Features.BreachMonitoring;

public class HaveIBeenPwnedService(IHttpClientFactory factory, IConfiguration config)
{
    public async Task<BreachCheckResult> CheckEmailAsync(string email, CancellationToken ct)
    {
        var apiKey = config["HaveIBeenPwned:ApiKey"]
            ?? throw new InvalidOperationException("HaveIBeenPwned:ApiKey not configured.");

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("hibp-api-key", apiKey);
        http.DefaultRequestHeaders.Add("User-Agent", "VulnWatch-BreachMonitor");

        var encoded = Uri.EscapeDataString(email);
        var response = await http.GetAsync(
            $"https://haveibeenpwned.com/api/v3/breachedaccount/{encoded}?truncateResponse=false", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new BreachCheckResult(email, false, 0, []);

        response.EnsureSuccessStatusCode();

        var breaches = await response.Content.ReadFromJsonAsync<List<HibpBreach>>(ct) ?? [];
        return new BreachCheckResult(email, breaches.Count > 0, breaches.Count,
            breaches.Select(b => b.Name).ToList());
    }
}

public record HibpBreach(string Name, string Title, string Domain, string BreachDate);
public record BreachCheckResult(string Email, bool IsBreached, int BreachCount, List<string> BreachNames);