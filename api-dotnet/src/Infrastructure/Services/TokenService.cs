using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly IDataProtector _protector;
    public TokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("VulnWatch.Integration.SlackBotToken.v1");
    }
    public (string RawToken, string TokenHash) Generate()
    {

        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var rawToken = $"vulnscan-verify={Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var tokenHash = Convert.ToBase64String(hash);

        return (rawToken, tokenHash);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}