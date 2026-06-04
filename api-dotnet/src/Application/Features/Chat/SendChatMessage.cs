using Application.Features.Chat.DTOs;
using Application.Features.Chat.Helpers;
using Application.Features.Scans;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using System.Text;
using System.Text.Json;

namespace Application.Features.Chat;

public record SendChatMessageCommand(
    Guid SessionId,
    string Message) : IRequest<Result<ChatMessageResponse>>;

public class SendChatMessageHandler(
    IRedisService sessionStore,
    IChatService chatService,
    IScanRepository scanRepo,
    IDomainRepository domainRepo,
    ICurrentUser currentUser)
    : IRequestHandler<SendChatMessageCommand, Result<ChatMessageResponse>>
{
    public async Task<Result<ChatMessageResponse>> Handle(
        SendChatMessageCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Message))
            return Result<ChatMessageResponse>.Failure(
                Error.Validation("Message cannot be empty."));

        var session = await sessionStore.GetChatSession(cmd.SessionId, ct);
        if (session is null)
            return Result<ChatMessageResponse>.Failure(
                Error.NotFound("Chat session not found or expired."));

        var scan = await scanRepo.FindByIdWithFindings(session.ScanId, ct);
        if (scan is null || scan.UserId != currentUser.UserId)
            return Result<ChatMessageResponse>.Failure(Error.Forbidden("Access denied."));

        if (scan.Status != ScanStatus.Completed)
            return Result<ChatMessageResponse>.Failure(
                Error.Validation("Scan has not completed yet."));

        ScannedDomain? domain = null;
        if (scan.DomainId.HasValue)
            domain = await domainRepo.GetById(scan.DomainId.Value, ct);

        var systemPrompt = ScanReportPromptBuilder.Build(scan, domain);

        var updatedHistory = session.History
            .Append(new ChatTurn(ChatMessageRole.User, cmd.Message))
            .ToList();

        var reply = await chatService.Chat(systemPrompt, updatedHistory, cmd.Message, ct);

        // Persist updated session with both turns
        var newHistory = session.History.ToList();
        newHistory.Add(new ChatTurn(ChatMessageRole.User, cmd.Message));
        newHistory.Add(new ChatTurn(ChatMessageRole.Assistant, reply));

        var updatedSession = session with { History = newHistory };
        await sessionStore.SetChatSession(updatedSession, ct);

        return Result<ChatMessageResponse>.Success(
            ChatMessageResponse.Create(cmd.SessionId, ChatMessageRole.Assistant, reply));
    }
}