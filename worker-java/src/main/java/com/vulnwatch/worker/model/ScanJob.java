package com.vulnwatch.worker.model;

import com.fasterxml.jackson.annotation.JsonProperty;

public record ScanJob(
    @JsonProperty("DomainId")   String domainId,
    @JsonProperty("DomainName")  String domainName,
    @JsonProperty("RepoId") String repoId,
    @JsonProperty("ScanId")  String scanId,
    @JsonProperty("ScanType")  String scanType,
    @JsonProperty("SurfaceType") String surfaceType,
    @JsonProperty("RequestedBy") String requestedBy,
    @JsonProperty("EnqueuedAt")  String enqueuedAt
) {}