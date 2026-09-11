using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Data;
using System.Linq;
using JetBrains.Annotations;
using script.avatar.ExplorationHelpers;

// ============================================================
// ExplorationManager.cs  — v2.4
//   • floorGraphs[N] contiene solo i nodi del piano N.
//   • La proprietà `graph` restituisce sempre floorGraphs[currentFloor],
//     quindi tutto il codice di esplorazione resta invariato.
//   • Quando l'agente attraversa un connettore verticale, ExecuteFloorChange:
//       1. Aggiunge "conn_{connectorId}" al grafo piano CORRENTE
//          (pos = triggerStartPosition, es. Door_Cor1-Box su piano 1).
//       2. Crea/recupera il grafo piano DESTINAZIONE.
//       3. Aggiunge "conn_{connectorId}" al grafo piano DESTINAZIONE
//          (pos = triggerEndPosition, es. Door_Box su piano 0).
//       4. Registra il link in ConnectorLinks (struttura pubblica).
//   • BeliefTransmitter può accedere ad AllFloorGraphs e ConnectorLinks
//     per ricostruire il grafo completo dell'edificio.
// ============================================================

// WARN: Moltissime guardie, c'è bisogno di averne così tante?


// ─────────────────────────────────────────────────────────────
// STRUTTURA DATI: link cross-floor tra due grafi di piano
// ─────────────────────────────────────────────────────────────
[System.Serializable]
public struct FloorConnectorLink
{
    public string  connectorId;
    public int     floorFrom;
    public string  nodeIdFrom;    // ID nodo in floorGraphs[floorFrom]
    public Vector3 posFrom;       // posizione fisica lato partenza
    public int     floorTo;
    public string  nodeIdTo;      // ID nodo in floorGraphs[floorTo]
    public Vector3 posTo;         // posizione fisica lato arrivo
     public bool    bidirectional;
}


// ─────────────────────────────────────────────────────────────

[RequireComponent(typeof(ExplorationData))]
[RequireComponent(typeof(StateMachine))]
public class ExplorationManager : MonoBehaviour
{

    private Dictionary<string, string> registeredRoomDoors = new Dictionary<string, string>();
    private HashSet<string> registeredTransitDoors = new HashSet<string>();
    private HashSet<string> allCorridorDoors = new HashSet<string>();
    
    private bool explorationStarted = false;
    
    private string lastCorridorLinkDoorName;
    
    private DoorVarcoScript pendingArrivalDoor = null;
    
    private HashSet<string> traversedConnectors = new HashSet<string>();

    private Dictionary<string, HashSet<string>> _roomArtifacts
        = new Dictionary<string, HashSet<string>>();

    private Dictionary<string, RoomObjectSnapshot> _artifactData
        = new Dictionary<string, RoomObjectSnapshot>();
    
    private ExplorationData expData;

    private StateMachine stateMachine;

    private NavMeshAgent navAgent;
    

    
    // --------------------------------------------------------
    // EVENTI
    // --------------------------------------------------------
    void Awake()
    {
        expData = GetComponent<ExplorationData>();
        stateMachine = GetComponent<StateMachine>();
        navAgent = GetComponent<NavMeshAgent>();
        
        CorridorPoleScript.OnPoleTouched            += stateMachine.HandlePoleTouched;
        ExplorationVisionCone.OnDoorDiscovered      += HandleDoorDiscovered;
        ExplorationVisionCone.OnCorridorPoleVisible += stateMachine.HandleCorridorPoleVisible;
        ExplorationVisionCone.OnDoorVisibleInRoom   += HandleDoorVisibleInRoom;
        VerticalConnectorScript.OnConnectorReached  += HandleConnectorReached;
        
    }

    void OnDestroy()
    {
        CorridorPoleScript.OnPoleTouched            -= stateMachine.HandlePoleTouched;
        ExplorationVisionCone.OnDoorDiscovered      -= HandleDoorDiscovered;
        ExplorationVisionCone.OnCorridorPoleVisible -= stateMachine.HandleCorridorPoleVisible;
        ExplorationVisionCone.OnDoorVisibleInRoom   -= HandleDoorVisibleInRoom;
        VerticalConnectorScript.OnConnectorReached  -= HandleConnectorReached;
        
    }
    // --------------------------------------------------------
    // REFACTOR: EXTRACTED METHODS
    // --------------------------------------------------------

    private bool CheckAndMakeRoomRegistration(DoorVarcoScript door)
    {
        if  (!registeredRoomDoors.ContainsKey(door.gameObject.name))
        {
            registeredRoomDoors[door.gameObject.name] = expData.currentNodeId;
            return false;
        }
        return true;
    }
    
    [CanBeNull] private static GameObject FindRoom(string roomId, [CanBeNull] FloorNode fd) => 
        fd?.roomObjects.Find(r => r != null && r.name == roomId);

    public void ComputeCorridorOrders(string corridorId)
    {
        expData.graph.ComputeForwardOrders(corridorId);
        expData.graph.ComputeBackwardOrders(corridorId);
        expData.graph.ComputeExplorationOrder(corridorId);
    }

    // --------------------------------------------------------
    // AVVIO
    // --------------------------------------------------------
    void Start()
    {
        if (expData.buildingGraph == null)
        {
            Debug.LogWarning("[ExplMgr] BuildingGraph non assegnato!");
            return;
        }
    
        // ── Controlla se esiste già uno snapshot su disco ────────
        if (GraphPersistence.Exists(expData.buildingId))
        {
            BuildingSnapshot snapshot = GraphPersistence.Load(expData.buildingId);
    
            if (snapshot != null)
            {
                Debug.Log($"[ExplMgr] Snapshot trovato per '{expData.buildingId}': " +
                        $"trasmissione diretta a JaCaMo, esplorazione saltata.");
    
                var bt = GetComponent<BeliefTransmitter>();
                bt?.TransmitSnapshot(snapshot);
    
                // Metti la macchina a stati in Completed:
                // l'agente resta fermo, JaCaMo ha già tutto.
                stateMachine.SetState(State.Completed);
                return;
            }

            // File corrotto o illeggibile: procedi con esplorazione normale
            Debug.LogWarning($"[ExplMgr] Snapshot corrotto per '{expData.buildingId}': riesplorazione.");
        }
    
        
        // ── Nessuno snapshot: avvio esplorazione normale ─────────
        FloorNode floorData = PrepareExploration();
        if (floorData == null) return;
    
        expData.currentFloor = floorData.floorIndex;
        StartExploration(floorData.spawnObject.name, floorData.spawnPosition);
    }

    //REFACTOR: Method extracted
    
    private FloorNode PrepareExploration()
    {
        foreach (var floor in expData.buildingGraph.floors)
            floor.explored = false;
    
        Vector3   agentPos  = navAgent.transform.position;
        FloorNode floorData = null;
        float     minDist   = float.MaxValue;
    
        foreach (var floor in expData.buildingGraph.floors)
        {
            foreach (var room in floor.roomObjects)
            {
                if (room == null) continue;
                float d = Vector3.Distance(agentPos, room.transform.position);
                if (d < minDist) { minDist = d; floorData = floor; }
            }
        }
    
        //WARN: spawnObject e spawnPosition? Non sarebbe averne meglio una sola?
        if (floorData == null || floorData.spawnObject == null)
        {
            Debug.LogWarning("[ExplMgr] Piano non trovato o spawnObject mancante!");
            return null;
        }
    
        Vector3 startPos = floorData.spawnPosition;
        if (Vector3.Distance(agentPos, startPos) > 0.05f)
        {
            navAgent.enabled = false;
            navAgent.transform.position = startPos;
            navAgent.enabled = true;
            navAgent.Warp(startPos);
        }
        return floorData;
    }
    
    // ──────────────────────────────────────────────────────────────
    // METODO 1: StartExploration  (sostituisce l'intero metodo)
    // ──────────────────────────────────────────────────────────────
    public void StartExploration(string startNodeId, Vector3 startPos)
    {
        if (explorationStarted) return;
        explorationStarted = true;
     

        Debug.Log($"[ExplMgr] Piano di partenza: {expData.currentFloor}");
     
        // Crea il grafo per il piano iniziale
        expData.floorGraphs[expData.currentFloor] = new TopologicalGraph();
     
        expData.explorationVisionCone?.SetExplorationMode(true);
        expData.currentNodeId = startNodeId;
        CorridorPoleScript.OnPoleTouched -= stateMachine.HandlePoleTouched;
        CorridorPoleScript.OnPoleTouched += stateMachine.HandlePoleTouched;

        // WARN: questo metodo può essere estratto?
        var spawnCorridor = FindCorridorData(startNodeId);
        if (spawnCorridor != null)
        {
            // Spawn in un corridoio: non creare un room node,
            // vai direttamente al polo più vicino
            BeginMovingToPole(startPos, startNodeId, spawnCorridor);
        }
        else
        {
            expData.graph.AddRoomNode(startNodeId, startPos);
            expData.graph.EnterNode(startNodeId);
            StartRotation360(isRoom: true);
        }

        Debug.Log($"[ExplMgr] Avviato da '{startNodeId}' piano {expData.currentFloor}");
    }

    
    
    // --------------------------------------------------------
    // REFACTOR HANDLERS POLE TOUCHED
    // --------------------------------------------------------
    
    public void PoleTouchedWhileTransiting(string corridorId, string poleId, Vector3 polePos)
        {
            // Ignora segnali di polo se siamo ancora vicini al polo A:
            // significa che è un polo diverso da B che ha fatto scattare il trigger
            // prima che l'agente si sia davvero avvicinato a B.
            // WARN: qua è molto simile a sopra, magari si può fare qualche piccolo cambiamento nella logica,
            // ed abbiamo già estratto un metodo.
            float distToB = Vector3.Distance(transform.position, expData.poleBPos);
            float distToA = Vector3.Distance(transform.position, expData.poleAPos);
            if (distToA < distToB && distToB > expData.poleArrivalThreshold)
            {
                return;
            }
            expData.SetPendingCorridorAndPole(corridorId, poleId, polePos);
            ProcessPoleTouched();
        }

    private bool HandleAlreadyExploredPole(CorridorPoleNode poleANode, string corridorId, string poleId)
    {
        var central = poleANode.edges.Find(e => e?.id == $"central_{corridorId}");
        if (central is { state: EdgeState.Explored })
        {
            if (!string.IsNullOrEmpty(expData.currentNodeId) &&
                (expData.currentNodeId.EndsWith("_A") || expData.currentNodeId.EndsWith("_B")))
            {
                string poleAId = $"{corridorId}_A";
                string poleBId = $"{corridorId}_B";
                Vector3 doorPos = transform.position;
                float distToA = Vector3.Distance(doorPos, expData.graph.GetNode(poleAId)?.position ?? doorPos);
                float distToB = Vector3.Distance(doorPos, expData.graph.GetNode(poleBId)?.position ?? doorPos);
                string nearestTo = distToA <= distToB ? poleAId : poleBId;
                expData.graph.AddSegmentArc(expData.currentNodeId, nearestTo,
                    Vector3.Distance(transform.position, expData.graph.GetNode(nearestTo)?.position ?? transform.position));
            }

            Debug.Log($"[ExplMgr] Polo {corridorId}_{poleId} ignorato — corridoio già transitato.");
            return true;
        }

        return false;
    }
        
    public void PoleTouchedWhileIdle(string corridorId, string poleId, Vector3 polePos)
    {
        if (string.IsNullOrEmpty(expData.idleTargetCorridorId)) return;
        // Se stiamo navigando verso un corridoio specifico (idleTargetCorridorId),
        // ignora segnali di polo di altri corridoi per non interrompere il tragitto.
        if (corridorId != expData.idleTargetCorridorId)
        {
            ResetCorridorPoles(corridorId);
            return;
        }

        // Salta il check "già transitato" se ci stiamo andando intenzionalmente:
        // il corridoio può avere ancora porte da esplorare anche se il transito
        // centrale è già marcato come esplorato.
        var poleANode = expData.graph.GetCorridorPoleNode($"{corridorId}_A");
        if (poleANode != null && !HandleAlreadyExploredPole(poleANode, corridorId, poleId)) return;
        // Corridoio corretto raggiunto: pulisci il target idle
        expData.idleTargetCorridorId = null;
        expData.SetPendingCorridorAndPole(corridorId, poleId, polePos);
        ProcessPoleTouched();
    }

  public void ProcessPoleTouched()
    {
        var corridorData = FindCorridorData(expData.pendingCorridorId);
        if (corridorData == null) return;

        bool isFirstPole = (!stateMachine.IsState(State.Transiting));

        if (isFirstPole)
        {
            TouchedFirstPole(corridorData);
        }
        else
        {
            TouchedAnotherPole();
        }

        expData.pendingCorridorId = null;
        expData.pendingPoleId     = null;
    }

    // --------------------------------------------------------
    // HANDLER: DoorSensor scopre una porta durante il transito
    // --------------------------------------------------------
    private void HandleDoorDiscovered(DoorVarcoScript door, string corridorId, int orderFW)
    {
        if (!stateMachine.IsState(State.Transiting)) return;
        if (corridorId != expData.transitCorridorId) return;
        if (registeredTransitDoors.Contains(door.gameObject.name)) return;
        if (allCorridorDoors.Contains(door.gameObject.name)) return;

        float corridorLength = Vector3.Distance(expData.poleAPos, expData.poleBPos);
        if (door.distFromB > corridorLength + 0.6f && door.distFromA > corridorLength + 0.6f)
        {
            Debug.Log($"[ExplMgr] {door.gameObject.name} oltre il corridoio → ignorata");
            return;
        }
        
        string frontName = door.roomFront?.name ?? "";
        string backName  = door.roomBack?.name  ?? "";
        if (frontName != corridorId && backName != corridorId) return;
        bool isCorrLink = door.isCorridorLink;

        registeredTransitDoors.Add(door.gameObject.name);
        if (!isCorrLink) allCorridorDoors.Add(door.gameObject.name);
        
        string computedSide = ComputeSideRelativeToCorridorAxis(
            door.transform.position, expData.poleAPos, expData.poleBPos);
        if (!isCorrLink)
        {
            expData.graph.AddDoorEdges(corridorId, door.gameObject.name,
            door.elementType == DoorVarcoScript.ElementType.Door,
            door.transform.position, computedSide,
            door.distFromA, door.distFromB,
            isCorridorLink: false);
        }
        

        Debug.Log($"[ExplMgr] Porta censita: {door.gameObject.name} " +
                $"corridoio={corridorId} side={computedSide} corridorLink={isCorrLink}");
    }

    private string ComputeSideRelativeToCorridorAxis(Vector3 doorPos, Vector3 posA, Vector3 posB)
    {
        Vector3 axis   = (posB - posA).normalized; axis.y = 0f;
        Vector3 toDoor = (doorPos - posA).normalized; toDoor.y = 0f;
        float cross = axis.x * toDoor.z - axis.z * toDoor.x;
        if (Mathf.Abs(cross) < 0.05f) return "RIGHT";
        return cross > 0f ? "LEFT" : "RIGHT";
    }
    
    //
    // REFACTOR HANDLERS VISION CONE
    //

    private string ReturnNearestRoomToDoor(DoorVarcoScript door, VerticalConnector connectorForDoor)
    {
        var fd = expData.buildingGraph?.GetFloor(expData.currentFloor);
        if (fd == null) return null;
        
        float minDist = float.MaxValue;
        string nearest = null;
        foreach (string roomId in fd.roomIds)
        {
            if (roomId == expData.currentNodeId || roomId == connectorForDoor.id) continue;
            if (expData.graph.GetNode(roomId) != null) continue; // già visitata
            var rObj = FindRoom(roomId, fd);
            if (rObj == null) continue;
            float d = Vector3.Distance(door.transform.position, rObj.transform.position);
            if (d < minDist) { minDist = d; nearest = roomId; }
        }

        return nearest;
    }

    private bool IsRoomPointedByConnectorDoorInvalid(string connRoomBeyond, VerticalConnector connectorForDoor) => 
        string.IsNullOrEmpty(connRoomBeyond) || connRoomBeyond == expData.currentNodeId
                                             || connRoomBeyond == connectorForDoor.id;

    private void HandleDoorVisibleInConnectorAlreadyTransited(DoorVarcoScript door, VerticalConnector connectorForDoor)
    {
        if (CheckAndMakeRoomRegistration(door)) return;
        if (!traversedConnectors.Contains(expData.currentNodeId))
            return;

        // Determina la stanza di destinazione
        string connRoomBeyond = door.GetRoomNameBeyondDoor(transform.position);

        // FIX self-loop: se GetRoomNameBeyondDoor restituisce il nodo corrente
        // (succede quando door.roomFront/roomBack non sono configurati correttamente),
        // cerco la stanza non ancora visitata del piano corrente più vicina alla porta.
        if (IsRoomPointedByConnectorDoorInvalid(connRoomBeyond, connectorForDoor))
        {
            connRoomBeyond = ReturnNearestRoomToDoor(door, connectorForDoor);
        }

        // Se ancora non abbiamo una destinazione valida, abbandoniamo
        if (IsRoomPointedByConnectorDoorInvalid(connRoomBeyond, connectorForDoor))
            return;

        string targetId = connRoomBeyond;

        // Aggiungi il nodo destinazione al grafo se non esiste ancora
        if (expData.graph.GetNode(targetId) == null)
        {
            var floorData = expData.buildingGraph?.GetFloor(expData.currentFloor);
            var roomObj   = FindRoom(targetId, floorData);
            if (roomObj != null)
                expData.graph.AddRoomNode(targetId, roomObj.transform.position);
        }

        if (expData.graph.GetNode(targetId) == null) return; // nodo non trovabile, skip

        float connNavDist = door.NavMeshDistanceTo(transform.position);
        var edge = expData.graph.AddRoomDoorEdge(expData.currentNodeId, door.gameObject.name,
            door.elementType == DoorVarcoScript.ElementType.Door,
            door.transform.position, connNavDist, targetId);

        // Marca explored SOLO se la stanza è già stata visitata;
        // altrimenti resta Discovered → l'agente la raggiungerà via la porta.
        var beyondNode = expData.graph.GetNode(targetId);
        bool targetVisited = beyondNode is { physicallyVisited: true };
        if (edge != null && targetVisited)
            expData.graph.MarkEdgeExplored(expData.currentNodeId, edge.id, targetId);

        Debug.Log($"[ExplMgr] Connettore '{door.gameObject.name}' già attraversato → " +
                  $"RoomDoor {(targetVisited ? "explored" : "discovered")} verso '{targetId}'.");
    }
    
    private void HandleDoorVisibleInConnector(DoorVarcoScript door, VerticalConnector connectorForDoor)
    {
        string corridorBeyond = door.GetRoomNameBeyondDoor(transform.position);
        if (!CheckAndMakeRoomRegistration(door))
        {
            if (!string.IsNullOrEmpty(corridorBeyond) && FindCorridorData(corridorBeyond) != null)
            {
                AddEdgeToUnexploredCorridor(door, corridorBeyond);
            }
            else
            {
                AddEdgeToUnknownCorridor(door, connectorForDoor);
            }
        }
    }

    private void AddEdgeToUnexploredCorridor(DoorVarcoScript door, string corridorBeyond)
    {
        NavMeshPath path = new NavMeshPath();
        float corridorNavDist = 0f;
        if (NavMesh.CalculatePath(transform.position, door.transform.position, NavMesh.AllAreas, path))
            for (int i = 1; i < path.corners.Length; i++)
                corridorNavDist += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        expData.graph.AddRoomDoorEdge(expData.currentNodeId, door.gameObject.name,
            door.elementType == DoorVarcoScript.ElementType.Door,
            door.transform.position, corridorNavDist, corridorBeyond);
    }

    private void AddEdgeToUnknownCorridor(DoorVarcoScript door, VerticalConnector connectorForDoor)
    {
        // Apri fisicamente la porta
        if (!door.IsTraversable)
            door.TryOpen();

                
        // WARN: metodo estraibile
        // Crea il nodo connettore nel grafo se non esiste ancora
        string connId2 = connectorForDoor?.id;
        if (!string.IsNullOrEmpty(connId2) && expData.graph.GetNode(connId2) == null)
        {
            bool isRev3 = (connectorForDoor.floorFrom == expData.currentFloor);
            Vector3 connPos3 = isRev3
                ? connectorForDoor.triggerEndPosition
                : connectorForDoor.triggerStartPosition;
            expData.graph.AddRoomNode(connId2, connPos3);
        }

        // Arco currentNodeId→door→connettore, marcato explored (porta aperta = visitata)
        if (!string.IsNullOrEmpty(connId2))
        {
            float navDist2 = door.NavMeshDistanceTo(transform.position);
            var edgeConn = expData.graph.AddRoomDoorEdge(expData.currentNodeId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, navDist2, connId2);
            if (edgeConn != null)
                expData.graph.MarkEdgeExplored(expData.currentNodeId, edgeConn.id, connId2);
        }

        Debug.Log($"[ExplMgr] Scala '{door.gameObject.name}' aperta da '{expData.currentNodeId}' → '{connId2}' (non entro)");
    }

    private void HandleCorridorDoor(DoorVarcoScript door)
    {
        string corridorId = expData.currentNodeId.Replace("_B", "");

        if (allCorridorDoors.Contains(door.gameObject.name)) return;

        float dB = Vector3.Distance(door.transform.position, expData.poleBPos);
        float dA = Vector3.Distance(door.transform.position, expData.poleAPos);

        if (dB > 0.6f && dA > 0.6)
        { Debug.Log($"[ExplMgr] {door.gameObject.name} fuori corridoio (dB={dB:F2}) → ignorata"); return; }

        registeredTransitDoors.Add(door.gameObject.name);
        allCorridorDoors.Add(door.gameObject.name);

        string computedSide = ComputeSideRelativeToCorridorAxis(
            door.transform.position, expData.poleAPos, expData.poleBPos);

        expData.graph.AddDoorEdges(corridorId, door.gameObject.name,
            door.elementType == DoorVarcoScript.ElementType.Door,
            door.transform.position, computedSide, dA, dB);

        ComputeCorridorOrders(corridorId);

        Debug.Log($"[ExplMgr] Porta polo-B → {corridorId}: {door.gameObject.name} " +
                  $"dA={dA:F1} dB={dB:F1} side={computedSide}");
    }

    private void HandleConnectorDoor(DoorVarcoScript door)
    {
        var connectorForDoor = FindConnectorForDoor(door.gameObject.name);

        // La porta è accettata se:
        //   a) appartiene fisicamente a questa stanza (roomFront/roomBack)
        //   b) oppure il nodo corrente È il connettore stesso (es. scalaSolaio vede Door_Solaio2)
        bool frontMatch     = door.roomFront != null && door.roomFront.name == expData.currentNodeId;
        bool backMatch      = door.roomBack  != null && door.roomBack.name  == expData.currentNodeId;
        bool connectorMatch = connectorForDoor != null && connectorForDoor.id == expData.currentNodeId;

        if (!frontMatch && !backMatch && !connectorMatch)
        {
            CheckAndMakeRoomRegistration(door);
            return;
        }

        // Connettore già attraversato: creo arco verso la stanza al di là vs Connettore NON ancora attraversato
        if (connectorForDoor != null && traversedConnectors.Contains(connectorForDoor.id))
            HandleDoorVisibleInConnectorAlreadyTransited(door, connectorForDoor);
        else
            HandleDoorVisibleInConnector(door, connectorForDoor);
    }

    private void HandleNonConnectorDoor(DoorVarcoScript door)
    {
        registeredRoomDoors[door.gameObject.name] = expData.currentNodeId;
        door.discovered = true;

        var node = expData.graph.GetNode(expData.currentNodeId);

        if (node is { type: NodeType.CorridorPoleB })
        {
            HandleCorridorDoor(door);
            return;
        }

        // Ignora porte che non appartengono fisicamente a questa stanza
        string frontName      = door.roomFront?.name;
        string backName       = door.roomBack?.name;
        bool   doorBelongsHere = (frontName == expData.currentNodeId || backName == expData.currentNodeId);
        if (!doorBelongsHere)
        {
            return;
        }

        string roomBeyond = door.GetRoomNameBeyondDoor(transform.position);
        bool leadsToCorridor = !string.IsNullOrEmpty(roomBeyond)
                               && roomBeyond.ToLower().Contains("corridoio");

        string toNodeId = leadsToCorridor ? roomBeyond : null;
        if (!string.IsNullOrEmpty(toNodeId))
        {
            var targetNode = expData.graph.GetNode(toNodeId);
            if (targetNode is { physicallyVisited: true } && !targetNode.HasUndiscoveredEdges())
                return;
        }

        float navDist = door.NavMeshDistanceTo(transform.position);
        expData.graph.AddRoomDoorEdge(expData.currentNodeId, door.gameObject.name,
            door.elementType == DoorVarcoScript.ElementType.Door,
            door.transform.position, navDist,
            toNodeId: toNodeId);

    }
    
    // --------------------------------------------------------
    // HANDLER: VisionCone vede una porta dentro una stanza
    // --------------------------------------------------------
    private void HandleDoorVisibleInRoom(DoorVarcoScript door)
    {
        if (!stateMachine.IsState(State.Rotating360) && 
            !stateMachine.IsState(State.InsideRoom)) return;
        if (!expData.inRoomMode) return;
        if (registeredRoomDoors.TryGetValue(door.gameObject.name, out string registeredFrom)
            && registeredFrom == expData.currentNodeId) return;

        // FIX spam: porte non-connettore in un nodo connettore → ignora UNA VOLTA SOLA
        if (traversedConnectors.Contains(expData.currentNodeId) && !IsConnectorDoor(door.gameObject.name))
        {
            CheckAndMakeRoomRegistration(door);
            return;
        }

        // ── PORTE CONNETTORE (scale/ascensori) vs PORTA NORMALE ──
        if (IsConnectorDoor(door.gameObject.name))
            HandleConnectorDoor(door);
        else
            HandleNonConnectorDoor(door);
    }

     // REFACTOR: EXTRACTED METHOD

     public void AddNodeToFromGraph(TopologicalGraph fromGraph, string connectorId, Vector3 connPosHere)
     {
         fromGraph.AddRoomNode(connectorId, connPosHere);
         string sourceId = expData.currentNodeId;
         if (!string.IsNullOrEmpty(sourceId))
         {
             float dist = Vector3.Distance(fromGraph.GetNode(sourceId).position, connPosHere);
             var edge = fromGraph.AddRoomDoorEdge(sourceId, connectorId, true, connPosHere, dist, connectorId);
             if (edge != null) fromGraph.MarkEdgeExplored(sourceId, edge.id, connectorId);
         }

         fromGraph.EnterNode(connectorId);
     }

     // --------------------------------------------------------
    // HANDLER: connettore verticale raggiunto (trigger fisico)
    // --------------------------------------------------------
    private void HandleConnectorReached(string connectorId, int targetFloor, Vector3 pos)
    {
        if (expData.pendingConnector == null || expData.pendingConnector.id != connectorId) return;

        stateMachine.TryTransition(State.MovingToConnector, State.TraversingStairs, () =>
        {
            bool isReverse = (expData.pendingConnector.floorFrom == targetFloor);
            Vector3 connPosHere = isReverse
                ? expData.pendingConnector.triggerEndPosition
                : expData.pendingConnector.triggerStartPosition;

            int fromFloor = isReverse ? expData.pendingConnector.floorTo : expData.pendingConnector.floorFrom;
            expData.floorGraphs.TryGetValue(fromFloor, out var fromGraph);

            if (fromGraph != null && fromGraph.GetNode(connectorId) == null)
            {
                AddNodeToFromGraph(fromGraph, connectorId, connPosHere);
            }

            
        });
        
        if(stateMachine.IsState(State.TraversingStairs))
            StartCoroutine(TraverseStairsThen(targetFloor));
    }
    
    //
    // REFACTOR POLE TOUCHED
    //

    private void CreateCorridorSegment(string corridorId, Vector3 doorPos, string poleAId, string poleBId)
    {
        var nodeA = expData.graph.GetNode($"{corridorId}_A");
        var nodeB = expData.graph.GetNode($"{corridorId}_B");
        
        string fromNode = null;
        float bestDist = float.MaxValue;
        if (nodeA != null) { float d = Vector3.Distance(doorPos, nodeA.position); if (d < bestDist) { bestDist = d; fromNode = $"{corridorId}_A"; } }
        if (nodeB != null) { float d = Vector3.Distance(doorPos, nodeB.position); if (d < bestDist) { fromNode = $"{corridorId}_B"; } }
        
        float distToNewA = Vector3.Distance(doorPos, expData.poleAPos);
        float distToNewB = Vector3.Distance(doorPos, expData.poleBPos);
        string toNode = distToNewA <= distToNewB ? poleAId : poleBId;
        
        Vector3 fromPos = expData.graph.GetNode(fromNode)?.position ?? Vector3.zero;
        Vector3 toPos   = expData.graph.GetNode(toNode)?.position   ?? Vector3.zero;
        float distFromDoor = NavMeshDist(fromPos, doorPos);
        float distDoorTo   = NavMeshDist(doorPos, toPos);
        float dist = (distFromDoor >= 0f && distDoorTo >= 0f)
            ? distFromDoor + distDoorTo
            : Vector3.Distance(fromPos, toPos);

        expData.graph.AddSegmentArc(fromNode, toNode, dist, lastCorridorLinkDoorName, doorPos);
    }

    [CanBeNull]
    private string GetOtherCorridorIdIfDoorMatchesCorridorCriteria(DoorVarcoScript door)
    {
        if (!door.isCorridorLink) return null;
        string front = door.roomFront?.name ?? "";
        string back  = door.roomBack?.name  ?? "";

        // La porta deve avere un lato sul nuovo corridoio
        bool newIsA = (front == expData.pendingCorridorId || back == expData.pendingCorridorId);
        if (!newIsA) return null;

        // L'altro lato deve essere un corridoio già scoperto
        string otherCorrId = (front == expData.pendingCorridorId) ? back : front;
        // ← aggiungi qui, dopo otherCorrId è dichiarato
        var otherCorrDataCheck = FindCorridorData(otherCorrId);
        if (otherCorrDataCheck == null) return null;
            
        var otherNodeA = expData.graph.GetNode($"{otherCorrId}_A");
        var otherNodeB = expData.graph.GetNode($"{otherCorrId}_B");
        if (otherNodeA == null && otherNodeB == null) return null; // non ancora scoperto
        return otherCorrId;
    }

    private void TouchedFirstPole(CorridorData corridorData)
    {
        expData.poleAPos = expData.pendingPolePos;
        Vector3 targetB = expData.pendingPoleId == "1"
            ? corridorData.Pole2Position
            : corridorData.Pole1Position;
        expData.poleBPos = targetB;

        expData.graph.AddCorridorPoles(expData.pendingCorridorId, expData.poleAPos, expData.poleBPos,
            Vector3.Distance(expData.poleAPos, expData.poleBPos));

        string comingFromNodeId = expData.currentNodeId;
        string poleAId          = $"{expData.pendingCorridorId}_A";
        expData.currentNodeId           = poleAId;
        expData.graph.EnterNode(poleAId);

        if (!string.IsNullOrEmpty(comingFromNodeId) && 
            (comingFromNodeId.EndsWith("_B") || comingFromNodeId.EndsWith("_A")))
        {
            string comingCorridor = comingFromNodeId[..^2];

            var linkDoor = FindDoorScript(lastCorridorLinkDoorName);

            string f = linkDoor?.roomFront?.name ?? "";
            string b = linkDoor?.roomBack?.name  ?? "";
            bool directlyLinked = linkDoor != null &&
                                (f == comingCorridor || b == comingCorridor) &&
                                (f == expData.pendingCorridorId || b == expData.pendingCorridorId);

            if (directlyLinked)
            {
                CreateCorridorSegment(comingCorridor, linkDoor.transform.position, poleAId, poleBId: $"{expData.pendingCorridorId}_B");
            }
            lastCorridorLinkDoorName = null;
        }
        // Dopo aver creato i poli del nuovo corridoio,
        // cerca corridorLink già registrati che connettono
        // un corridoio già scoperto a questo nuovo corridoio.
        string newCorrA = $"{expData.pendingCorridorId}_A";
        string newCorrB = $"{expData.pendingCorridorId}_B";
        foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
        { 
            string otherCorrId = GetOtherCorridorIdIfDoorMatchesCorridorCriteria(door);

            // Trova il polo più vicino dell'altro corridoio rispetto alla porta
            if (otherCorrId != null) CreateCorridorSegment(otherCorrId, door.transform.position, newCorrA, newCorrB);
           
        }

        expData.transitCorridorId = expData.pendingCorridorId;
        registeredTransitDoors.Clear();
        BeginTransitingToB(expData.poleAPos, targetB);
        

        Debug.Log($"[ExplMgr] Transito {expData.pendingCorridorId}: A={expData.poleAPos} → B={targetB}");
    }


    private void TouchedAnotherPole()
    {
        expData.explorationVisionCone?.StopTransitScan();

        string poleBId = $"{expData.pendingCorridorId}_B";
        expData.currentNodeId  = poleBId;
        expData.graph.EnterNode(poleBId);

        ScanCorridorDoorsFromScript(expData.pendingCorridorId);

        ComputeCorridorOrders(expData.pendingCorridorId);

        expData.graph.MarkEdgeExplored($"{expData.pendingCorridorId}_A",
            $"central_{expData.pendingCorridorId}", poleBId);

        expData.movingToPoleCorridorId = null;
        Debug.Log($"[ExplMgr] Polo B toccato. Inizio ispezione {expData.pendingCorridorId}.");
        StartRotation360(isRoom: false);
    }


    public void StartRotation360(bool isRoom)
    {
        expData.inRoomMode    = isRoom;
        expData.rotationAccum = 0f;
        navAgent.isStopped = true;
        stateMachine.SetState(State.Rotating360);

        if (isRoom)
            registeredRoomDoors.Clear();

        // Marca il nodo come fisicamente visitato (360° reale).
        // Questo distingue i nodi aggiunti programmaticamente (es. ExecuteFloorChange)
        // dai nodi realmente esplorati, impedendo che LOOP EVITATO li salti.
        if (isRoom && expData.graph != null)
        {
            var visitNode = expData.graph.GetNode(expData.currentNodeId);
            if (visitNode != null) visitNode.physicallyVisited = true;
        }

    }

    //
    // REFACTOR DECISIONE PROSSIMA AZIONE
    //
    
    public (string targetNode, GraphNode targetData) BacktrackingLoop()
    {
        string    targetNode = null;
        GraphNode targetData = null;

        while (true)
        {
            targetNode = expData.graph.Backtrack();
            if (targetNode == null) { CheckFloorCompletion(); break; }

            targetData = expData.graph.GetNode(targetNode);
            if (targetData != null && targetData.HasUndiscoveredEdges())
            {
                if (targetNode.EndsWith("_A") || targetNode.EndsWith("_B"))
                {
                    string corridorId = targetNode.Substring(0, targetNode.Length - 2);
                    var nodeA = expData.graph.GetNode($"{corridorId}_A");
                    var nodeB = expData.graph.GetNode($"{corridorId}_B");
                    bool corridorDone = nodeA is { physicallyVisited: true }
                                        && nodeB is { physicallyVisited: true }
                                        && expData.graph.IsCorridorFullyTransited(corridorId);
                    if (corridorDone)
                    {
                        continue;
                    }
                }
                break;
            }
            Debug.Log($"[Backtrack] {targetNode} già esplorato, riavvolgo...");
        }
        return (targetNode, targetData);
    }
    
    // ====================================================
    // DECISIONE PROSSIMA AZIONE
    // ====================================================
    
    public void DecideNextAction()
    {
        if (string.IsNullOrEmpty(expData.currentNodeId)) return;

        var node = expData.graph.GetNode(expData.currentNodeId);
        if (node == null) { stateMachine.DoBacktrack(); return; }

        GraphEdge nextEdge = null;

        if (node.type == NodeType.CorridorPoleB)
        {
            nextEdge = expData.graph.NextCorridorEdgeToInspect(expData.currentNodeId) 
                       ?? expData.graph.NextRoomEdgeToInspect(expData.currentNodeId);

            if (nextEdge == null)
            {
                nextEdge = node.edges.FirstOrDefault(e =>
                    e != null &&
                    e.state != EdgeState.Explored &&
                    e.isCorridorLink);
    
                if (nextEdge != null)
                    Debug.Log($"[ExplMgr] Corridor link non esplorato trovato: {nextEdge.id}");
            }
        }
        else if (node.type == NodeType.Room)
            nextEdge = expData.graph.NextRoomEdgeToInspect(expData.currentNodeId);
        else
            nextEdge = expData.graph.NextEdgeToInspect(expData.currentNodeId);

        if (nextEdge != null)
        {
            MoveToNextEdge(nextEdge);
        }
        else
        {
            stateMachine.DoBacktrack();
        }
    }
    
    // REFACTOR ENTRATA ATTRAVERSO UN ARCO
    private string FindNextNodeId(DoorVarcoScript doorScript, GraphEdge edge)
    {

        string newNodeId;
        if (doorScript != null && (edge.id.StartsWith("bw_") || edge.id.StartsWith("fw_")))
        {
            string corridorId = (expData.currentNodeId.EndsWith("_A") || expData.currentNodeId.EndsWith("_B"))
                ? expData.currentNodeId.Substring(0, expData.currentNodeId.Length - 2)
                : expData.currentNodeId;
            string front = doorScript.roomFront?.name ?? "";
            string back  = doorScript.roomBack?.name  ?? "";
            if (front == corridorId) newNodeId = back;
            else if (back == corridorId) newNodeId = front;
            else newNodeId = doorScript.GetRoomNameBeyondDoor(transform.position);
        }
        else if (doorScript != null)
            newNodeId = doorScript.GetRoomNameBeyondDoor(transform.position);
        else
            newNodeId = edge.id;

        return newNodeId;
    }

    private void StopNavigationAtConnectorDoor(DoorVarcoScript doorScript, GraphEdge edge, string newNodeId)
    {
        var connector = FindConnectorForDoor(doorScript.gameObject.name);
        if (connector != null) newNodeId = connector.id;

        if (newNodeId == expData.currentNodeId)
        {
            string beyond = doorScript.GetRoomNameBeyondDoor(transform.position);
            if (!string.IsNullOrEmpty(beyond) && beyond != expData.currentNodeId)
                newNodeId = beyond;
        }

        if (expData.graph.GetNode(newNodeId) == null)
            expData.graph.AddRoomNode(newNodeId, edge.position);

        expData.graph.MarkEdgeExplored(expData.currentNodeId, edge.id, newNodeId);
        SyncTwinCorridorEdge(edge.id, newNodeId);

        expData.currentEdge = null;
        navAgent.isStopped = false;
        DecideNextAction();
    }

    private bool HasCorridorBeenVisited(string newNodeId)
    {
        if (expData.graph.IsCorridorFullyTransited(newNodeId))
        {
            Debug.LogWarning($"[ExplMgr] Corridoio '{newNodeId}' già transitato.");
            expData.currentEdge = null;
            navAgent.isStopped = false;
            DecideNextAction();
            return true;
        }
        // Controlla anche se corridoio già fisicamente visitato e senza porte pendenti
        var targetCorridorNode = expData.graph.GetNode($"{newNodeId}_B");
        if (targetCorridorNode is { physicallyVisited: true } && !targetCorridorNode.HasUndiscoveredEdges())
        {
            Debug.LogWarning($"[ExplMgr] Corridoio '{newNodeId}' già completato, salto transito.");
            expData.currentEdge = null;
            navAgent.isStopped = false;
            DecideNextAction();
            return true;
        }
        return false;
    }

    private Vector3 ComputeNavigationTarget(bool isTargetCorridor, 
        DoorVarcoScript doorScript, 
        GraphEdge edge, 
        string newNodeId)
    {
        Vector3 enterTarget;
        if (isTargetCorridor)
        {
            // Corridoio: comportamento originale — entra oltre la porta
            Vector3 doorNormal = doorScript != null ? doorScript.transform.forward : transform.forward;
            Vector3 toDoor     = (edge.position - transform.position).normalized;
            if (Vector3.Dot(doorNormal, toDoor) < 0) doorNormal = -doorNormal;
            enterTarget = edge.position + doorNormal * expData.enterOffset;
        }
        else
        {
            // Stanza: vai al centro del GameObject della stanza
            var floorData = expData.buildingGraph?.GetFloor(expData.currentFloor);
            var roomObj   = FindRoom(newNodeId, floorData);
            if (roomObj != null)
                enterTarget = GetRoomCenter(roomObj);
            else
            {
                // Fallback: offset oltre la porta
                Vector3 doorNormal = doorScript != null ? doorScript.transform.forward : transform.forward;
                Vector3 toDoor     = (edge.position - transform.position).normalized;
                if (Vector3.Dot(doorNormal, toDoor) < 0) doorNormal = -doorNormal;
                enterTarget = edge.position + doorNormal * expData.enterOffset;
            }
        }
        return enterTarget;
    }

    private bool HasEdgeAlreadyBeenExplored(string newNodeId, bool isTargetCorridor, GraphEdge edge)
    {
        var existingNode = expData.graph.GetNode(newNodeId);
        if (existingNode != null && !existingNode.HasUndiscoveredEdges() 
                                 && !isTargetCorridor 
                                 && existingNode.physicallyVisited)
        {
            Debug.LogWarning($"[ExplMgr] LOOP EVITATO: '{newNodeId}' già completata.");
            expData.graph.MarkEdgeExplored(expData.currentNodeId, edge.id, newNodeId);
            SyncTwinCorridorEdge(edge.id, newNodeId);
            expData.currentEdge = null;
            navAgent.isStopped = false;
            DecideNextAction();
            return true;
        }

        expData.graph.MarkEdgeExplored(expData.currentNodeId, edge.id, isTargetCorridor ? null : newNodeId);
        SyncTwinCorridorEdge(edge.id, newNodeId);

        if (isTargetCorridor)
        {
            var currentNode = expData.graph.GetNode(expData.currentNodeId);
            if (currentNode != null)
            {
                currentNode.edges.RemoveAll(e =>
                    e is { edgeType: EdgeType.Door } &&
                    e.toNodeId == edge.toNodeId);
            }

            if (HasCorridorBeenVisited(newNodeId)) return true;
            
            expData.idleTargetCorridorId = newNodeId;
            stateMachine.SetState(State.Idle);
        }
        else
        {
            var floorData2 = expData.buildingGraph?.GetFloor(expData.currentFloor);
            var roomObj2   = FindRoom(newNodeId, floorData2);
            Vector3 roomCenter = roomObj2 != null ? GetRoomCenter(roomObj2) : edge.position;
            expData.graph.AddRoomNode(newNodeId, roomCenter);
            expData.currentNodeId = newNodeId;
            expData.graph.EnterNode(expData.currentNodeId);
            stateMachine.SetState(State.EnteringRoom);
        }

        return false;
    }
    
    // ====================================================
    // ENTRA ATTRAVERSO UN ARCO
    // ====================================================
    public void EnterThroughEdge(GraphEdge edge)
    {
        var doorScript = FindDoorScript(edge.id);

        string newNodeId = FindNextNodeId(doorScript, edge);
        
        if (doorScript != null && IsConnectorDoor(doorScript.gameObject.name))
        {
            StopNavigationAtConnectorDoor(doorScript, edge, newNodeId);
            return;
        }
        
        bool isTargetCorridor = FindCorridorData(newNodeId) != null;

        if(HasEdgeAlreadyBeenExplored(newNodeId, isTargetCorridor, edge)) return;

        // Calcolo target di navigazione
        Vector3 enterTarget = ComputeNavigationTarget(isTargetCorridor, doorScript, edge, newNodeId);

        NavMeshHit hit;
        if (!NavMesh.SamplePosition(enterTarget, out hit, 2f, NavMesh.AllAreas))
        {
            enterTarget = edge.position;
            if (!NavMesh.SamplePosition(enterTarget, out hit, 2f, NavMesh.AllAreas))
            {
                hit.position = edge.position;
                Debug.LogWarning($"[ExplMgr] NavMesh non trovato vicino a '{newNodeId}'.");
            }
        }
        enterTarget = hit.position;

        navAgent.isStopped = false;
        navAgent.SetDestination(enterTarget);

        if (isTargetCorridor)
            lastCorridorLinkDoorName = (edge.id.StartsWith("bw_") || edge.id.StartsWith("fw_"))
                ? edge.id.Substring(3) : edge.id;
        expData.currentEdge = null;
    }

    private void SyncTwinCorridorEdge(string edgeId, string toNodeId)
    {
        if (string.IsNullOrEmpty(expData.currentNodeId)) return;

        if (expData.currentNodeId.EndsWith("_B") && edgeId.StartsWith("bw_"))
        {
            string poleA  = expData.currentNodeId.Replace("_B", "_A");
            string fwEdge = edgeId.Replace("bw_", "fw_");
            expData.graph.MarkEdgeExplored(poleA, fwEdge, toNodeId);
        }
        else if (expData.currentNodeId.EndsWith("_A") && edgeId.StartsWith("fw_"))
        {
            string poleB  = expData.currentNodeId.Replace("_A", "_B");
            string bwEdge = edgeId.Replace("fw_", "bw_");
            expData.graph.MarkEdgeExplored(poleB, bwEdge, toNodeId);
        }
    }
    
    //
    // REFACTOR COMPLETAMENTO PIANO
    //

    private bool FindAndExploreAdjacentFloors(List<FloorNode> unexplored)
    {
        unexplored.Sort((a, b) =>
            Mathf.Abs(a.floorIndex - expData.currentFloor)
                .CompareTo(Mathf.Abs(b.floorIndex - expData.currentFloor)));

        foreach (var targetFloor in unexplored)
        {
            var connectors = expData.buildingGraph.GetConnectors(expData.currentFloor, targetFloor.floorIndex);
            connectors.RemoveAll(c => traversedConnectors.Contains(c.id));
            if (connectors.Count == 0) continue;

            connectors.Sort((a, b) => a.costUp.CompareTo(b.costUp));
            expData.pendingConnector = connectors[0];

            bool isReverse = (expData.pendingConnector.floorFrom != expData.currentFloor);
            Vector3 navTarget = isReverse
                ? expData.pendingConnector.triggerEndPosition
                : expData.pendingConnector.triggerStartPosition;

            Debug.Log($"[ExplMgr] Piano {expData.currentFloor} → piano {targetFloor.floorIndex} " +
                      $"via '{expData.pendingConnector.id}' (diretto)");
            navAgent.isStopped = false;
            navAgent.SetDestination(navTarget);
            stateMachine.SetState(State.MovingToConnector);
            return true;
        }

        return false;
    }

    private bool FindAndExploreOtherFloors(List<FloorNode> unexplored)
    {
        Debug.Log($"[ExplMgr] Nessun connettore diretto da piano {expData.currentFloor}. " +
                  $"Cerco percorso indiretto...");

        unexplored.Sort((a, b) => a.floorIndex.CompareTo(b.floorIndex));

        // Piani già visitati, ordinati per vicinanza al piano corrente
        var visitedFloors = new List<int>(expData.floorGraphs.Keys);
        visitedFloors.Sort((a, b) =>
            Mathf.Abs(a - expData.currentFloor).CompareTo(Mathf.Abs(b - expData.currentFloor)));

        foreach (var targetFloor in unexplored)
        {
            foreach (int midFloor in visitedFloors)
            {
                if (midFloor == expData.currentFloor) continue;

                // Il piano intermedio deve avere un connettore non traversato verso targetFloor
                var connToTarget = expData.buildingGraph.GetConnectors(midFloor, targetFloor.floorIndex);
                connToTarget.RemoveAll(c => traversedConnectors.Contains(c.id));
                if (connToTarget.Count == 0) continue;

                // Cerca il connettore tra currentFloor e midFloor
                // (può essere già traversato — vogliamo ri-attraversarlo)
                var connToMid = expData.buildingGraph.GetConnectors(expData.currentFloor, midFloor);
                if (connToMid.Count == 0) continue;

                connToMid.Sort((a, b) => a.costUp.CompareTo(b.costUp));
                expData.pendingConnector = connToMid[0];

                bool isReverse = (expData.pendingConnector.floorFrom != expData.currentFloor);
                Vector3 navTarget = isReverse
                    ? expData.pendingConnector.triggerEndPosition
                    : expData.pendingConnector.triggerStartPosition;

                Debug.Log($"[ExplMgr] Percorso indiretto: piano {expData.currentFloor} → piano {midFloor} " +
                          $"→ piano {targetFloor.floorIndex} via '{expData.pendingConnector.id}'");
                navAgent.isStopped = false;
                navAgent.SetDestination(navTarget);
                stateMachine.SetState(State.MovingToConnector);
                return true;
            }
        }

        return false;
    }
    
    // ====================================================
    // COMPLETAMENTO PIANO
    // ====================================================
    public void CheckFloorCompletion()
    {
        if (expData.buildingGraph == null) { OnAllFloorsCompleted(); return; }

        var floorData = expData.buildingGraph.GetFloor(expData.currentFloor);

        if (!IsCurrentFloorFullyExplored(floorData))
        {
            Debug.LogWarning("[ExplMgr] Current floor not explored completely!");
            NavigateToUnexploredCorridor(floorData);
            return;
        }

        if (floorData != null) floorData.explored = true;
        Debug.Log($"[ExplMgr] Piano {expData.currentFloor} completato.");

        var unexplored = expData.buildingGraph.GetUnexploredFloors();
        if (unexplored.Count == 0)
        {
            Debug.Log("[ExplMgr] Tutti i piani esplorati.");
            OnAllFloorsCompleted();
            return;
        }

        // STEP 1 — connettore diretto dal piano corrente a un piano non esplorato
        // Ordina per vicinanza al piano corrente (evita di scendere quando si può salire)
        
        // Looks for adjaecent unexplored floors, if one of them exists, starts the exploration on that floor.
        if (FindAndExploreAdjacentFloors(unexplored)) return;

        // STEP 2 — nessun connettore diretto: cerca un piano intermedio già esplorato
        // che abbia un connettore verso un piano non ancora esplorato
        if (FindAndExploreOtherFloors(unexplored)) return;

        Debug.Log("[ExplMgr] Tutti i piani esplorati (o non raggiungibili).");
        OnAllFloorsCompleted();
    }
    

    // ──────────────────────────────────────────────────────────────
    // METODO 2: IsCurrentFloorFullyExplored
    // ──────────────────────────────────────────────────────────────
    private bool IsCurrentFloorFullyExplored(FloorNode floorData)
    {
        // 1. Il grafo del piano corrente deve avere tutti gli archi esplorati
        if (!expData.graph.IsFullyExplored())
        {
            Debug.LogWarning("[ExplMgr] Floor has unexplored edges.");
            return false;
        }
     
        // 2. Tutti i roomObjects del BuildingGraph per questo piano devono
        //    essere stati visitati come nodi nel grafo.
        if (floorData == null || floorData.roomObjects.Count <= 0) return true;
        foreach (string roomId in floorData.roomIds)
        {
            // Check diretto: nodo con ID uguale al nome del GameObject
            if (expData.graph.GetNode(roomId) != null) continue;
     
            // ── FIX BUG 2: i corridoi nel grafo si chiamano {id}_A e {id}_B ──
            // Un corridoio nel BuildingGraph si chiama es. "corridoio1",
            // ma nel TopologicalGraph esiste come "corridoio1_A" e "corridoio1_B".
            // Se almeno il polo A è presente, il corridoio è stato attraversato.
            var poleANode = expData.graph.GetCorridorPoleNode($"{roomId}_A");
            if (poleANode != null)
            {
                var centralEdge = poleANode.edges?.Find(e => e?.id == $"central_{roomId}");
                if (centralEdge is { state: EdgeState.Explored })
                    continue;

                // FIX: considera completato anche se entrambi i poli sono fisicamente visitati
                var poleBNode = expData.graph.GetNode($"{roomId}_B");
                if (poleANode.physicallyVisited && poleBNode is { physicallyVisited: true })
                {
                    Debug.Log($"[ExplMgr] '{roomId}' — entrambi i poli visitati, considero completato.");
                    continue;
                }

                Debug.Log($"[ExplMgr] Piano {expData.currentFloor}: '{roomId}' ha polo ma transito non completato.");
            }
                
            Debug.LogWarning("[ExplMgr] Floor has unexplored Rooms.");
            return false;
        }

        return true;
    }
    
    private Vector3 GetRoomCenter(GameObject roomObj)
    {
        if (roomObj == null) return Vector3.zero;

        var col = roomObj.GetComponentInChildren<Collider>();
        if (col != null) return col.bounds.center;

        var rend = roomObj.GetComponentInChildren<Renderer>();
        return rend != null ? rend.bounds.center : roomObj.transform.position;
    }
    
    // REFACTOR: extracted methods

    private bool HasRoomAlreadyBeenExplored(string roomId)
    {
        // Skip se la porta fisica verso questa stanza è già stata esplorata (anche con nome nodo diverso)
        string doorCheck = FindDoorBetweenNodes(expData.currentNodeId, roomId);
        if (!string.IsNullOrEmpty(doorCheck))
        {
            var currNode = expData.graph.GetNode(expData.currentNodeId);
            if (currNode != null && currNode.edges.Exists(e =>
                    e?.id == doorCheck && e.state == EdgeState.Explored))
                return true;
        }
        var poleANodeNav = expData.graph.GetCorridorPoleNode($"{roomId}_A");
        var centralEdgeNav = poleANodeNav?.edges?.Find(e => e?.id == $"central_{roomId}");
        return centralEdgeNav is { state: EdgeState.Explored };
        // davvero già transitato
        // altrimenti cade giù e NavigateToUnexploredCorridor lo naviga normalmente
    }

    

    private void CreateEdgeWithPreviousRoom(string roomId, Vector3 center)
    {
        var prevNode = expData.graph.GetNode(expData.currentNodeId);
        if (prevNode != null)
        {
            bool alreadyLinked = prevNode.edges.Exists(e => e != null && e.toNodeId == roomId);
            if (!alreadyLinked)
            {
                float dist = Vector3.Distance(prevNode.position, center);
                // Trova la porta fisica più vicina al nodo connettore corrente
                string synDoorName = FindDoorBetweenNodes(expData.currentNodeId, roomId);
                if (string.IsNullOrEmpty(synDoorName))
                    synDoorName = $"nav_{expData.currentNodeId}_{roomId}";

                var synEdge = expData.graph.AddRoomDoorEdge(expData.currentNodeId, synDoorName,
                    false, center, dist, roomId);
                if (synEdge != null)
                    expData.graph.MarkEdgeExplored(expData.currentNodeId, synEdge.id, roomId);
            }
        }
    }

    private void UpdateEdgeId(string roomId)
    {
        foreach (var node in expData.graph.AllNodes())
        {
            if (node == null) continue;
            foreach (var edge in node.edges)
            {
                if (edge != null && edge.toNodeId == node.id && edge.state == EdgeState.Explored)
                {
                    edge.toNodeId = roomId;
                    Debug.Log($"[ExplMgr] Arco {node.id}→{edge.id} corretto: toNodeId aggiornato a '{roomId}'");
                }
            }
        }
    }

    private bool CheckForRoomExistenceAndStatus(string roomId, GameObject roomObj) => 
        expData.graph.GetNode(roomId) != null || HasRoomAlreadyBeenExplored(roomId) || roomObj == null;

    private void NavigateToUnexploredCorridor(FloorNode floorData)
    {
        if (floorData != null)
        {
            foreach (string roomId in floorData.roomIds)
            {
                var roomObj = FindRoom(roomId, floorData);
                
                if (CheckForRoomExistenceAndStatus(roomId, roomObj)) continue;

                // se è un corridoio, usa MovingToPole invece di EnteringRoom
                var corridorData = FindCorridorData(roomId);
                if (corridorData != null)
                {
                    BeginMovingToPole(roomId, corridorData);
                    return;
                }

                Vector3 center = GetRoomCenter(roomObj);
                expData.graph.AddRoomNode(roomId,center);
                expData.graph.EnterNode(roomId);
                CreateEdgeWithPreviousRoom(roomId, center);

                UpdateEdgeId(roomId);
                expData.currentNodeId = roomId;
                navAgent.isStopped = false;
                navAgent.SetDestination(center);
                stateMachine.SetState(State.EnteringRoom);
                return;
            }
        }

        string targetId = expData.graph.FindNearestNodeWithDiscoveredEdges(transform.position);
        if (targetId == null) { OnAllFloorsCompleted(); return; }

        navAgent.SetDestination(expData.graph.GetNode(targetId).position);
        expData.currentNodeId = targetId;
        stateMachine.SetState(State.Backtracking);
    }
    
    //REFACTOR: methods extracted from ExecuteFloorChange 

    private string SetupConnectorAndNewFloor([NotNull] VerticalConnector activeConnector, int targetFloor)
    {
        int fromFloor = expData.currentFloor;
        bool isReverse = (activeConnector.floorFrom == targetFloor);

        
        traversedConnectors.Add(activeConnector.id);
        string connNodeId = activeConnector.id;

        Vector3 connPosCurrentFloor = isReverse
            ? activeConnector.triggerEndPosition
            : activeConnector.triggerStartPosition;

        Vector3 connPosDestFloor = isReverse
            ? activeConnector.triggerStartPosition
            : activeConnector.triggerEndPosition;

        if (expData.graph != null && expData.graph.GetNode(connNodeId) == null)
            expData.graph.AddRoomNode(connNodeId, connPosCurrentFloor);

        if (!expData.floorGraphs.ContainsKey(targetFloor))
            expData.floorGraphs[targetFloor] = new TopologicalGraph();

        var destGraph = expData.floorGraphs[targetFloor];
        if (destGraph.GetNode(connNodeId) == null)
            destGraph.AddRoomNode(connNodeId, connPosDestFloor);
            
        bool alreadyExists = expData.ConnectorLinks.Exists(l =>
            l.connectorId == activeConnector.id &&
            ((l.floorFrom == fromFloor   && l.floorTo == targetFloor) ||
             (l.floorFrom == targetFloor && l.floorTo == fromFloor)));

        if (!alreadyExists)
        {

            expData.ConnectorLinks.Add(new FloorConnectorLink
            {
                connectorId = activeConnector.id,
                floorFrom   = fromFloor,
                nodeIdFrom  = connNodeId,
                posFrom     = connPosCurrentFloor,
                floorTo     = targetFloor,
                nodeIdTo    = connNodeId,
                posTo       = connPosDestFloor,
                bidirectional = activeConnector.bidirectional 
            });
        }

        return connNodeId;
    }

    private (string adjRoom, string adjDoorName, Vector3 adjPos) FindAdjacentRoomThroughPhysicalDoor(string connNodeId, int targetFloor)
    {
        string adjRoom = null;
        string adjDoorName = null;
        Vector3 adjPos = Vector3.zero;
        var arrDoor = pendingArrivalDoor;
        string front = arrDoor.roomFront?.name;
        string back  = arrDoor.roomBack?.name;
        string notNullCandidate = string.IsNullOrEmpty(front) ? back : front;
        string candidate = (back == connNodeId)
            ? front
            : notNullCandidate;
        candidate = (front == connNodeId) ? back : candidate;

        var fdAdj = expData.buildingGraph?.GetFloor(targetFloor);
        if (!string.IsNullOrEmpty(candidate) && candidate != connNodeId
                                             && (fdAdj?.roomIds.Contains(candidate) ?? false))
        {
            adjRoom     = candidate;
            adjDoorName = arrDoor.gameObject.name;
            var rObj = FindRoom(adjRoom, fdAdj);
            adjPos      = rObj != null ? GetRoomCenter(rObj) : arrDoor.transform.position;
        }
        return (adjRoom, adjDoorName, adjPos);
    }

    private (string adjRoom, string adjDoorName, Vector3 adjPos) FindAdjacentRoomThroughRoomNodes(string connNodeId, int targetFloor)
    {
        string adjRoom = null;
        string adjDoorName = null;
        Vector3 adjPos = Vector3.zero;
        var fdAdj = expData.buildingGraph?.GetFloor(targetFloor);
        if (fdAdj != null)
        {
            foreach (string rid in fdAdj.roomIds)
            {
                string dName = FindDoorBetweenNodes(connNodeId, rid);
                if (string.IsNullOrEmpty(dName)) continue;
                var rObj = FindRoom(rid, fdAdj);
                if (rObj == null) continue;
                adjRoom     = rid;
                adjDoorName = dName;
                adjPos      = GetRoomCenter(rObj);
                break;
            }
        }
        Debug.Log("adjRoom: " + adjRoom);
        return (adjRoom, adjDoorName, adjPos);
    }

    private (string adjRoom, string adjDoorName, Vector3 adjPos) FindAdjacentRoom(string connNodeId, int targetFloor)
    {
        string adjRoom = null;
        string adjDoorName = null;
        Vector3 adjPos = Vector3.zero;
        // Tentativo 1: usa la porta di arrivo fisica
        if (pendingArrivalDoor != null)
        {
            (adjRoom, adjDoorName, adjPos) = FindAdjacentRoomThroughPhysicalDoor(connNodeId, targetFloor);
        }
        pendingArrivalDoor = null;

        // Tentativo 2: fallback tramite FindDoorBetweenNodes
        if (string.IsNullOrEmpty(adjRoom))
        {
            (adjRoom, adjDoorName, adjPos) = FindAdjacentRoomThroughRoomNodes(connNodeId, targetFloor);
        }

        return (adjRoom, adjDoorName, adjPos);
    }

    private bool IsAdjacentRoomACorridorWithSetup(string adjRoom)
    {
        // Se adjRoom è un corridoio con poli, non trattarlo come stanza normale
        var adjCorridor = FindCorridorData(adjRoom);
        if (adjCorridor != null)
        {
            BeginMovingToPole1(adjRoom, adjCorridor, false);
            Debug.Log($"[ExplMgr] Destinazione è corridoio '{adjRoom}' → vado al polo.");
            return true;
        }

        return false;
    }
    
    private void SetupAdjacentRoomAsRoom(string adjRoom, Vector3 adjPos, string connNodeId, string adjDoorName)
    {
        if (expData.graph.GetNode(adjRoom) == null)
            expData.graph.AddRoomNode(adjRoom, adjPos);

        float dAdj = Vector3.Distance(expData.graph.GetNode(connNodeId).position, adjPos);
        var adjEdge = expData.graph.AddRoomDoorEdge(connNodeId, adjDoorName,
            false, adjPos, dAdj, adjRoom);
        if (adjEdge != null)
            expData.graph.MarkEdgeExplored(connNodeId, adjEdge.id, adjRoom);

        expData.graph.EnterNode(adjRoom);
        expData.currentNodeId = adjRoom;

        expData.inRoomMode             = false;
        expData.movingToPoleCorridorId = null;
        expData.idleTargetCorridorId   = null;

        navAgent.isStopped = false;
        navAgent.SetDestination(adjPos);
        expData.enteringRoomTimer   = 0f;
        expData.enteringRoomTimeout = 6f;
        stateMachine.SetState(State.EnteringRoom);

        Debug.Log($"[ExplMgr] Cammino verso '{adjRoom}' via '{adjDoorName}'.");
    }

    [CanBeNull]
    private GameObject FindConnectorRoomWithGuards(string dName, string connNodeId, string rid, FloorNode fd2)
    {
        // Checks both for door and edge existence
        if (string.IsNullOrEmpty(dName)) return null;
        bool isConnTrigger = FindConnectorForDoor(dName) != null;
        if (isConnTrigger) return null;
        var existingEdge = expData.graph.GetNode(connNodeId)?.edges
            .Find(e => e?.toNodeId == rid || e?.id == dName);
        if (existingEdge != null) return null;
        var roomObj = FindRoom(rid, fd2);
        return roomObj;
    }
    
    private void ConnectAlreadyKnownRoomsToConnectorNode(string connNodeId, int targetFloor)
    {
        var fd2 = expData.buildingGraph?.GetFloor(targetFloor);
        if (fd2 != null)
        {
            foreach (string rid in fd2.roomIds)
            {
                // cerca la porta fisica tra connettore e stanza
                string dName = FindDoorBetweenNodes(connNodeId, rid);
                GameObject roomObj = FindConnectorRoomWithGuards(dName, connNodeId, rid, fd2);
                if (roomObj == null) return;
                Vector3 rCenter = GetRoomCenter(roomObj);
                float d = Vector3.Distance(expData.graph.GetNode(connNodeId).position, rCenter);
                expData.graph.AddRoomDoorEdge(connNodeId, dName, false, rCenter, d, rid);
            }
        }
    }

    private bool HandleNextFloorArrival(string connNodeId, int targetFloor)
    {
        var floorData = expData.buildingGraph?.GetFloor(targetFloor);
        if (floorData != null)
        {
            GetToNextFloorSpawnPosition(floorData, targetFloor, connNodeId);
            if (IsCurrentFloorFullyExplored(floorData))
            {
                CheckFloorCompletion();
                return true;
            }
            if (!string.IsNullOrEmpty(connNodeId))
            {
                CorridorData exitCorridor = FindCorridorContainingPosition(navAgent.transform.position, 3f);
                if (exitCorridor != null)
                { 
                    BeginMovingToPole1(exitCorridor, true);
                    return true;  // ← salta Tentativo 1/2 e il blocco corridoio finale
                }

                (string adjRoom, string adjDoorName, Vector3 adjPos) = FindAdjacentRoom(connNodeId, targetFloor);

                Debug.Log("Adjacent room: " + adjRoom);
                
                if (!string.IsNullOrEmpty(adjRoom))
                {
                    if (IsAdjacentRoomACorridorWithSetup(adjRoom)) return true;

                    // adjRoom è una stanza normale, gestione originale
                    SetupAdjacentRoomAsRoom(adjRoom, adjPos, connNodeId, adjDoorName);
                    return true;
                }
                // pre-collega le stanze già note del piano destinazione al nodo connettore
                ConnectAlreadyKnownRoomsToConnectorNode(connNodeId, targetFloor);

            }
        }

        return false;
    }

    private bool HasCorridorAlreadyBeenExplored(CorridorPoleNode poleANode, CorridorData spawnCorridor) => 
        poleANode != null &&
        poleANode.edges.Exists(e => e?.id == $"central_{spawnCorridor.corridorId}"
                                                   && e.state == EdgeState.Explored);

    private void GetToNextFloorSpawnPosition(FloorNode floorData, int targetFloor, string connNodeId)
    {
        string nextFloorSpawnId =
            (floorData.spawnObject != null ? floorData.spawnObject.name : $"floor_{targetFloor}_entry");   
        string spawnId = !string.IsNullOrEmpty(connNodeId)
            ? connNodeId
            : nextFloorSpawnId;

        expData.currentNodeId = spawnId;
        if (expData.graph.GetNode(expData.currentNodeId) == null)
            expData.graph.AddRoomNode(expData.currentNodeId, floorData.spawnPosition);

        expData.graph.EnterNode(expData.currentNodeId);
    }
    
    
    
    private void ExecuteFloorChange(int targetFloor)
    {
        if (stateMachine.IsState(State.Completed)) return;

        
        string connNodeId = string.Empty;
        var activeConnector = expData.pendingConnector;

        if (activeConnector != null)
        {
            connNodeId = SetupConnectorAndNewFloor(activeConnector, targetFloor);
        }
        else if (!expData.floorGraphs.ContainsKey(targetFloor))
        {
            expData.floorGraphs[targetFloor] = new TopologicalGraph();
        }

        expData.currentFloor     = targetFloor;
        expData.pendingConnector = null;
        

        registeredRoomDoors.Clear();
        registeredTransitDoors.Clear();
        allCorridorDoors.Clear();

        if (HandleNextFloorArrival(connNodeId, targetFloor)) return;

        expData.inRoomMode             = false;
        expData.movingToPoleCorridorId = null;
        expData.idleTargetCorridorId   = null;

        CorridorData spawnCorridor = FindCorridorContainingPosition(navAgent.transform.position, 3f);
        if (spawnCorridor == null) { 
            StartRotation360(isRoom: true);
            return;
        }
        // Controlla se il corridoio è già stato completamente esplorato in questo piano
        var poleANode = expData.graph.GetCorridorPoleNode($"{spawnCorridor.corridorId}_A");
        bool alreadyDone = HasCorridorAlreadyBeenExplored(poleANode, spawnCorridor);
        
        if (alreadyDone)
        {
            Debug.Log($"[ExplMgr] Corridoio '{spawnCorridor.corridorId}' già esplorato — skip.");
            StartRotation360(isRoom: true);
        }
        else
        {
            Debug.Log($"[ExplMgr] Spawn in corridoio '{spawnCorridor.corridorId}' — avvio modalità corridoio.");
            BeginMovingToPole1(spawnCorridor, false);
        }
        
    }
    
    // ====================================================
    // FINE ESPLORAZIONE
    // ====================================================

    private void OnAllFloorsCompleted()
    {
        expData.explorationVisionCone?.SetExplorationMode(false);
        stateMachine.SetState(State.Completed);

        // ── 1. Calcola distanze Stanze ────────────────────────────
        var ddm = GetComponent<RoomDistanceMap>();
        if (ddm != null)
            ddm.Compute(expData.floorGraphs);
        else
            Debug.LogWarning("[ExplMgr] RoomDistanceMap non trovato sul GameObject!");

        List<RoomDistancePair> roomPairs = ddm?.pairs ?? new List<RoomDistancePair>();

        // ── 2. Costruisci snapshot ───────────────────────────────
        BuildingSnapshot snapshot = GraphSnapshotBuilder.Build(
            expData.floorGraphs, expData.ConnectorLinks, roomPairs, _roomArtifacts, expData.buildingId);

        snapshot.discoveredObjects.AddRange(_artifactData.Values);
        // ── 3. Salva su disco ────────────────────────────────────
        bool saved = GraphPersistence.Save(snapshot);
        if (!saved)
            Debug.LogWarning("[ExplMgr] Salvataggio snapshot fallito: " +
                            "la trasmissione a JaCaMo procede comunque.");

        
        // ── 4. Salva JSON artefatti separato ─────────────────────
        ArtifactsPersistence.Save(expData.buildingId, _roomArtifacts, _artifactData);


        // ── 6. Trasmetti a JaCaMo ────────────────────────────────
        var bt = GetComponent<BeliefTransmitter>();
        bt?.TransmitSnapshot(snapshot);

         // ── 7. Aggiorna current_room col nodo reale di fine esplorazione ──
        var loc = GetComponent<LocalizationTest>();  
        loc?.ForceCurrentRoomAndSend(NormalizeRoomId(expData.currentNodeId));
        Debug.Log($"[ExplMgr] Edificio '{expData.buildingId}' esplorato. " +
                $"Piani: {expData.floorGraphs.Count}  Link: {expData.ConnectorLinks.Count}  " +
                $"Distanze: {roomPairs.Count}  Snapshot salvato: {saved}");
    }
   
    // ====================================================
    // HELPER
    // ====================================================
    public CorridorData FindCorridorData(string id)
    {
        foreach (var c in FindObjectsByType<CorridorData>(FindObjectsSortMode.None))
            if (c.corridorId == id) return c;
        return null;
    }

    private CorridorData FindCorridorContainingPosition(Vector3 pos, float radius)
    {
        foreach (var c in FindObjectsByType<CorridorData>(FindObjectsSortMode.None))
        {
            // La posizione è tra i due poli (proiezione sull'asse del corridoio)
            Vector3 axis = c.Pole2Position - c.Pole1Position;
            float len    = axis.magnitude;
            if (len < 0.01f) continue;
            Vector3 dir  = axis / len;
            float proj   = Vector3.Dot(pos - c.Pole1Position, dir);
            // Distanza laterale dall'asse
            Vector3 onAxis  = c.Pole1Position + dir * Mathf.Clamp(proj, 0f, len);
            float lateralDist = Vector3.Distance(pos, onAxis);
            if (lateralDist <= radius && proj >= -radius && proj <= len + radius)
                return c;
        }
        return null;
    }
    private string NormalizeRoomId(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return nodeId;
        if (nodeId.EndsWith("_A") || nodeId.EndsWith("_B"))
            return nodeId.Substring(0, nodeId.Length - 2);   // corridoio2_B → corridoio2
        return nodeId;
    }

    [CanBeNull]
    public DoorVarcoScript FindDoorScript([CanBeNull] string name)
    {
        if (name == null) return null;
        string cleanName = name;
        if (name.StartsWith("fw_"))   cleanName = name.Substring(3);
        if (name.StartsWith("bw_"))   cleanName = name.Substring(3);

        var go = GameObject.Find(cleanName);
        if (go == null) return null;
        return go.GetComponentInChildren<DoorVarcoScript>();
    }

    private bool IsConnectorDoor(string doorName)
    {
        if (expData.buildingGraph == null) return false;
        foreach (var connector in expData.buildingGraph.connectors)
        {
            if (connector.triggerStartObject?.name == doorName) return true;
            if (connector.triggerEndObject?.name   == doorName) return true;
        }
        return false;
    }
    private VerticalConnector FindConnectorForDoor(string doorName)
    {
        if (expData.buildingGraph == null) return null;
        foreach (var c in expData.buildingGraph.connectors)
        {
            if (c.triggerStartObject?.name == doorName) return c;
            if (c.triggerEndObject?.name == doorName) return c;
        }
        return null;
    }

    public void ResetCorridorPoles(string corridorId)
    {
        foreach (var pole in FindObjectsByType<CorridorPoleScript>(FindObjectsSortMode.None))
        {
            if (pole.corridorId == corridorId)
                pole.ResetTrigger();
        }
    }

    //PORTE PER LE SCALE
    public IEnumerator TraverseStairsThen(int targetFloor) {
        var activeConnector = expData.pendingConnector;

        // Apri porta di partenza
        bool isNormalDir = (activeConnector == null || activeConnector.floorFrom != targetFloor);
        var departureDoorObj = isNormalDir
            ? activeConnector?.triggerStartObject
            : activeConnector.triggerEndObject;

        if (departureDoorObj != null)
        {
            var departureDoor = departureDoorObj.GetComponentInChildren<DoorVarcoScript>();
            if (departureDoor != null && !departureDoor.IsTraversable)
            {
                departureDoor.TryOpen();
                yield return new WaitForSeconds(0.6f);
            }
        }
        
        if (activeConnector == null) throw new NoNullAllowedException(nameof(activeConnector) + " cannot be null");

        // Aspetta che il NavMesh porti l'agente fisicamente all'altro piano
        // (il NavMesh cammina sulle scale autonomamente)
        Vector3 destination = isNormalDir
            ? activeConnector.triggerEndPosition
            : activeConnector.triggerStartPosition;

        navAgent.isStopped = false;
        navAgent.SetDestination(destination);

        // Aspetta l'arrivo
        yield return new WaitForSeconds(0.5f); // piccolo delay per far partire il path
        while (navAgent.pathPending ||
               navAgent.remainingDistance > expData.arrivalThreshold * 2f)
        {
            yield return null;
        }

        // Apri porta di arrivo
        var arrivalDoorObj = isNormalDir
            ? activeConnector.triggerEndObject
            : activeConnector.triggerStartObject;

        if (arrivalDoorObj != null)
        {
            var arrivalDoor = arrivalDoorObj.GetComponentInChildren<DoorVarcoScript>();
            if (arrivalDoor != null && !arrivalDoor.IsTraversable)
            {
                arrivalDoor.TryOpen();
                yield return new WaitForSeconds(0.6f);
            }
        }

        ExecuteFloorChange(targetFloor);
    }

    private bool isCorridorDoorAlreadyRegistered(DoorVarcoScript door) => 
        !door.isCorridorLink 
        && (registeredTransitDoors.Contains(door.gameObject.name) 
            || allCorridorDoors.Contains(door.gameObject.name));

    private void ScanCorridorDoorsFromScript(string corridorId)
    {
        foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
        {
            string frontName = door.roomFront?.name ?? "";
            string backName  = door.roomBack?.name  ?? "";

            if (frontName != corridorId && backName != corridorId) continue;

            if (isCorridorDoorAlreadyRegistered(door)) { continue; }

            float dA = Vector3.Distance(door.transform.position, expData.poleAPos);
            float dB = Vector3.Distance(door.transform.position, expData.poleBPos);
            string otherSide = (frontName == corridorId) ? backName : frontName;
            string side = ComputeSideRelativeToCorridorAxis(door.transform.position, expData.poleAPos, expData.poleBPos);

            
            if (door.isCorridorLink)
            {
                string poleBId = $"{corridorId}_B";
                float dist = Vector3.Distance(
                    expData.graph.GetNode(poleBId)?.position ?? door.transform.position,
                    door.transform.position);
                var corrLinkEdge = expData.graph.AddRoomDoorEdge(poleBId, door.gameObject.name,
                    false, door.transform.position, dist,
                    toNodeId: otherSide);

                // Marca subito esplorato se il corridoio destinazione è già visitato
                var targetCorrNode = expData.graph.GetNode(otherSide) ?? expData.graph.GetNode($"{otherSide}_A");
                bool targetAlreadyVisited = targetCorrNode is { physicallyVisited: true };
                if (corrLinkEdge != null && targetAlreadyVisited)
                    expData.graph.MarkEdgeExplored(poleBId, corrLinkEdge.id, otherSide);
            }
            else
            {
                expData.graph.AddDoorEdges(corridorId, door.gameObject.name,
                    door.elementType == DoorVarcoScript.ElementType.Door,
                    door.transform.position, side, dA, dB,
                    isCorridorLink: false);
            }

            registeredTransitDoors.Add(door.gameObject.name);
            allCorridorDoors.Add(door.gameObject.name);
        }
    }

    private bool AddEdgeToCorridorDoor(string roomId, string roomBeyond, DoorVarcoScript door)
    {
        bool leadsToCorridor = FindCorridorData(roomBeyond) != null;
        if (leadsToCorridor)
        {
            // Se la stanza corrente ha poli propri, aggiungi arco dal polo B
            bool currentHasPoles = FindCorridorData(roomId) != null;
            if (currentHasPoles)
            {
                string poleBId = $"{roomId}_B";
                float dist = door.NavMeshDistanceTo(transform.position);
                expData.graph.AddRoomDoorEdge(poleBId, door.gameObject.name,
                    door.elementType == DoorVarcoScript.ElementType.Door,
                    door.transform.position, dist,
                    toNodeId: roomBeyond);
            }
            return true;
        }

        return false;
    }

    private bool IsRoomDoorAlreadyRegistered(string roomId, DoorVarcoScript door)  => 
        registeredRoomDoors.TryGetValue(door.gameObject.name, out string from)
               && from == roomId;

    private void ScanRoomDoorsFromScript(string roomId)
    {
        foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
        {
            if (door.roomFront == null || door.roomBack == null || IsConnectorDoor(door.gameObject.name)) continue;

            string frontName = door.roomFront.name;
            string backName  = door.roomBack.name;
            if (frontName != roomId && backName != roomId) continue;

            // Con il nuovo Dictionary: controlla se già registrata da QUESTA stanza
            if (IsRoomDoorAlreadyRegistered(roomId, door)) continue;
            string roomBeyond = (frontName == roomId) ? backName : frontName;

            // ← NON aggiungere se la destinazione è già stata fisicamente visitata
            var beyondNode = expData.graph.GetNode(roomBeyond);
            if (beyondNode is { physicallyVisited: true }) continue;

            if(AddEdgeToCorridorDoor(roomId, roomBeyond, door)) continue;

            registeredRoomDoors[door.gameObject.name] = roomId;
            door.discovered = true;

            float navDist = door.NavMeshDistanceTo(transform.position);
            
            expData.graph.AddRoomDoorEdge(roomId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, navDist,
                toNodeId: null);
        }
    }


    private string FindDoorBetweenNodesByConnector(VerticalConnector conn, string nodeA, string nodeB)
    {
        
        bool aIsStart = (conn.id == nodeA || conn.triggerStartObject.name == nodeA);
        bool aIsEnd = (conn.id == nodeA || conn.triggerEndObject.name == nodeA);

        //verifica anche che nodeB corrisponda al lato opposto
        bool bMatchesEnd = (conn.id == nodeB || conn.triggerEndObject.name == nodeB);
        bool bMatchesStart = (conn.id == nodeB || conn.triggerStartObject.name == nodeB);
        
        if (aIsStart && bMatchesEnd && conn.triggerEndObject != null)
            return conn.triggerEndObject.name;
        if (aIsEnd && bMatchesStart && conn.triggerStartObject != null)
            return conn.triggerStartObject.name;

        return null;
    }

    private string FindDoorBetweenNodesByDoor(DoorVarcoScript d, string  nodeA, string nodeB)
    {
        
        string front = d.roomFront?.name;
        string back = d.roomBack?.name;

        if (front != null && back != null &&
            ((front == nodeA && back == nodeB) || (front == nodeB && back == nodeA)))
        {
            return d.gameObject.name;
        }

        return null;
    }

    private string FindDoorBetweenNodes(string nodeA, string nodeB)
    {
        string dName = null;
        foreach (var conn in expData.buildingGraph?.connectors ?? new List<VerticalConnector>())
        {
            dName = FindDoorBetweenNodesByConnector(conn, nodeA, nodeB) ?? dName;
        }
        // fallback DoorVarcoScript — invariato
        foreach (var d in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
        {
            dName = FindDoorBetweenNodesByDoor(d, nodeA, nodeB) ?? dName;
        }
        return dName;
    }

    private float NavMeshDist(Vector3 a, Vector3 b)
    {
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path)) return -1f;
        if (path.status == NavMeshPathStatus.PathInvalid) return -1f;
        float d = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            d += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return d;
    }

    public void RegisterDiscoveredObject(string artifactId, string roomId,
        string artifactType, int wsPort, float x, float y, float z)
    {
        // Aggiorna dato completo
        _artifactData[artifactId] = new RoomObjectSnapshot
        {
            artifactId   = artifactId,
            artifactType = artifactType,
            roomId       = roomId,
            wsPort       = wsPort,
            x = x, y = y, z = z,
        };

        // Rimuovi da stanza precedente se cambiata
        foreach (var kv in _roomArtifacts)
            if (kv.Key != roomId)
                kv.Value.Remove(artifactId);

        // Aggiungi alla stanza corrente
        if (!_roomArtifacts.ContainsKey(roomId))
            _roomArtifacts[roomId] = new HashSet<string>();
        _roomArtifacts[roomId].Add(artifactId);
    }

    public void EndRotating360()
    {
        navAgent.isStopped = false;
        expData.rotationAccum = 0f;
        Debug.Log($"[ExplMgr] 360° completato in '{expData.currentNodeId}'.");

        if (expData.currentNodeId != null && expData.currentNodeId.EndsWith("_B"))
        {
            string corrId = expData.currentNodeId.Replace("_B", "");
            ComputeCorridorOrders(corrId);
        }

        if (expData.inRoomMode && !string.IsNullOrEmpty(expData.currentNodeId))
        {
            var currentNode = expData.graph.GetNode(expData.currentNodeId);
            bool hasUnexploredDoors = currentNode != null &&
                                      currentNode.edges.Exists(e => e != null &&
                                                                    e.state != EdgeState.Explored &&
                                                                    e.edgeType == EdgeType.Door);
            if (!hasUnexploredDoors)
                ScanRoomDoorsFromScript(expData.currentNodeId);
        }
    }
    
    public void BeginMovingToPole(string roomId, CorridorData corridorData)
    {
        float d1 = Vector3.Distance(transform.position, corridorData.Pole1Position);
        float d2 = Vector3.Distance(transform.position, corridorData.Pole2Position);
        expData.SetPendingCorridorAndPole(roomId, 
            d1 <= d2 ? "1" : "2", 
            d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
        expData.movingToPoleCorridorId = roomId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        navAgent.SetDestination(d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
        stateMachine.SetState(State.MovingToPole);
    }
    
    public void BeginMovingToPole(Vector3 startPos, string startNodeId, CorridorData spawnCorridor)
    {
        float d1 = Vector3.Distance(startPos, spawnCorridor.Pole1Position);
        float d2 = Vector3.Distance(startPos, spawnCorridor.Pole2Position);
        expData.SetPendingCorridorAndPole(startNodeId, 
            d1 <= d2 ? "1" : "2", 
            d1 <= d2 ? spawnCorridor.Pole1Position : spawnCorridor.Pole2Position);
        expData.movingToPoleCorridorId = startNodeId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        navAgent.SetDestination(d1 <= d2 ? spawnCorridor.Pole1Position : spawnCorridor.Pole2Position);
        stateMachine.SetState(State.MovingToPole);
    }
    
    public void BeginMovingToPole1(CorridorData corridor, bool nullDoor)
    {
        if(nullDoor) pendingArrivalDoor = null;
        expData.SetPendingCorridorAndPole(corridor.corridorId, "1",  corridor.Pole1Position);
        expData.movingToPoleCorridorId = corridor.corridorId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        navAgent.SetDestination(corridor.Pole1Position);
        stateMachine.SetState(State.MovingToPole);
    }
    
    public void BeginMovingToPole1(string roomId, CorridorData corridor, bool nullDoor)
    {
        if(nullDoor) pendingArrivalDoor = null;
        expData.SetPendingCorridorAndPole(roomId, "1",  corridor.Pole1Position);
        expData.movingToPoleCorridorId = roomId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        navAgent.SetDestination(corridor.Pole1Position);
        stateMachine.SetState(State.MovingToPole);
    }
    public void BeginTransitingToB(Vector3 poleAPos, Vector3 targetB)
    {
        navAgent.isStopped = false;
        navAgent.SetDestination(targetB);
        stateMachine.SetState(State.Transiting);
    }
    
    public void MoveToNextEdge(GraphEdge nextEdge)
    {
        expData.currentEdge = nextEdge;
        navAgent.isStopped = false;
        navAgent.SetDestination(nextEdge.position);
        stateMachine.SetState(State.MovingToDoor);
        Debug.Log($"[ExplMgr] Prossima porta: {nextEdge.id} navDist={nextEdge.navMeshDist:F1}");
    }
}