// vesna.asl
// Overwrites immediately the random walk plan
+!start_walking <- true.


{ include("topologicalNavigationBase.asl") }


name("navigator").

// ── Start: connects to the envManager and starts instruction loop ─────────
!start.

+!start <-
    .wait(2000);
    .print("[vesna] avviata, aggancio envManager...");
    !ensure_env_manager;
    .print("[vesna] attendo grafo pronto...");
    !wait_graph_ready;
    .print("[vesna] attendo posizione iniziale...");
    .wait({+current_room(_)}, 60000, _);
    ?current_room(Start);
    .print("[vesna] posizione iniziale: ", Start);
    !check_target_loop.

+!wait_graph_ready : graphReady(true).
+!wait_graph_ready <-
    .wait({+graphReady(true)}, 120000, _).

// ── Instruction loop: checks periodically for new targets ─────────────────
+!check_target_loop <-
    .wait(2000);
    if (vesna.CheckTarget(Target, Artifact)) {
        .print("[vesna] nuovo comando ricevuto: ", Target, " (oggetto: ", Artifact, ")");
        !go_to(Target, Artifact);
    };
    !check_target_loop.

+current_room(_) : at(_) <- true. // at è inutile?
+current_room(Where) <-
    +at(Where);
    .print("[vesna] posizione iniziale: ", Where).

// ── Main goal: move towards the Target ─────────────────────────────────────

// If the agent is already moving, ignore the instruction
+!go_to(Target, _) : movement_in_progress(ActualDest) <-
    .print("[vesna] Comando ignorato: movimento in corso verso ", ActualDest).

// valid instruction: navigate towards the room, then (if the agent has to
// move towards an artifact) get near the object.
+!go_to(Target, Artifact) : at(Start) & env_manager(EnvId) <-
    .print("[vesna] da ", Start, " verso ", Target);
    computePath(Start, Target, ResolvedGoal, Path, Cost)[artifact_id(EnvId)];
    if (Path == []) {
        if (not path_attempts(_)) { +path_attempts(0) };
        ?path_attempts(N);
        if (N >= 15) {
            -path_attempts(_);
            .print("[vesna] ERRORE: nessun percorso verso ", Target, " dopo ", N, " tentativi");
        } else {
            M = N + 1; -+path_attempts(M);
            .wait(1000);
            !go_to(Target, Artifact);
        };
    } else {
        -path_attempts(_);                   // reset in case of success
        .print("[vesna] percorso verso ", ResolvedGoal, " (", Cost, "m): ", Path);
        Path = [_First|StepsToWalk];
        !follow_path(StepsToWalk);
        .print("[vesna] arrivata alla stanza: ", Target);
        !final_approach(Target, Artifact);
    }.

+!go_to(Target, _) <-
    .print("[vesna] posizione non nota o envManager non pronto, ignoro: ", Target).

