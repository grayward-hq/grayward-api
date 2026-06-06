package com.vulnwatch.worker;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.builder.SpringApplicationBuilder;

/**
 * Main entry point for the VulnWatch Worker application.
 * <p>
 * This class initialises the Spring Boot context. It is explicitly configured
 * to run as a non-web application to optimise resource utilisation for background
 * task processing.
 * </p>
 */
@SpringBootApplication
public class Application {

    /**
     * Starts the distributed vulnerability scanning background worker.
     * <p>
     * Employs {@link SpringApplicationBuilder} to strictly enforce
     * {@link WebApplicationType#NONE}, ensuring that no embedded servlet container
     * (like Tomcat) is spun up during execution.
     * </p>
     *
     * @param args command-line arguments passed to the application
     */
    public static void main(String[] args) {
        new SpringApplicationBuilder(Application.class)
                .web(WebApplicationType.NONE)   // ← guarantees NONE mode in code
                .run(args);
    }
}