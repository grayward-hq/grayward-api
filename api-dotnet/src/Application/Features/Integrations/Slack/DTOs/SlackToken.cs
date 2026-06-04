namespace Application.Features.Integrations.Slack.DTOs;

public record SlackToken(
    bool Ok,
    string? AppId,
    SlackAuthedUser? AuthedUser,
    string? Scope,
    string? TokenType,
    string? AccessToken,
    string? BotUserId,
    SlackTeam? Team,
    string? Enterprise,
    bool IsEnterpriseInstall,
    SlackIncomingWebhook? IncomingWebhook,
    string? Error)
{
    public static SlackToken Success(
        string appId,
        SlackAuthedUser authedUser,
        string scope,
        string tokenType,
        string accessToken,
        string botUserId,
        SlackTeam team,
        bool isEnterpriseInstall,
        SlackIncomingWebhook incomingWebhook,
        string? enterprise = null)
        => new(
            Ok: true,
            AppId: appId,
            AuthedUser: authedUser,
            Scope: scope,
            TokenType: tokenType,
            AccessToken: accessToken,
            BotUserId: botUserId,
            Team: team,
            Enterprise: enterprise,
            IsEnterpriseInstall: isEnterpriseInstall,
            IncomingWebhook: incomingWebhook,
            Error: null
        );

    public static SlackToken Fail(string error)
        => new(
            Ok: false,
            AppId: null,
            AuthedUser: null,
            Scope: null,
            TokenType: null,
            AccessToken: null,
            BotUserId: null,
            Team: null,
            Enterprise: null,
            IsEnterpriseInstall: false,
            IncomingWebhook: null,
            Error: error
        );
}

public record SlackAuthedUser(
    string Id
);

public record SlackTeam(
    string Id,
    string Name
);

public record SlackIncomingWebhook(
    string Channel,
    string ChannelId,
    string ConfigurationUrl,
    string Url
);