using Application.Features.Scans.DTOs;
using Domain.Entities;

namespace Application.Interfaces;

public interface IScanJobFactory
{
    ScanJob Create(Scan scan);
}