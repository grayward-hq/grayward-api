package com.vulnwatch.worker.engine.domain.subfinder.classifiers;


import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import com.vulnwatch.worker.enums.FindingSeverity;

public interface Classifier {
    FindingSeverity classify(SubdomainRecord record, SubdomainFindings.SubdomainFindingsBuilder builder);
}
