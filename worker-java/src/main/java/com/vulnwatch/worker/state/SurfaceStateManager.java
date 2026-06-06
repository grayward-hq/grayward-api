package com.vulnwatch.worker.state;

import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.state.SurfaceStateSnapshot;

import java.util.Map;

/**
 * Contract for tracking per-surface state within a scan job.
 * Backed by Redis — each surface (DNS, SSL, HTTP) is tracked
 * independently so failures in one never block the others.
 */
public interface SurfaceStateManager {

    /**
     * Initialises all provided surfaces to PENDING for the given scan.
     * Called once when a job is first picked up from the queue.
     */
    void initSurfaces(String scanId, java.util.List<SurfaceType> surfaces);

    /**
     * Transitions a surface to a new state.
     * Records the transition timestamp in Redis.
     */
    void transition(String scanId, SurfaceType surface, SurfaceStatus newState);

    /**
     * Transitions a surface to a failure state with a reason.
     * Used by ScannerRetryPolicy and DeadLetterQueueHandler.
     */
    void transitionFailed(String scanId, SurfaceType surface,
                          SurfaceStatus newState, FailureReason reason);

    /**
     * Increments the retry count for a surface and returns the new count.
     */
    int incrementRetryCount(String scanId, SurfaceType surface);

    /**
     * Returns the current state snapshot for a single surface.
     */
    SurfaceStateSnapshot getSnapshot(String scanId, SurfaceType surface);

    /**
     * Returns state snapshots for all surfaces in a scan.
     * Key = SurfaceType, Value = snapshot.
     */
    Map<SurfaceType, SurfaceStateSnapshot> getAllSnapshots(String scanId);

    /**
     * Returns true when every initialised surface has reached a terminal state
     * (SUCCESS, SUCCESS_NO_AI, or PERMANENTLY_FAILED).
     * Used by ScanJobStateMachine to decide when to advance to COMPLETED/FAILED.
     */
    boolean allTerminal(String scanId);

    /**
     * Removes all surface state keys for a scan.
     * Called by CheckpointManager after the job fully completes.
     */
    void clear(String scanId);
}