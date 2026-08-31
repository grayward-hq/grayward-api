using Application.Features.Scans.DTOs;
using Application.Mappers;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Tests.Application.Scan;

/// <summary>
/// Guards the scan-job payload contract between the API and the Java worker.
/// </summary>
/// <remarks>
/// These two sides drifted silently for months: the API sent a single flags value under a singular
/// "SurfaceType" key while the worker read a JSON array from "SurfaceTypes", so the worker saw null
/// and quietly ran every scanner on every job. Nothing failed and nothing logged an error, which is
/// why it survived so long. The point of these tests is that the next drift breaks a build.
/// </remarks>
public class ScanJobContractTests
{
    /// <summary>
    /// Every member must own a distinct bit, or a mask cannot be decomposed. Http = 3 was Dns | Ssl,
    /// which is the defect this whole change exists to fix.
    /// </summary>
    [Fact]
    public void SurfaceType_members_are_distinct_powers_of_two()
    {
        var seen = new List<int>();

        foreach (var value in Enum.GetValues<SurfaceType>())
        {
            if (value == SurfaceType.None) continue;

            var bits = (int)value;
            (bits & (bits - 1)).Should().Be(0, $"{value} = {bits} must be a single bit");
            seen.Should().NotContain(bits, $"{value} = {bits} collides with an earlier member");
            seen.Add(bits);
        }
    }

    /// <summary>
    /// Names are what travel on the wire, and the worker resolves them against its own enum. If a
    /// member is renamed on either side without the other, jobs silently lose that surface.
    /// </summary>
    [Fact]
    public void SurfaceType_names_match_the_worker_enum()
    {
        // Mirrors com.vulnwatch.worker.enums.SurfaceType.
        var workerSurfaces = new[]
        {
            "Dns", "Ssl", "HttpHeaders", "Dependency", "Secrets", "Subdomains", "Ports"
        };

        Enum.GetValues<SurfaceType>()
            .Where(v => v != SurfaceType.None)
            .Select(v => v.ToString())
            .Should().BeEquivalentTo(workerSurfaces);
    }

    [Fact]
    public void Factory_decomposes_a_combined_mask_into_every_surface()
    {
        var job = CreateJobWith(SurfaceType.Dns | SurfaceType.Ssl | SurfaceType.HttpHeaders);

        job.SurfaceTypes.Should().BeEquivalentTo("Dns", "Ssl", "HttpHeaders");
    }

    [Fact]
    public void Factory_emits_a_single_surface_as_a_one_element_list()
    {
        CreateJobWith(SurfaceType.Dns).SurfaceTypes.Should().BeEquivalentTo("Dns");
    }

    /// <summary>None means nothing was requested, and must not become an implicit "scan everything".</summary>
    [Fact]
    public void Factory_emits_an_empty_list_for_None()
    {
        CreateJobWith(SurfaceType.None).SurfaceTypes.Should().BeEmpty();
    }

    /// <summary>
    /// The historical mask: Dns | Ssl | Http collapsed to 3 under the old values and is migrated to
    /// 7. It must decompose to all three surfaces, not to HttpHeaders alone.
    /// </summary>
    [Fact]
    public void Migrated_historical_mask_decomposes_to_all_three_surfaces()
    {
        ((int)(SurfaceType.Dns | SurfaceType.Ssl | SurfaceType.HttpHeaders)).Should().Be(7);

        CreateJobWith((SurfaceType)7).SurfaceTypes
            .Should().BeEquivalentTo("Dns", "Ssl", "HttpHeaders");
    }

    private static ScanJob CreateJobWith(SurfaceType surfaces)
    {
        var scan = global::Domain.Entities.Scan.Create(
            userId: Guid.NewGuid(),
            idempotencyKey: Guid.NewGuid(),
            targetType: ScanTargetType.Domain,
            coverage: ScanCoverage.Full,
            surfaceTypes: surfaces,
            domainId: Guid.NewGuid());

        return new ScanJobFactory().Create(scan);
    }
}