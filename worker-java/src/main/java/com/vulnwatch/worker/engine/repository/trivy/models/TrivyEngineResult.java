package com.vulnwatch.worker.engine.repository.trivy.models;

public record TrivyEngineResult(
        String packageName,
        String installedVersion,
        String fixedVersion,
        String title,
        String severity,
        String secretLocation,
        String category,
        Integer startLine,
        Integer endLine
) {
    public static TrivyEngineResult dependencyVulnerability(String packageName,
                                                            String installedVersion,
                                                            String fixedVersion,
                                                            String title,
                                                            String severity){
        return new TrivyEngineResult(packageName, installedVersion, fixedVersion, title, severity, null, null, null, null);
    }

    public static TrivyEngineResult secretFinding(String title,
                                                            String severity,
                                                            String secretLocation,
                                                            String category,
                                                            int startLine,
                                                            int endLine){
        return new TrivyEngineResult(null, null, null, title, severity, secretLocation, category, startLine, endLine);
    }

}
