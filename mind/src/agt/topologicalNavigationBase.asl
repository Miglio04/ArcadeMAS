{ include("libraryPlans.asl") }
{ include("initialGoalsPlan.asl") }

// Overwrites the plan for random walk
+!start_walking <- true.


+!ensure_env_manager <-
	joinWorkspace("/main/main", _);
    lookupArtifact("envManager", EnvId);
    focus(EnvId);
    +env_manager(EnvId);
    ?name(N);
    .print("[", N, "] envManager agganciato: ", EnvId).

-!ensure_env_manager <-
    ?name(N);
    .print("[", N, "] ensure_env_manager fallito, ritento...");
    .wait(500);
    !ensure_env_manager.

// ── Object approach ───────────────────────
// if the target is a room, the agent doesn't look for an object and stops
+!final_approach(_, "") <- true.

// If the target is an artifact, the agent looks for the object via its ID.
// fire-and-forget: this approach doesn't expect +reached(place, Artifact).
+!final_approach(Room, Artifact) <-
    !!reach_dest(Artifact);
    -movement_in_progress(Artifact);
    .abolish(at(_));
    +at(Room);
    ?name(N);
    .print("[", N, "] arrivato a ", Artifact, ", stanza corrente: ", Room).


// intermediate node
+!follow_path([Next | Rest]) : Rest \== [] <-
    ?name(N);
    .print("[", N, "] -> ", Next);
    !!reach_dest(Next);
    .wait({ +reached(place, Next) });
    -movement_in_progress(Next);
    .abolish(at(_)); +at(Next);
    .print("[", N, "] arrivato in: ", Next);
    !follow_path(Rest).

// last node
+!follow_path([Last]) <-
    ?name(N);
    .print("[", N, "] -> ", Last);
    !!reach_dest(Last);
    .wait({ +reached(place, Last) });
    -movement_in_progress(Last);
    .abolish(at(_)); +at(Last);
    .print("[", N, "] sei a destinazione. Percorso completato").
+!follow_path([]).