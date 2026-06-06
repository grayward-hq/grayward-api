using Application.Helpers;
using Domain.Entities;
using Domain.Enums;

namespace Application.Features.Compliance.DTOs;
public record EnrichedFinding(
    Finding Raw,
    HttpHeadersPayload? HttpHeaders,
    SslPayload? Ssl,
    DnsPayload? Dns)
{
    public static EnrichedFinding From(Finding f) => new(
        f,
        f.Surface == FindingSurface.HttpHeaders
            ? PayloadParser.TryParse<HttpHeadersPayload>(f.TechnicalPayload) : null,
        f.Surface == FindingSurface.Ssl
            ? PayloadParser.TryParse<SslPayload>(f.TechnicalPayload) : null,
        f.Surface == FindingSurface.Dns
            ? PayloadParser.TryParse<DnsPayload>(f.TechnicalPayload) : null
    );
}