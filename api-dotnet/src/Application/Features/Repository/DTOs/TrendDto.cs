using Domain.Enums;

namespace Application.Features.Repository.DTOs;

public record TrendPointDto(
    DateTime Date,
    int Critical,
    int High,
    int Medium,
    int Low);

public sealed record TrendRowDto(
    FindingSeverity Severity,
    DateTime Day);