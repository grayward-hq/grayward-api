using Domain.Enums;

namespace Application.Features.Repository.DTOs;

public record SeverityCountDto(FindingSeverity Severity, int Count);