package com.vulnwatch.worker.engine.domain.dnsrecon.model;

import java.util.List;

public record ScanContext(
        List<String> aRecordList,
        List<String> aaaaRecordList,
        List<String> nsRecordList,
        List<String> mxRecordList,
        List<String> soaRecordList,
        List<String> txtRecordList,
        List<String> dnsKeyRecordList,
        List<String> rrisgRecordList

) {
    public List<String> getDmarcRecords(){
        return txtRecordList.stream()
                .filter(r -> r.contains("dmarc")&&r.contains("v=DMARC1"))
                .toList();
    }

}
