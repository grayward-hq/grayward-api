package com.vulnwatch.worker.engine.domain.subfinder.models;

import com.vulnwatch.worker.enums.FindingSeverity;
import lombok.Builder;
import lombok.Getter;
import lombok.Singular;

import java.util.List;
import java.util.Set;

@Builder
@Getter
public class SubdomainFindings {
    private SubdomainRecord record;

    @Builder.Default
    private FindingSeverity risk = FindingSeverity.NONE;

    @Singular
    private List<String> tags;

    @Singular
    private List<String> notes;
}
