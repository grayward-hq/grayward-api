using Application.Features.Chat.DTOs;
using Application.Features.Chat.Helpers;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using System.Runtime.CompilerServices;
using System.Text;

namespace Application.Features.Chat;

public record StreamChatMessageCommand(
    Guid SessionId,
    string Message) : IStreamRequest<string>;

public class StreamChatMessageHandler(
    IRedisService sessionStore,
    IChatService chatService,
    IScanRepository scanRepo,
    IDomainRepository domainRepo,
    ICurrentUser currentUser)
    : IStreamRequestHandler<StreamChatMessageCommand, string>
{
    public async IAsyncEnumerable<string> Handle(
        StreamChatMessageCommand cmd,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Message))
        {
            yield return BuildErrorEvent("Message cannot be empty.");
            yield break;
        }

        var session = await sessionStore.GetChatSession(cmd.SessionId, ct);
        if (session is null)
        {
            yield return BuildErrorEvent("Session not found or expired.");
            yield break;
        }

        var scan = await scanRepo.FindByIdWithFindings(session.ScanId, ct);
        if (scan is null || scan.UserId != currentUser.UserId)
        {
            yield return BuildErrorEvent("Access denied.");
            yield break;
        }

        if (scan.Status != ScanStatus.Completed)
        {
            yield return BuildErrorEvent("Scan is not completed.");
            yield break;
        }

        var domain = await domainRepo.GetById(scan.DomainId!.Value, ct);

        // ── Build context ────────────────────────────────────────────────────

        var systemPrompt = ScanReportPromptBuilder.Build(scan, domain);

        var history = session.History
            .Append(new ChatTurn(ChatMessageRole.User, cmd.Message))
            .ToList();

        // ── Stream and accumulate ─────────────────────────────────────────────

        var accumulated = new StringBuilder();

        await foreach (var chunk in chatService.Stream(systemPrompt, history, cmd.Message, ct))
        {
            accumulated.Append(chunk);
            yield return chunk;
        }

        // ── Persist full reply after streaming completes ──────────────────────

        var fullReply = accumulated.ToString();

        var newHistory = session.History.ToList();
        newHistory.Add(new ChatTurn(ChatMessageRole.User, cmd.Message));
        newHistory.Add(new ChatTurn(ChatMessageRole.Assistant, fullReply));

        await sessionStore.SetChatSession(session with { History = newHistory }, ct);
    }

    private static string BuildErrorEvent(string message)
        => System.Text.Json.JsonSerializer.Serialize(new { error = message });
}