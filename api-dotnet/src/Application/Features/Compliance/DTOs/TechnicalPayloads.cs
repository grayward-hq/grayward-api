namespace Application.Features.Compliance.DTOs;

public record HttpHeadersPayload(
    int StatusCode,
    string? ServerHeader,
    List<string> PresentHeaders,
    List<string> MissingHeaders,
    string? ExposedTechnology,
    List<string> Issues);

public record SslPayload(
    string Protocol,
    string CipherSuite,
    string CertSubject,
    DateTime CertExpiry,
    int DaysUntilExpiry,
    bool IsSelfSigned,
    bool IsExpired,
    List<string> Issues);

public record DnsPayload(
    bool HasSPF,
    bool HasDMARC,
    bool HasMX,
    List<string> Issues);