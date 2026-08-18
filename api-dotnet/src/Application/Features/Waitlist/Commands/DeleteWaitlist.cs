using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Commands;

public record DeleteWaitlistCommand(Guid WaitlistId) 
    : IRequest<Result<MessageResponse>>;

public class DeleteWaitlistHandler : IRequestHandler<DeleteWaitlistCommand, Result<MessageResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly ILogger<DeleteWaitlistHandler> _logger;

    public DeleteWaitlistHandler(
        IWaitlistRepository waitlistRepo,
        ILogger<DeleteWaitlistHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(DeleteWaitlistCommand cmd, CancellationToken ct)
    {
        var entry = await _waitlistRepo.GetById(cmd.WaitlistId, ct);
        if (entry is null)
        {
            return Result<MessageResponse>.Failure(
                Error.NotFound("Waitlist entry not found"));
        }

        _waitlistRepo.Remove(entry);
        await _waitlistRepo.SaveChangesAsync(ct);

        _logger.LogWarning("Waitlist entry deleted: {id}", cmd.WaitlistId);

        return Result<MessageResponse>.Success(
            MessageResponse.Create("Waitlist entry deleted successfully"));
    }
}
