using Domain.Entities;
using Domain.Enums;

namespace Tests;
public static class Fakes
{
    public static User TestUser(
        string email = "tony@example.com",
        bool emailConfirmed = true,
        string? googleId = null) => new()
    {
        // IdentityUser<Guid> has public setters on Id/Email/UserName
        Id = Guid.NewGuid(),
        Email = email,
        UserName = email,
        NormalizedEmail = email.ToUpperInvariant(),
        NormalizedUserName = email.ToUpperInvariant(),
        EmailConfirmed = emailConfirmed,
        SecurityStamp = Guid.NewGuid().ToString(),
    };
 
    public static ScannedDomain TestScannedDomain(
        Guid? userId = null,
        string name = "example.com",
        VerificationStatus status = VerificationStatus.Pending,
        string? token = "hash123") =>
        ScannedDomain.Create(userId ?? Guid.NewGuid(), name, token)
            .Tap(d =>
            {
                if (status == VerificationStatus.Verified) d.Verify();
            });
 
    public static Scan TestScan(
        Guid? userId = null,
        Guid? domainId = null,
        ScanStatus status = ScanStatus.Queued,
        int? score = null)
    {
        var scan = Scan.Create(
            userId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            ScanTargetType.Domain,
            ScanCoverage.Quick,
            SurfaceType.Dns | SurfaceType.Ssl | SurfaceType.Http,
            domainId ?? Guid.NewGuid(),
            null);
 
        if (status == ScanStatus.Running) scan.MarkRunning();
        if (status == ScanStatus.Completed) { scan.MarkRunning(); scan.Complete(score ?? 80); }
        if (status == ScanStatus.Failed) { scan.MarkRunning(); scan.Fail(); }
 
        return scan;
    }
 
    public static Finding TestFinding(
        Guid? scanId = null,
        FindingSurface surface = FindingSurface.Ssl,
        FindingSeverity severity = FindingSeverity.High,
        FindingStatus status = FindingStatus.Open)
    {
        var f = Finding.Create(
            scanId ?? Guid.NewGuid(),
            surface,
            severity,
            "Missing HSTS header",
            aiExplanation: "The server does not enforce HTTPS.",
            remediationSteps: "Add Strict-Transport-Security header.");
 
        if (status == FindingStatus.Remediated) f.Remediate();
        if (status == FindingStatus.Ignored) f.Ignore();
 
        return f;
    }
}
 
// Fluent helper to allow inline mutation of domain objects built via factory methods
file static class ObjectExtensions
{
    public static T Tap<T>(this T obj, Action<T> action) { action(obj); return obj; }
}