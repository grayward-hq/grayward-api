package com.vulnwatch.worker.engine.domain.subfinder.utility;

import com.vulnwatch.worker.engine.domain.subfinder.classifiers.Classifier;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import com.vulnwatch.worker.enums.FindingSeverity;

import java.util.List;

public class SubdomainRiskAggregator {
    public SubdomainFindings aggregate(SubdomainRecord record, List<Classifier> classifiers){
        SubdomainFindings.SubdomainFindingsBuilder builder = SubdomainFindings.builder().record(record);
        FindingSeverity highest = FindingSeverity.NONE;

        for(Classifier classifier:classifiers){
            FindingSeverity severity = classifier.classify(record, builder);

            if(severity.isAtLeast(highest)){
                highest = severity;
            }
        }
        return builder
                .risk(highest)
                .build();
    }
}
