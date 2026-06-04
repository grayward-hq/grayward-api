using Application.Features.Scans;
using Application.Features.Scans.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.Configuration;

namespace Tests.Scans.Handlers;
 
public class StartScanHandlerTests : IDisposable
{
    private readonly VulnWatchDbContext _dbContext;
    private readonly Mock<ICurrentUser> _currentUser;
    private readonly Mock<IScanRepository> _scanRepo;
    private readonly Mock<IDomainRepository> _domainRepo;
    private readonly Mock<IRedisService> _redis;
    private readonly Mock<IConfiguration> _config;

    private readonly Mock<ILogger<StartScanHandler>> _logger;
    private readonly StartScanHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();
 
    public StartScanHandlerTests()
    {
        // Use a real in-memory DbContext — DatabaseFacade cannot be mocked.
        // Suppress TransactionIgnoredWarning: in-memory transactions are no-ops but
        // EF throws by default when BeginTransactionAsync is called.
       var options = new DbContextOptionsBuilder<VulnWatchDbContext>()
            .UseInMemoryDatabase("StartScanTests_" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new VulnWatchDbContext(options);
 
        _currentUser = new Mock<ICurrentUser>();
        _scanRepo = new Mock<IScanRepository>();
        _domainRepo = new Mock<IDomainRepository>();
        _redis = new Mock<IRedisService>();
        _config = new Mock<IConfiguration>();
        _logger = new Mock<ILogger<StartScanHandler>>();
 
        _currentUser.Setup(c => c.UserId).Returns(_userId);
 
        _scanRepo.Setup(r => r.AddAsync(It.IsAny<Scan>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _scanRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _redis.Setup(r => r.PublishScanJob(It.IsAny<string>(), It.IsAny<ScanJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
 
        _sut = new StartScanHandler(
            _dbContext, _currentUser.Object, _scanRepo.Object,
            _domainRepo.Object, _redis.Object, _logger.Object, _config.Object);
    }
 
    public void Dispose() => _dbContext.Dispose();
 
    [Fact]
    public async Task Handle_VerifiedDomain_NoRunningScan_EnqueuesScan()
    {
        var domain = Fakes.TestScannedDomain(_userId, status: VerificationStatus.Verified);
        _domainRepo.Setup(r => r.FindUserDomainByName(_userId, "example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(domain);
        _scanRepo.Setup(r => r.FindByIdempotencyKey(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Scan?)null);
        _scanRepo.Setup(r => r.FindRunningByDomain(domain.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Scan?)null);
 
        var result = await _sut.Handle(
            new StartScanCommand("example.com", ScanCoverage.Quick, SurfaceType.Ssl, Guid.NewGuid()),
            CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(ScanStatus.Queued);
        _redis.Verify(r => r.PublishScanJob("scan-jobs", It.IsAny<ScanJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }
 
    [Fact]
    public async Task Handle_DomainNotFound_ReturnsNotFound()
    {
        _domainRepo.Setup(r => r.FindUserDomainByName(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScannedDomain?)null);
 
        var result = await _sut.Handle(
            new StartScanCommand("unknown.com", ScanCoverage.Quick, SurfaceType.Dns, Guid.NewGuid()),
            CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
 
    [Fact]
    public async Task Handle_UnverifiedDomain_ReturnsForbidden()
    {
        var domain = Fakes.TestScannedDomain(_userId, status: VerificationStatus.Pending);
        _domainRepo.Setup(r => r.FindUserDomainByName(_userId, "example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(domain);
 
        var result = await _sut.Handle(
            new StartScanCommand("example.com", ScanCoverage.Quick, SurfaceType.Dns, Guid.NewGuid()),
            CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Forbidden);
        result.Error.Message.Should().Contain("Verify");
    }
 
    [Fact]
    public async Task Handle_ScanAlreadyRunning_ReturnsExistingScanWithoutCreatingNew()
    {
        var domain = Fakes.TestScannedDomain(_userId, status: VerificationStatus.Verified);
        var runningScan = Fakes.TestScan(_userId, domain.Id, ScanStatus.Running);
        _domainRepo.Setup(r => r.FindUserDomainByName(_userId, "example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(domain);
        _scanRepo.Setup(r => r.FindByIdempotencyKey(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Scan?)null);
        _scanRepo.Setup(r => r.FindRunningByDomain(domain.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runningScan);
 
        var result = await _sut.Handle(
            new StartScanCommand("example.com", ScanCoverage.Quick, SurfaceType.Dns, Guid.NewGuid()),
            CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.ScanId.Should().Be(runningScan.Id);
        result.Value.Message.Should().Contain("already in progress");
        _redis.Verify(r => r.PublishScanJob(It.IsAny<string>(), It.IsAny<ScanJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }
 
    [Fact]
    public async Task Handle_DuplicateIdempotencyKey_ReturnsExistingScanImmediately()
    {
        var domain = Fakes.TestScannedDomain(_userId, status: VerificationStatus.Verified);
        var existingScan = Fakes.TestScan(_userId, domain.Id, ScanStatus.Queued);
        var idempotencyKey = Guid.NewGuid();
 
        _domainRepo.Setup(r => r.FindUserDomainByName(_userId, "example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(domain);
        _scanRepo.Setup(r => r.FindByIdempotencyKey(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingScan);
 
        var result = await _sut.Handle(
            new StartScanCommand("example.com", ScanCoverage.Quick, SurfaceType.Dns, idempotencyKey),
            CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.ScanId.Should().Be(existingScan.Id);
        result.Value.Message.Should().Contain("already initiated");
        _scanRepo.Verify(r => r.AddAsync(It.IsAny<Scan>(), It.IsAny<CancellationToken>()), Times.Never);
        _redis.Verify(r => r.PublishScanJob(It.IsAny<string>(), It.IsAny<ScanJob>(), It.IsAny<CancellationToken>()), Times.Never); 
    }
}
 
public class GetScanReportHandlerTests
{
    private readonly Mock<IScanRepository> _scanRepo;
    private readonly Mock<IDomainRepository> _domainRepo;
    private readonly Mock<ICurrentUser> _currentUser;
    private readonly GetScanReportHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();
 
    public GetScanReportHandlerTests()
    {
        _scanRepo = new Mock<IScanRepository>();
        _domainRepo = new Mock<IDomainRepository>();
        _currentUser = new Mock<ICurrentUser>();
        _currentUser.Setup(c => c.UserId).Returns(_userId);
        _sut = new GetScanReportHandler(_scanRepo.Object, _domainRepo.Object, _currentUser.Object);
    }
 
    [Fact]
    public async Task Handle_CompletedScanWithFindings_ReturnsFullReport()
    {
        var domainId = Guid.NewGuid();
        var scan = Fakes.TestScan(_userId, domainId, ScanStatus.Completed, score: 65);
        var domain = Fakes.TestScannedDomain(_userId);
 
        // Attach findings to scan via reflection (private setter)
        var findingField = typeof(Scan).GetField("_findings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
 
        var findings = new List<Finding>
        {
            Fakes.TestFinding(scan.Id, FindingSurface.Ssl, FindingSeverity.Critical),
            Fakes.TestFinding(scan.Id, FindingSurface.Dns, FindingSeverity.High),
            Fakes.TestFinding(scan.Id, FindingSurface.HttpHeaders, FindingSeverity.Low),
        };
 
        // Use the ICollection navigation property directly
        foreach (var f in findings)
            ((ICollection<Finding>)scan.Findings).Add(f);
 
        _scanRepo.Setup(r => r.FindByIdWithFindings(scan.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
        _domainRepo.Setup(r => r.GetById(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(domain);
 
        var result = await _sut.Handle(new GetScanReportQuery(scan.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.SecurityScore.Should().Be(65);
        result.Value.FindingGroups.CriticalCount.Should().Be(1);
        result.Value.FindingGroups.HighCount.Should().Be(1);
        result.Value.FindingGroups.LowCount.Should().Be(1);
        result.Value.RiskLevel.Should().Be("moderate");
    }
 
    [Fact]
    public async Task Handle_ScanNotFound_ReturnsNotFound()
    {
        _scanRepo.Setup(r => r.FindByIdWithFindings(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Scan?)null);
 
        var result = await _sut.Handle(new GetScanReportQuery(Guid.NewGuid()), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
 
    [Fact]
    public async Task Handle_ScanBelongingToOtherUser_ReturnsNotFound()
    {
        var scan = Fakes.TestScan(Guid.NewGuid(), null, ScanStatus.Completed); // different userId
        _scanRepo.Setup(r => r.FindByIdWithFindings(scan.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
 
        var result = await _sut.Handle(new GetScanReportQuery(scan.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
 
    [Fact]
    public async Task Handle_ScanNotCompleted_ReturnsValidationError()
    {
        var scan = Fakes.TestScan(_userId, null, ScanStatus.Running);
        _scanRepo.Setup(r => r.FindByIdWithFindings(scan.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
 
        var result = await _sut.Handle(new GetScanReportQuery(scan.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.Validation);
        result.Error.Message.Should().Contain("not complete");
    }
 
    [Theory]
    [InlineData(80, "safe")]
    [InlineData(65, "moderate")]
    [InlineData(40, "at_risk")]
    [InlineData(0, "at_risk")]   // null score maps to 80 via Fakes.Scan default; use 0 for at_risk
    public async Task Handle_SecurityScore_MapsToCorrectRiskLevel(int score, string expectedRisk)
    {
        var scan = Fakes.TestScan(_userId, Guid.NewGuid(), ScanStatus.Completed, score);
        var domain = Fakes.TestScannedDomain(_userId);
        _scanRepo.Setup(r => r.FindByIdWithFindings(scan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(scan);
        _domainRepo.Setup(r => r.GetById(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(domain);
 
        var result = await _sut.Handle(new GetScanReportQuery(scan.Id), CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.RiskLevel.Should().Be(expectedRisk);
    }
}
 
public class GetScanHistoryHandlerTests
{
    private readonly Mock<IScanRepository> _scanRepo;
    private readonly Mock<IDomainRepository> _domainRepo;
    private readonly Mock<ICurrentUser> _currentUser;
    private readonly Mock<IHttpContextAccessor> _http;
    private readonly GetScanHistoryHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();
 
    public GetScanHistoryHandlerTests()
    {
        _scanRepo = new Mock<IScanRepository>();
        _domainRepo = new Mock<IDomainRepository>();
        _currentUser = new Mock<ICurrentUser>();
        _http = new Mock<IHttpContextAccessor>();
 
        _currentUser.Setup(c => c.UserId).Returns(_userId);
 
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/scans";
        _http.Setup(h => h.HttpContext).Returns(httpContext);
 
        _sut = new GetScanHistoryHandler(_scanRepo.Object, _domainRepo.Object, _currentUser.Object, _http.Object);
    }
 
    [Fact]
    public async Task Handle_DomainNotOwnedByUser_ReturnsNotFound()
    {
        _domainRepo.Setup(r => r.FindUserDomainById(_userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScannedDomain?)null);
 
        var result = await _sut.Handle(
            new GetScanHistoryQuery(Guid.NewGuid(), null, null),
            CancellationToken.None);
 
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }
 
    [Fact]
    public async Task Handle_ValidDomain_ReturnsPaginatedResults()
    {
        var domain = Fakes.TestScannedDomain(_userId);
        var scans = new List<Scan>
        {
            Fakes.TestScan(_userId, domain.Id, ScanStatus.Completed, 90),
            Fakes.TestScan(_userId, domain.Id, ScanStatus.Failed),
        };
 
        _domainRepo.Setup(r => r.FindUserDomainById(_userId, domain.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(domain);
        _scanRepo.Setup(r => r.GetPaged(It.IsAny<ScanFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((scans, 2));
 
        var result = await _sut.Handle(
            new GetScanHistoryQuery(domain.Id, null, null, Page: 1, PageSize: 10),
            CancellationToken.None);
 
        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
    }
}