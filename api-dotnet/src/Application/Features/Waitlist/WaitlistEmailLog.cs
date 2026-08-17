using System.Security.Cryptography;
using System.Text;

namespace Application.Features.Waitlist;

/// <summary>
/// Produces the short, stable token used in place of an address in waitlist log lines.
/// </summary>
/// <remarks>
/// These flows exist to stop an anonymous caller learning which addresses are on the waitlist.
/// Logging the address itself would hand that back to anyone with log access, so every waitlist log
/// line identifies the recipient by this hash instead. Short by design — enough to correlate lines
/// within an investigation, not a store of addresses.
/// </remarks>
public static class WaitlistEmailLog
{
    public static string Hash(string email)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(email));
        return Convert.ToHexString(bytes)[..8];
    }
}
