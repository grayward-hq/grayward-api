

namespace Application.Features.BrandProtection.DTOs;

public record LookAlikeDomainResult(
    string Candidate, 
    bool HasDns,      
    bool IsActive,        
    string RiskLevel);