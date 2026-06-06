namespace Domain.Enums;

public enum AlertType
{
    SslExpiry,
    ScanCompleted,
    CriticalFindingDetected,
    DomainStatusChanged,
    SecurityScoreDrop,
    OwnershipWarning,
    BrandThreat,
    CredentialBreach
}
