namespace Application.Features.Dashboard.DTOs;

public sealed class ScanScoreDto
{
    public DateTime? CompletedAt { get; init; }
    public int? SecurityScore { get; init; }
}