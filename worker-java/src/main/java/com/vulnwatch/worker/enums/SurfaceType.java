package com.vulnwatch.worker.enums;

import com.vulnwatch.worker.exception.InvalidSurfaceTypeException;
import io.swagger.v3.oas.annotations.media.Schema;
import lombok.Getter;

@Getter
@Schema(description = "Type of security surface being scanned")
public enum SurfaceType {

    @Schema(description = "DNS records and configurations")
    DNS("Dns"),

    @Schema(description = "SSL/TLS certificates and protocols")
    SSL("Ssl"),

    @Schema(description = "HTTP security headers")
    HTTP_HEADERS("HttpHeaders"),

    @Schema(description = "Dependency vulnerabilities")
    DEPENDENCY("Dependency"),

    @Schema(description = "Hardcoded secrets and credentials")
    SECRETS("Secrets");

    private final String label;

    SurfaceType(String name) {
        this.label = name;
    }

    /**
     * Lookup by enum constant name, case-insensitive.
     * e.g. fromString("dns") → DNS, fromString("HTTP_HEADERS") → HTTP_HEADERS
     */
    public static SurfaceType fromString(String value) {
        if (value == null) {
            throw new InvalidSurfaceTypeException("Surface type cannot be null");
        }
        for (SurfaceType type : SurfaceType.values()) {
            if (type.name().equalsIgnoreCase(value.trim())) {
                return type;
            }
        }
        throw new InvalidSurfaceTypeException("Unknown surface type: %s".formatted(value));
    }

    /**
     * Lookup by the label field — matches EngineResult.surface() values.
     * e.g. fromLabel("Dns") → DNS, fromLabel("HttpHeaders") → HTTP_HEADERS
     * Use this when parsing EngineResult or DB surface strings.
     */
    public static SurfaceType fromLabel(String label) {
        if (label == null) {
            throw new InvalidSurfaceTypeException("Surface label cannot be null");
        }
        for (SurfaceType type : SurfaceType.values()) {
            if (type.label.equalsIgnoreCase(label.trim())) {
                return type;
            }
        }
        throw new InvalidSurfaceTypeException("Unknown surface label: %s".formatted(label));
    }
}