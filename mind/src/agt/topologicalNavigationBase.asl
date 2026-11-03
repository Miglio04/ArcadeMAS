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

