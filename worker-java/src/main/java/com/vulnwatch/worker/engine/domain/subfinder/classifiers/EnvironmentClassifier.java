package com.vulnwatch.worker.engine.domain.subfinder.classifiers;

import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import com.vulnwatch.worker.enums.FindingSeverity;

import java.util.regex.Pattern;

public class EnvironmentClassifier implements Classifier {

    private static final Pattern STAGING = Pattern.compile("stag|staging|stg|qc|demo|test");
    private static final Pattern PROD    = Pattern.compile("prod|live|nexus");

    @Override
    public FindingSeverity classify(SubdomainRecord record, SubdomainFindings.SubdomainFindingsBuilder builder) {
        String h = record.host().toLowerCase();
        if (STAGING.matcher(h).find()) builder.tag("staging");
        if (PROD.matcher(h).find())    builder.tag("production");

        return builder.build().getRisk();
    }
}
