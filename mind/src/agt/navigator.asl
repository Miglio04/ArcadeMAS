// vesna.asl
// Overwrites immediately the random walk plan
+!start_walking <- true.


name("navigator").

// ── Instruction loop: checks periodically for new targets ─────────────────
+!check_target_loop: currentRoom(Start) & start \== [] <-
    .wait(2000);
    if (vesna.CheckTarget(Target, Artifact)) {
        .print("[vesna] nuovo comando ricevuto: ", Target, " (oggetto: ", Artifact, ")");
        !go_to(Target, Artifact);
    };
    !check_target_loop.

+!check_target_loop <- .wait(2000);
    !check_target_loop.

