using Application.Features.Chat.DTOs;
using Application.Features.Scans;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;

namespace Application.Features.Chat;

public record StartScanChatCommand(Guid ScanId) : IRequest<Result<ChatMessageResponse>>;

public class StartScanChatHandler(
    IScanRepository scanRepo,
    IDomainRepository domainRepo,
    IRedisService sessionStore,
    ICurrentUser currentUser)
    : IRequestHandler<StartScanChatCommand, Result<ChatMessageResponse>>
{
    public async Task<Result<ChatMessageResponse>> Handle(
        StartScanChatCommand cmd, CancellationToken ct)
    {
        var scan = await scanRepo.FindByIdWithFindings(cmd.ScanId, ct);

        if (scan is null || scan.UserId != currentUser.UserId)
            return Result<ChatMessageResponse>.Failure(Error.NotFound("Scan not found."));

        if (scan.Status != ScanStatus.Completed)
            return Result<ChatMessageResponse>.Failure(
                Error.Validation("Scan must be completed before starting a chat."));

        var domain = await domainRepo.GetById(scan.DomainId!.Value, ct);
        if (domain is null)
            return Result<ChatMessageResponse>.Failure(Error.NotFound("Domain not found."));

        var sessionId = await sessionStore.CreateChatSession(cmd.ScanId, ct);

        var greeting = $"I've loaded the security scan report for **{domain.DomainName}** " +
                       $"(score: {scan.SecurityScore}/100). Ask me anything about the findings, " +
                       $"what they mean, or how to fix them.";

        return Result<ChatMessageResponse>.Success(
            ChatMessageResponse.Create(sessionId, ChatMessageRole.Assistant, greeting));
    }
}