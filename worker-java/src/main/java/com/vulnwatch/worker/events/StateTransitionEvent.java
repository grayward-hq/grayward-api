package com.vulnwatch.worker.events;

/** Payload published on every state transition. */
public record StateTransitionEvent(
        String scanId,
        String domainId,
        String domainName,
        String requestedBy,
        String status,
        String timestamp
) {}
