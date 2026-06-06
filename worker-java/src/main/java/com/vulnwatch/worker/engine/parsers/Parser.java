package com.vulnwatch.worker.engine.parsers;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;

import java.io.File;
import java.io.IOException;

public interface Parser<T> {
    T parse(File file) throws IOException;

}
