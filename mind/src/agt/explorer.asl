// ============================================================
// explorer.asl
// ============================================================

// include base for plans that are shared between agents
{ include("topologicalNavigationBase.asl") }

name("explorer").

!start.

+!start <-
    !ensure_env_manager;
    .print("[explorer] pronto, ricevo il grafo da Unity...").


// ── Helper: awaits for the envManager if it's not available yet ───────────
+!get_env(E) : env_manager(E).
+!get_env(E) <-
    .wait({+env_manager(_)}, 10000, _);
    ?env_manager(E).

// ── Unity graph reception ─────────────────────────────────────────────────
// Holds the graph information until exploration_completed is executed.

+node(Id, Type, Floor, X, Y, Z, CorridorId, PoleLabel, WsPort) <-
    .print("[explorer] nodo: ", Id).

+edge(GoName, From, To, Floor, EdgeType, DoorState, Side, Dir, DistA, DistB, WsPort, IsPhysical) <-
    .print("[explorer] arco: ", GoName, " da ", From, " a ", To).

+connector_link(Id, FloorA, FloorB, Bidirectional, PosAx, PosAy, PosAz, PosBx, PosBy, PosBz) <-
    .print("[explorer] connettore: ", Id, " piano ", FloorA, " <-> ", FloorB).

+door_dist(RoomA, RoomB, Floor, Dist) <-
    true.

+corridor(CorridorId, PoleA, PoleB, Floor, WsPort) <-
    .print("[explorer] corridoio: ", CorridorId);
    ?node(PoleA, _, _, X1, Y1, Z1, _, _, _);
    ?node(PoleB, _, _, X2, Y2, Z2, _, _, _);
    makeArtifact(CorridorId, "artifact.CorridorArtifact",
                 [CorridorId, WsPort, X1, Y1, Z1, X2, Y2, Z2], _).

+new_object(ArtifactId, RoomId, ArtifactType, WsPort, X, Y, Z) <-
    .print("[explorer] oggetto: ", ArtifactId);
    makeArtifact(ArtifactId, "artifact.RoomObjectArtifact",
                 [ArtifactId, WsPort, RoomId, X, Y, Z], Handle);
    focus(Handle).

// ── Graph completed: stored information is sent in a single batch ─────────
+exploration_complete(BuildingId, _, _, TotalNodes, TotalEdges, _) <-
    .print("[explorer] grafo completo: ", TotalNodes, " nodi, ", TotalEdges, " archi, invio in blocco...");
    !get_env(E);
    // -- Non connector nodes --
    .findall(Id,    node(Id,_,_,_,_,_,_,_,_),    NIds);
    .findall(Type,  node(_,Type,_,_,_,_,_,_,_),  NTypes);
    .findall(Floor, node(_,_,Floor,_,_,_,_,_,_), NFloors);
    .findall(X,     node(_,_,_,X,_,_,_,_,_),     NXs);
    .findall(Y,     node(_,_,_,_,Y,_,_,_,_),     NYs);
    .findall(Z,     node(_,_,_,_,_,Z,_,_,_),     NZs);

    // -- Connector nodes --
    .findall(Id2,          connector_link(Id2,_,_,_,_,_,_,_,_,_),         CIds);
    .findall("Connector",  connector_link(_,_,_,_,_,_,_,_,_,_),           CTypes);
    .findall(FloorA,       connector_link(_,FloorA,_,_,_,_,_,_,_,_),      CFloors);
    .findall(PosAx,        connector_link(_,_,_,_,PosAx,_,_,_,_,_),       CXs);
    .findall(PosAy,        connector_link(_,_,_,_,_,PosAy,_,_,_,_),       CYs);
    .findall(PosAz,        connector_link(_,_,_,_,_,_,PosAz,_,_,_),       CZs);

    .concat(NIds,    CIds,    AllIds);
    .concat(NTypes,  CTypes,  AllTypes);
    .concat(NFloors, CFloors, AllFloors);
    .concat(NXs,     CXs,     AllXs);
    .concat(NYs,     CYs,     AllYs);
    .concat(NZs,     CZs,     AllZs);

    // -- Edges --
    .findall(From,     edge(_,From,_,_,_,_,_,_,_,_,_,_),     EFroms);
    .findall(To,        edge(_,_,To,_,_,_,_,_,_,_,_,_),       ETos);
    .findall(EdgeType,  edge(_,_,_,_,EdgeType,_,_,_,_,_,_,_), ETypes);
    .findall(Dir,       edge(_,_,_,_,_,_,_,Dir,_,_,_,_),      EDirs);
    .findall(DistA,     edge(_,_,_,_,_,_,_,_,DistA,_,_,_),    EDistAs);
    .findall(DistB,     edge(_,_,_,_,_,_,_,_,_,DistB,_,_),    EDistBs);

    // -- Distances between doors --
    .findall(RoomA, door_dist(RoomA,_,_,_), DARooms);
    .findall(RoomB, door_dist(_,RoomB,_,_), DBRooms);
    .findall(Dist,  door_dist(_,_,_,Dist),  DDists);

    // -- Objects --
    .findall(ArtId,  new_object(ArtId,_,_,_,_,_,_),  OArtIds);
    .findall(RoomId, new_object(_,RoomId,_,_,_,_,_), ORoomIds);

    // Door/passage artifacts should exist before the graphReady signal.
    !create_door_artifacts;

    // -- CArtAgO operation to complete and send the graph --
    setCompleteGraph(
        AllIds, AllTypes, AllFloors, AllXs, AllYs, AllZs,
        EFroms, ETos, ETypes, EDirs, EDistAs, EDistBs,
        DARooms, DBRooms, DDists,
        OArtIds, ORoomIds
    )[artifact_id(E)];

    .print("[explorer] grafo inviato correttamente all'envManager");
    !poll_target.


+current_room(Where) : at(Where) <- true.     // ignore if it's already known
+current_room(Where) <-
	.abolish(at(_));
    +at(Where);
    .print("[explorer] posizione iniziale: ", Where).


// ── Target polling from bridge (ai_bridge.py → target.json) ───────────────
+!poll_target <-
    .wait(1500);
    !try_navigate;
    !poll_target.

+!try_navigate : at(Start) & not navigating <-
    vesna.CheckTarget(Goal, Artifact);
    +navigating;
    .print("[explorer] target: ", Goal);
    !navigate_to(Start, Goal);
    !final_approach(Goal, Artifact);
    -navigating.

// safety guard (i.e. the agent is already navigating)
+!try_navigate <- true.

// If there is no target the agent continues what it was doing
-!try_navigate <- .abolish(navigating).

// ── Creates door/passage artifacts ────────────────────────────────────────
// Whether a door is a physical door or an open passage is decided by 
// isPhysical, which arrives directly from a field in the Unity Inspector.
// EdgeType == "Central" is excluded, since the edge is in the corridor
// and it doesn't represent a physical element. The Open/Closed state 
// modifies only the initial state of the door.
+!create_door_artifacts <-
    for( edge(GoName, _, _, _, EdgeType, DoorState, _, Dir, _, _, WsPort, IsPhysical) ) {
        if (not created_opening(GoName) & EdgeType \== "Central") {
            if (IsPhysical) {
                if (DoorState == "Open") { IsOpen = "true" } else { IsOpen = "false" };
                makeArtifact(GoName, "artifact.DoorArtifact",
                             [GoName, WsPort, IsOpen, "false", 0.0, 0.0, 0.0], _);
                +created_opening(GoName)
            } else {
                makeArtifact(GoName, "artifact.VarcoArtifact",
                             [GoName, WsPort, 0.0, 0.0, 0.0], _);
                +created_opening(GoName)
            }
        }
    }.

-!create_door_artifacts <-
    .print("[explorer] Error in door/passage artifact creation").
    
// ── Navigates towards a room using A* ─────────────────────────────────────
+!navigate_to(StartId, GoalId) <-
    .print("[explorer] Calcolo percorso: ", StartId, " → ", GoalId);
    vesna.ComputePath(StartId, GoalId, Path, Cost);
    if (Path == []) {
        .print("[explorer] Nessun percorso trovato da ", StartId, " a ", GoalId)
    } else {
        .print("[explorer] Percorso (", Cost, "m): ", Path);
        Path = [_First | StepsToWalk];   // discards the first node
        !follow_path(StepsToWalk);
        .print("[explorer] Percorso completato.")
    }.

+!follow_path([]).
