
using System.Security.Cryptography;
using Application.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

public class GitHubAppJwtFactory
{
    private readonly GitHubAppOptions _options;
    public GitHubAppJwtFactory(IOptions<GitHubAppOptions> options) => _options = options.Value;

    public string CreateJwt()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.PrivateKeyPem);

        var now = DateTimeOffset.UtcNow;
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer    = _options.AppId,
            IssuedAt  = now.AddSeconds(-60).UtcDateTime,  // backdate for clock skew
            NotBefore = now.AddSeconds(-60).UtcDateTime,
            Expires   = now.AddMinutes(9).UtcDateTime,    // GitHub max is 10 min
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(rsa.ExportParameters(true)), // export params so disposing rsa is safe
                SecurityAlgorithms.RsaSha256)
        });
    }
}