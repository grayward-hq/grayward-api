package com.vulnwatch.worker.enums;

/**
 * Why a surface scanner or AI enrichment step failed.
 * Stored in Redis surface state and included in DLQ payloads
 * so C# and operators understand failures without parsing logs.
 */
public enum FailureReason {

    /** Scanner threw an exception (DNS lookup, SSL handshake, HTTP probe failure). */
    SCANNER_ERROR,

    /** AI enrichment call failed — model error, timeout, or malformed response. */
    AI_ERROR,

    /**
     * Operation timed out before completing.
     * Separate from SCANNER_ERROR so retry backoff can be tuned independently.
     */
    TIMEOUT,

    /** Redis payload could not be deserialized into a ScanJob. */
    DESERIALIZATION_ERROR,

    /** Catch-all. Always investigate when this appears in DLQ. */
    UNKNOWN
}