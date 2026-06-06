using Domain.Enums;

namespace Application.Features.Domain.DTOs;


public record DomainSummary(
    Guid Id,
    string Domain,
    VerificationStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastScannedAt,
    int? LastSecurityScore,
    DnsInstructions? Instructions);