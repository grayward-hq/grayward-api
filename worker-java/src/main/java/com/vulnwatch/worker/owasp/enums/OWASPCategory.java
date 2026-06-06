package com.vulnwatch.worker.owasp.enums;

import lombok.Data;
import lombok.Getter;
import lombok.RequiredArgsConstructor;

@RequiredArgsConstructor
@Getter
public enum OWASPCategory {
    /**
     * Controls restricting what authenticated users can do are missing or misconfigured.
     */
    BROKEN_ACCESS_CONTROL(
            "A01", "Broken Access Control",
            "Controls restricting what authenticated users can do are missing or misconfigured."
    ),

    /**
     * Sensitive data exposed due to weak encryption, expired certificates or insecure protocols
     */
    CRYPTOGRAPHIC_FAILURES(

            "A02", "Cryptographic Failures",

            "Sensitive data is exposed due to weak encryption, expired certificates, or insecure protocols."

    ),


    /**
     * Untrusted data sent to an interpreter as part of a query
     */
    INJECTION(

            "A03", "Injection",

            "Untrusted data is sent to an interpreter as part of a command or query."

    ),


    /**
     * Ineffective security controls due to flawed design
     */
    INSECURE_DESIGN(

            "A04", "Insecure Design",

            "Missing or ineffective security controls due to fundamentally flawed design."

    ),


    /**
     * Insufficient security settings accross the application stack
     */
    SECURITY_MISCONFIGURATION(

            "A05", "Security Misconfiguration",

            "Missing, incorrect, or default security settings across the application stack."

    ),


    /**
     * Broken identification, authorisation and session management functions
     */
    AUTH_FAILURES(
            "A07", "Identification and Authentication Failures",
            "Functions related to identity, authentication, and session management are broken."
    ),

    /**
     * Vulnerable and Outdated Components
     */
    VULNERABLE_COMPONENTS(
            "A06", "Vulnerable and Outdated Components",
            "Security risks increase when components run with known vulnerabilities or are no longer supported."
    );

    private final String code;
    private final String displayName;
    private final String description;

}
