using Application.Features.Domain;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Tests.Domain.Handlers;
 
public class RegisterDomainHandlerTests
{
    private readonly Mock<IDomainRepository> _domains;
    private readonly Mock<ICurrentUser> _currentUser;
    private readonly Mock<ITokenService> _token;
    private readonly Mock<ILogger<RegisterDomainHandler>> _logger;
    private readonly RegisterDomainHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();
 
    public RegisterDomainHandlerTests()
    {
        _domains = new Mock<IDomainRepository>();
        _currentUser = new Mock<ICurrentUser>();
        _token = new Mock<ITokenService>();
        _logger = new Mock<ILogger<RegisterDomainHandler>>();
 
        _currentUser.Setup(c => c.UserId).Returns(_userId);
        _token.Setup(t => t.Generate()).Returns(("rawtoken", "hashtoken"));
        _domains.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _domains.Setup(r => r.AddAsync(It.IsAny<ScannedDomain>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
 
        _sut = new RegisterDomainHandler(_domains.Object, _currentUser.Object, _token.Object, _logger.Object);
    }
 
    [Fact]
    public async Task Handle_NewValidDomain_RegistersAndReturnsDnsInstructions()
    {
        _domains.Setup(r => r.GetByNameAndUser("example.com", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScannedDomain?)null);
        _domains.Setup(r => r.CountPending(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
 
        var result = await _sut.Handle(new RegisterDomainCommand("example.com"), CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.DomainName.Should().Be("example.com");
        result.Value.VerificationToken.Should().Be("rawtoken");
        result.Value.Instructions.TxtRecord.Should().Be("_vulnwatch-verify");
    }
 
    [Fact]
    public async Task Handle_DuplicateDomainForUser_ReturnsConflict()
    {
        _domains.Setup(r => r.CountPending(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _domains.Setup(r => r.GetByNameAndUser("example.com", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fakes.TestScannedDomain(_userId, "example.com"));
 
        var result = await _sut.Handle(new RegisterDomainCommand("example.com"), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Conflict);
    }
 
    [Fact]
    public async Task Handle_AtPendingLimit_ReturnsRateLimited()
    {
        _domains.Setup(r => r.CountPending(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(20);
 
        var result = await _sut.Handle(new RegisterDomainCommand("newdomain.com"), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.RateLimited);
        result.Error.Message.Should().Contain("20");
    }
 
    [Theory]
    [InlineData("not a domain")]
    [InlineData("http://example.com")]
    [InlineData("-invalid.com")]
    public async Task Handle_InvalidDomainFormat_ReturnsValidationError(string domain)
    {
        _domains.Setup(r => r.CountPending(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _domains.Setup(r => r.GetByNameAndUser(It.IsAny<string>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScannedDomain?)null);
 
        var result = await _sut.Handle(new RegisterDomainCommand(domain), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Validation);
    }
 
    [Fact]
    public async Task Handle_DomainIsNormalizedToLowercase()
    {
        _domains.Setup(r => r.CountPending(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _domains.Setup(r => r.GetByNameAndUser("example.com", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScannedDomain?)null);
 
        await _sut.Handle(new RegisterDomainCommand("EXAMPLE.COM"), CancellationToken.None);
 
        _domains.Verify(r => r.GetByNameAndUser("example.com", _userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
 
public class VerifyDomainHandlerTests
{
    private readonly Mock<IDomainRepository> _domains;
    private readonly Mock<IDomainSettingsRepository> _domainSettings;
    private readonly Mock<ICurrentUser> _currentUser;
    private readonly Mock<IDnsResolver> _dnsResolver;
    private readonly Mock<ILogger<VerifyDomainHandler>> _logger;
    private readonly Guid _userId = Guid.NewGuid();
 
    // GetValue<T> is an extension method — cannot be mocked; use a real IConfiguration
    private static IConfiguration BuildConfig(bool dnsLookup) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dns:Lookup"] = dnsLookup.ToString().ToLower()
            })
            .Build();
 
    private VerifyDomainHandler BuildSut(bool dnsLookup = false) =>
        new(_domains.Object, _domainSettings.Object, _currentUser.Object, _dnsResolver.Object, _logger.Object, BuildConfig(dnsLookup));
 
    public VerifyDomainHandlerTests()
    {
        _domains = new Mock<IDomainRepository>();
        _domainSettings = new Mock<IDomainSettingsRepository>();
        _currentUser = new Mock<ICurrentUser>();
        _dnsResolver = new Mock<IDnsResolver>();
        _logger = new Mock<ILogger<VerifyDomainHandler>>();
 
        _currentUser.Setup(c => c.UserId).Returns(_userId);
        _domains.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _domainSettings.Setup(r => r.ExistsForDomain(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _domainSettings.Setup(r => r.AddAsync(It.IsAny<DomainSettings>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }
 
    [Fact]
    public async Task Handle_PendingDomainWithDnsLookupDisabled_VerifiesDomain()
    {
        var domain = Fakes.TestScannedDomain(_userId);
        _domains.Setup(r => r.GetById(domain.Id, It.IsAny<CancellationToken>())).ReturnsAsync(domain);
 
        var result = await BuildSut(dnsLookup: false).Handle(new VerifyDomainCommand(domain.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(VerificationStatus.Verified);
        domain.VerificationStatus.Should().Be(VerificationStatus.Verified);
    }
 
    [Fact]
    public async Task Handle_AlreadyVerifiedDomain_ReturnsConflict()
    {
        var domain = Fakes.TestScannedDomain(_userId, status: VerificationStatus.Verified);
        _domains.Setup(r => r.GetById(domain.Id, It.IsAny<CancellationToken>())).ReturnsAsync(domain);
 
        var result = await BuildSut().Handle(new VerifyDomainCommand(domain.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Conflict);
    }
 
    [Fact]
    public async Task Handle_DomainBelongingToOtherUser_ReturnsNotFound()
    {
        var domain = Fakes.TestScannedDomain(Guid.NewGuid()); // different userId
        _domains.Setup(r => r.GetById(domain.Id, It.IsAny<CancellationToken>())).ReturnsAsync(domain);
 
        var result = await BuildSut().Handle(new VerifyDomainCommand(domain.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
 
    [Fact]
    public async Task Handle_DomainNotFound_ReturnsNotFound()
    {
        _domains.Setup(r => r.GetById(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScannedDomain?)null);
 
        var result = await BuildSut().Handle(new VerifyDomainCommand(Guid.NewGuid()), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
 
    [Fact]
    public async Task Handle_DnsLookupEnabled_TxtRecordMissing_ReturnsPending()
    {
        var domain = Fakes.TestScannedDomain(_userId, token: "expectedhash");
        _domains.Setup(r => r.GetById(domain.Id, It.IsAny<CancellationToken>())).ReturnsAsync(domain);
        _dnsResolver.Setup(d => d.GetTxtRecords(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
 
        var result = await BuildSut(dnsLookup: true).Handle(new VerifyDomainCommand(domain.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(VerificationStatus.Pending);
        result.Value.Message.Should().Contain("propagating");
    }
}
 
public class DeleteDomainHandlerTests
{
    private readonly Mock<IDomainRepository> _domains;
    private readonly Mock<ICurrentUser> _currentUser;
    private readonly Mock<ILogger<DeleteDomainHandler>> _logger;
    private readonly DeleteDomainHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();
 
    public DeleteDomainHandlerTests()
    {
        _domains = new Mock<IDomainRepository>();
        _currentUser = new Mock<ICurrentUser>();
        _logger = new Mock<ILogger<DeleteDomainHandler>>();
 
        _currentUser.Setup(c => c.UserId).Returns(_userId);
        _domains.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
 
        _sut = new DeleteDomainHandler(_domains.Object, _currentUser.Object, _logger.Object);
    }
 
    [Fact]
    public async Task Handle_OwnedDomain_DeletesSuccessfully()
    {
        var domain = Fakes.TestScannedDomain(_userId);
        _domains.Setup(r => r.GetById(domain.Id, It.IsAny<CancellationToken>())).ReturnsAsync(domain);
 
        var result = await _sut.Handle(new DeleteDomainCommand(domain.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        _domains.Verify(r => r.Remove(domain), Times.Once);
        _domains.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
 
    [Fact]
    public async Task Handle_DomainOwnedByOtherUser_ReturnsForbidden()
    {
        var domain = Fakes.TestScannedDomain(Guid.NewGuid());
        _domains.Setup(r => r.GetById(domain.Id, It.IsAny<CancellationToken>())).ReturnsAsync(domain);
 
        var result = await _sut.Handle(new DeleteDomainCommand(domain.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Forbidden);
        _domains.Verify(r => r.Remove(It.IsAny<ScannedDomain>()), Times.Never);
    }
 
    [Fact]
    public async Task Handle_DomainNotFound_ReturnsNotFound()
    {
        _domains.Setup(r => r.GetById(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScannedDomain?)null);
 
        var result = await _sut.Handle(new DeleteDomainCommand(Guid.NewGuid()), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
}