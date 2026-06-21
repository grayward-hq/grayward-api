using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;

namespace Application.Catalogs;

public sealed class PlanCatalog : IPlanCatalog
{
    private static readonly Dictionary<PlanCode, Plan> Plans = new()
    {
        [PlanCode.Free] = new(
            Code: PlanCode.Free,
            Domains: new ResourceLimits(
                MaxOnboarded: 2,
                MaxScansPerMonth: 20,
                MaxConcurrentScans: 1,
                MinScanInterval: TimeSpan.FromDays(2)),
            Repositories: new ResourceLimits(
                MaxOnboarded: 2,
                MaxScansPerMonth: 10,
                MaxConcurrentScans: 1,
                MinScanInterval: TimeSpan.FromDays(2)),
            AllowedChannels: new HashSet<AlertChannel>
            {
                AlertChannel.Email
            }),

        [PlanCode.Pro] = new(
            Code: PlanCode.Pro,
            Domains: new ResourceLimits(
                MaxOnboarded: 10,
                MaxScansPerMonth: 100,
                MaxConcurrentScans: 10,
                MinScanInterval: TimeSpan.FromHours(6)),
            Repositories: new ResourceLimits(
                MaxOnboarded: 10,
                MaxScansPerMonth: 100,
                MaxConcurrentScans: 10,
                MinScanInterval: TimeSpan.FromHours(6)),
            AllowedChannels: new HashSet<AlertChannel>
            {
                AlertChannel.Email,
                AlertChannel.Slack
            }),

        [PlanCode.Enterprise] = new(
            Code: PlanCode.Enterprise,
            Domains: new ResourceLimits(
                MaxOnboarded: int.MaxValue,
                MaxScansPerMonth: int.MaxValue,
                MaxConcurrentScans: 25,
                MinScanInterval: TimeSpan.FromHours(1)),
            Repositories: new ResourceLimits(
                MaxOnboarded: int.MaxValue,
                MaxScansPerMonth: int.MaxValue,
                MaxConcurrentScans: 25,
                MinScanInterval: TimeSpan.FromHours(1)),
            AllowedChannels: new HashSet<AlertChannel>
            {
                AlertChannel.Email,
                AlertChannel.Slack
            }),
    };

    public Plan Get(PlanCode code) => Plans[code];
}