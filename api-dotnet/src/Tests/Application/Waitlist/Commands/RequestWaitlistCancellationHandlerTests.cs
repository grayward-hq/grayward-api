using Application.Features.Waitlist;
using Application.Features.Waitlist.Commands;
using Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Application.Waitlist.Commands;

/// <summary>
/// The handler's whole job is now to enqueue and return. Eligibility, throttling and sending moved
/// to <see cref="WaitlistMailDispatcher"/> — deliberately, because doing them here made a hit
/// measurably slower than a miss and that timing difference partially defeated the masked response.
/// Those rules are covered by <c>WaitlistMailDispatcherTests</c>; what matters here is that the
/// handler behaves identically no matter what address it is given.
/// </summary>
public class RequestWaitlistCancellationHandlerTests
{
    private const string GenericMessage =
        "If this email is on the waitlist, a cancellation link has been sent.";

    private readonly Mock<IWaitlistMailQueue> _mockQueue = new();
    private readonly Mock<IConfiguration> _mockConfig = new();
    private readonly Mock<IHttpContextAccessor> _mockHttp = new();
    private readonly Mock<ILogger<RequestWaitlistCancellationHandler>> _mockLogger = new();
    private readonly RequestWaitlistCancellationHandler _handler;

    public RequestWaitlistCancellationHandlerTests()
    {
        _handler = new RequestWaitlistCancellationHandler(
            _mockQueue.Object, _mockConfig.Object, _mockHttp.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_EnqueuesACancellationLinkJob()
    {
        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand("someone@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(GenericMessage, result.Value!.Message);

        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<WaitlistMailJob>(j =>
                j.Kind == WaitlistMailKind.CancellationLink &&
                j.Email == "someone@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NormalisesTheEmailBeforeQueueing()
    {
        await _handler.Handle(
            new RequestWaitlistCancellationCommand("  MiXeD@Example.COM  "), CancellationToken.None);

        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<WaitlistMailJob>(j => j.Email == "mixed@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The masking is only as good as its uniformity: an address on the list and one that is not
    /// must produce the same response and the same amount of work. The handler never looks the
    /// address up, so this holds by construction — this test pins that it stays that way.
    /// </summary>
    [Fact]
    public async Task Handle_TreatsEveryAddressIdentically()
    {
        var onList = await _handler.Handle(
            new RequestWaitlistCancellationCommand("known@example.com"), CancellationToken.None);
        var notOnList = await _handler.Handle(
            new RequestWaitlistCancellationCommand("unknown@example.com"), CancellationToken.None);

        Assert.Equal(onList.IsSuccess, notOnList.IsSuccess);
        Assert.Equal(onList.Value!.Message, notOnList.Value!.Message);

        _mockQueue.Verify(q => q.EnqueueAsync(
            It.IsAny<WaitlistMailJob>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// A queue outage must not become an oracle of its own by making this endpoint fail where it
    /// would otherwise succeed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenQueueingFails_StillReturnsTheMaskedResponse()
    {
        _mockQueue
            .Setup(q => q.EnqueueAsync(It.IsAny<WaitlistMailJob>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var result = await _handler.Handle(
            new RequestWaitlistCancellationCommand("someone@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(GenericMessage, result.Value!.Message);
    }
}
