namespace Application.Options;

public class GitHubAppOptions
{
    public const string Section = "GitHubApp";

    public string AppId { get; set; } = "";
    public string PrivateKeyPemPath { get; set; } = "";

    private string? _privateKeyPem;
    public string PrivateKeyPem => _privateKeyPem ??= LoadPem();

    private string LoadPem()
    {
        if (string.IsNullOrWhiteSpace(PrivateKeyPemPath))
            throw new InvalidOperationException(
                "GitHubApp__PrivateKeyPemPath is not set.");

        if (!File.Exists(PrivateKeyPemPath))
            throw new FileNotFoundException(
                $"GitHub PEM file not found at: {PrivateKeyPemPath}");

        return File.ReadAllText(PrivateKeyPemPath);
    }
}