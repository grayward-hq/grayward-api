namespace Domain.Enums;

/// <summary>
/// Attack surfaces a scan may cover, as a bitmask.
/// </summary>
/// <remarks>
/// Every member must be its own power of two. The previous values had Http = 3, which is
/// Dns | Ssl, so the number 3 was ambiguous: .ToString() reported "Http" and a caller asking
/// for Dns and Ssl silently got Http instead. Nothing could decompose the mask correctly while
/// two members shared bits.
///
/// Member names mirror the worker's SurfaceType enum one-for-one, so a name serialised here is
/// resolvable there without translation.
/// </remarks>
[Flags]
public enum SurfaceType
{
    None        = 0,
    Dns         = 1,
    Ssl         = 2,
    HttpHeaders = 4,   // was Http = 3, which collided with Dns | Ssl
    Dependency  = 8,   // was SourceCode = 4; renamed to match the worker's DEPENDENCY
    Secrets     = 16,
    Subdomains  = 32,
    Ports       = 64
}