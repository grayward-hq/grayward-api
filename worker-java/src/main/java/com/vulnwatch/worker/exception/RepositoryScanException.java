package com.vulnwatch.worker.exception;

/** Signals a retryable failure during repository scan execution (clone, auth, Trivy invocation). */
public class RepositoryScanException extends RuntimeException {
    public RepositoryScanException(String message, Throwable cause) {
        super(message, cause);
    }

    public RepositoryScanException(String message) {
        super(message);
    }
}