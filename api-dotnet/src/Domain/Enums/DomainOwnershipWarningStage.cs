namespace Domain.Enums;

public enum OwnershipWarningStage
{
    Warning,          // 24h — TXT record missing, please check
    MonitoringPaused, // 72h — monitoring stopped until record is restored
    Revoked           // 7d  — domain removed from account
}