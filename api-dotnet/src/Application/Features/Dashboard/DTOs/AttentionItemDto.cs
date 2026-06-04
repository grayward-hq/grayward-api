namespace Application.Features.Dashboard.DTOs;

public record AttentionItemDto(
    Guid DomainId,
    string DomainName,
    string Action,             // "renew_ssl" | "fix_findings" | "resume_monitoring" | "verify_domain"
    string Description,
    string Severity,           // "critical" | "warning" | "info"
    int SortOrder
);