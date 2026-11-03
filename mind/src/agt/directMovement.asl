
+!go_to(Target, "") <- 
    !!reach_dest(Target);
    .wait({ +reached(place, Target) });
    -movement_in_progress(Target).

+!go_to(_, Artifact) <- 
    !!reach_dest(Artifact);
    .wait({ +reached(place, Artifact) });
    -movement_in_progress(Artifact).