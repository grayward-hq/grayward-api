using Application.Features.Auth;
using Application.Features.Scans;
using Domain.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Tests.Application.Scan.Validators;

public class StartScanCommandValidatorTests
{
    private readonly StartScanCommandValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var result = _sut.Validate(new StartScanCommand(
            Guid.NewGuid(),
            ScanTargetType.Domain,
            ScanCoverage.Quick,
            SurfaceType.Dns | SurfaceType.Ssl,
            Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyTargetId_HasTargetIdError()
    {
        _sut.ShouldHaveValidationErrorFor(
            x => x.TargetId,
            new StartScanCommand(
                Guid.Empty,
                ScanTargetType.Domain,
                ScanCoverage.Quick,
                SurfaceType.Dns,
                Guid.NewGuid()));
    }

    [Fact]
    public void Validate_InvalidTargetType_HasTargetTypeError()
    {
        _sut.ShouldHaveValidationErrorFor(
            x => x.TargetType,
            new StartScanCommand(
                Guid.NewGuid(),
                (ScanTargetType)999,
                ScanCoverage.Quick,
                SurfaceType.Dns,
                Guid.NewGuid()));
    }

    [Fact]
    public void Validate_InvalidCoverage_HasCoverageError()
    {
        _sut.ShouldHaveValidationErrorFor(
            x => x.Coverage,
            new StartScanCommand(
                Guid.NewGuid(),
                ScanTargetType.Domain,
                (ScanCoverage)999,
                SurfaceType.Dns,
                Guid.NewGuid()));
    }

    [Fact]
    public void Validate_NoSurfaceTypes_HasSurfaceTypesError()
    {
        _sut.ShouldHaveValidationErrorFor(
            x => x.SurfaceTypes,
            new StartScanCommand(
                Guid.NewGuid(),
                ScanTargetType.Domain,
                ScanCoverage.Quick,
                0,
                Guid.NewGuid()));
    }

    [Fact]
    public void Validate_EmptyIdempotencyKey_HasIdempotencyKeyError()
    {
        _sut.ShouldHaveValidationErrorFor(
            x => x.IdempotencyKey,
            new StartScanCommand(
                Guid.NewGuid(),
                ScanTargetType.Domain,
                ScanCoverage.Quick,
                SurfaceType.Dns,
                Guid.Empty));
    }

    [Theory]
    [InlineData(ScanTargetType.Domain)]
    [InlineData(ScanTargetType.Repository)]
    public void Validate_ValidTargetTypes_NoErrors(ScanTargetType targetType)
    {
        var result = _sut.Validate(new StartScanCommand(
            Guid.NewGuid(),
            targetType,
            ScanCoverage.Quick,
            SurfaceType.Dns,
            Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
