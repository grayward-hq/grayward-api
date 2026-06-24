
namespace Application.Options;

public class GitHubAppOptions
{
    public string AppId { get; set; } = default!;          // App ID (or client ID)
    public string PrivateKeyPem { get; set; } = default!;  // full PEM contents
}