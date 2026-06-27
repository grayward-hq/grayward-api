namespace Domain.Enums;

[Flags]
public enum SurfaceType
{
    None = 0,
    Dns = 1,
    Ssl = 2,
    Http = 3,
    SourceCode = 4
}