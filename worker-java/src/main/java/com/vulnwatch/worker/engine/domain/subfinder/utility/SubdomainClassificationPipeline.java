package com.vulnwatch.worker.engine.domain.subfinder.utility;

import com.vulnwatch.worker.engine.domain.subfinder.classifiers.Classifier;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;

import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;

public class SubdomainClassificationPipeline {
    private final List<Classifier> classifiers;

    public SubdomainClassificationPipeline(List<Classifier> classifiers) {
        this.classifiers = List.copyOf(classifiers);
    }

    public List<SubdomainFindings> process(List<SubdomainRecord> records) {
        SubdomainRiskAggregator aggregator = new SubdomainRiskAggregator();
        return records.stream()
                .map(record -> aggregator.aggregate(record, classifiers))
                .sorted(Comparator.comparing(SubdomainFindings::getRisk))
                .collect(Collectors.toList());
    }
}
