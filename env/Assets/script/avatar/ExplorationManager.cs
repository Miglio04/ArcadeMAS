using System.Collections.Generic;
using System.Diagnostics;
using JetBrains.Annotations;
using script.avatar.ExplorationHelpers;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace script.avatar
{
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
    [RequireComponent(typeof(GraphConstruction))]
    [RequireComponent(typeof(ExplorationNavigation))]
    public class ExplorationManager : MonoBehaviour
    {
        private bool explorationStarted = false;
        
        private ExplorationData expData;

        private StateMachine stateMachine;

        private GraphConstruction graphConstruction;

        private ExplorationNavigation expNav;
        
        // --------------------------------------------------------
        // EVENTI
        // --------------------------------------------------------
        void Awake()
        {
            expData = GetComponent<ExplorationData>();
            stateMachine = GetComponent<StateMachine>();
            graphConstruction = GetComponent<GraphConstruction>();
            expNav = GetComponent<ExplorationNavigation>();

            // GraphConstruction
            GraphConstruction.OnCorridorDoorsScanNeeded += ScanCorridorDoorsFromScript;
            GraphConstruction.OnRotationRequested += StartRotation360;
            GraphConstruction.OnBacktrackNodeNull += CheckFloorCompletion;
            GraphConstruction.OnCurrentNodeNull += DoBacktrack;
            
            // ExplorationNavigation
            ExplorationNavigation.OnStairsTraversed += ExecuteFloorChange;
            
            // StateMachine
            StateMachine.OnCurrentNodeNullAfterBacktracking += CheckFloorCompletion;
            StateMachine.OnNavigationEnded += ProcessPoleTouched;
            StateMachine.OnPoleTouchedWhileTransiting += PoleTouchedWhileTransiting;
            StateMachine.OnPoleTouchedWhileIdle += PoleTouchedWhileIdle;
            StateMachine.OnNextActionNeeded += DecideNextAction;
            StateMachine.OnRotationRequested += StartRotation360;
            StateMachine.OnRotationCompleted += EndRotating360;
            StateMachine.OnDoorTraversable += EnterThroughEdge;
            
            // CorridorPoleScript
            CorridorPoleScript.OnPoleTouched            += stateMachine.HandlePoleTouched;
            
            // ExplorationVisionCone
            ExplorationVisionCone.OnDoorDiscovered      += HandleDoorDiscovered;
            ExplorationVisionCone.OnCorridorPoleVisible += stateMachine.HandleCorridorPoleVisible;
            ExplorationVisionCone.OnDoorVisibleInRoom   += HandleDoorVisibleInRoom;
            
            //VerticalConnectorScript
            VerticalConnectorScript.OnConnectorReached  += HandleConnectorReached;
        
        }

        void OnDestroy()
        {
            // GraphConstruction
            GraphConstruction.OnCorridorDoorsScanNeeded -= ScanCorridorDoorsFromScript;
            GraphConstruction.OnRotationRequested -= StartRotation360;
            GraphConstruction.OnBacktrackNodeNull -= CheckFloorCompletion;
            GraphConstruction.OnCurrentNodeNull -= DoBacktrack;
            
            // ExplorationNavigation
            ExplorationNavigation.OnStairsTraversed -= ExecuteFloorChange;
            
            // StateMachine
            StateMachine.OnCurrentNodeNullAfterBacktracking -= CheckFloorCompletion;
            StateMachine.OnNavigationEnded -= ProcessPoleTouched;
            StateMachine.OnPoleTouchedWhileTransiting -= PoleTouchedWhileTransiting;
            StateMachine.OnPoleTouchedWhileIdle -= PoleTouchedWhileIdle;
            StateMachine.OnNextActionNeeded -= DecideNextAction;
            StateMachine.OnRotationRequested -= StartRotation360;
            StateMachine.OnRotationCompleted -= EndRotating360;
            StateMachine.OnDoorTraversable -= EnterThroughEdge;
            
                        
            // CorridorPoleScript
            CorridorPoleScript.OnPoleTouched            -= stateMachine.HandlePoleTouched;
            
            // ExplorationVisionCone
            ExplorationVisionCone.OnDoorDiscovered      -= HandleDoorDiscovered;
            ExplorationVisionCone.OnCorridorPoleVisible -= stateMachine.HandleCorridorPoleVisible;
            ExplorationVisionCone.OnDoorVisibleInRoom   -= HandleDoorVisibleInRoom;
            
            //VerticalConnectorScript
            VerticalConnectorScript.OnConnectorReached  -= HandleConnectorReached;
        
        }

        // --------------------------------------------------------
        // AVVIO
        // --------------------------------------------------------
        void Start()
        {
            if (expData.buildingGraph == null)
            {
                Debug.LogWarning("[ExplMgr] BuildingGraph is null!");
                return;
            }
    
            // Check if snapshot already exists 
            if (GraphPersistence.Exists(expData.buildingId))
            {
                BuildingSnapshot snapshot = GraphPersistence.Load(expData.buildingId);
    
                if (snapshot != null)
                {
                    Debug.Log($"[ExplMgr] Snapshot found for '{expData.buildingId}': " +
                              $"exploration skipped, transmitting to JaCaMo.");
    
                    var bt = GetComponent<BeliefTransmitter>();
                    bt?.TransmitSnapshot(snapshot);
                    
                    stateMachine.SetState(State.Completed);
                    return;
                }

                // Start exploration in case of corrupted or unreadable file
                Debug.LogWarning($"[ExplMgr] Snapshot corrotto per '{expData.buildingId}': riesplorazione.");
            }
    
        
            // No snapshot: start exploration
            FloorNode floorData = expNav.PrepareExploration();
            if (floorData == null) return;
    
            expData.currentFloor = floorData.floorIndex;
            StartExploration(floorData.spawnObject.name, floorData.spawnPosition);
        }
        
        // UPDATE
        void Update()
        {
            stateMachine.Tick();
        }
        
        // ──────────────────────────────────────────────────────────────
        // METHOD 1: StartExploration
        // ──────────────────────────────────────────────────────────────
        private void StartExploration(string startNodeId, Vector3 startPos)
        {
            if (explorationStarted) return;
            explorationStarted = true;
     

            Debug.Log($"[ExplMgr] Piano di partenza: {expData.currentFloor}");
            
            expData.floorGraphs[expData.currentFloor] = new TopologicalGraph();
     
            expData.explorationVisionCone?.SetExplorationMode(true);
            expData.currentNodeId = startNodeId;
            CorridorPoleScript.OnPoleTouched -= stateMachine.HandlePoleTouched;
            CorridorPoleScript.OnPoleTouched += stateMachine.HandlePoleTouched;

            var spawnCorridor = ExplorationUtils.FindCorridorData(startNodeId);
            if (spawnCorridor != null)
            {
                // If the spawn is inside a corridor move to the nearest pole.
                expNav.BeginMovingToPole(startPos, startNodeId, spawnCorridor);
                stateMachine.SetState(State.MovingToPole);
            }
            else
            {
                graphConstruction.AddRoomAndEnterNode(startNodeId, startPos);
                StartRotation360(isRoom: true);
            }

            Debug.Log($"[ExplMgr] Avviato da '{startNodeId}' piano {expData.currentFloor}");
        }

        private void PoleTouchedWhileTransiting(string corridorId, string poleId, Vector3 polePos)
        {
            float distToB = Vector3.Distance(transform.position, expData.poleBPos);
            float distToA = Vector3.Distance(transform.position, expData.poleAPos);
            if (distToA < distToB && distToB > expData.poleArrivalThreshold)
            {
                return;
            }
            expData.SetPendingCorridorAndPole(corridorId, poleId, polePos);
            ProcessPoleTouched();
        }


        private void PoleTouchedWhileIdle(string corridorId, string poleId, Vector3 polePos)
        {
            if (string.IsNullOrEmpty(expData.idleTargetCorridorId)) return;
            // If the target of the movement is a defined corridor, don't stop when touching another pole
            if (corridorId != expData.idleTargetCorridorId)
            {
                ExplorationUtils.ResetCorridorPoles(corridorId);
                return;
            }
            
            var poleANode = graphConstruction.GetCorridorPoleNode($"{corridorId}_A");
            // Even if the corridor has been explored, some doors might not be.
            if (poleANode != null && !graphConstruction.HandleAlreadyExploredPole(poleANode, corridorId, poleId)) return;
            // Clean target if the intended corridor gets reached
            expData.idleTargetCorridorId = null;
            expData.SetPendingCorridorAndPole(corridorId, poleId, polePos);
            ProcessPoleTouched();
        }

        private void ProcessPoleTouched()
        {
            var corridorData = ExplorationUtils.FindCorridorData(expData.pendingCorridorId);
            if (corridorData == null) return;
        
            if (!stateMachine.IsState(State.Transiting))
            {
                TouchedFirstPole(corridorData);
            }
            else
            {
                graphConstruction.TouchedSecondPole();
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
            if (expData.registeredTransitDoors.Contains(door.gameObject.name)) return;
            if (expData.allCorridorDoors.Contains(door.gameObject.name)) return;

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

            expData.registeredTransitDoors.Add(door.gameObject.name);
            if (!isCorrLink) expData.allCorridorDoors.Add(door.gameObject.name);
        
            if(!isCorrLink) graphConstruction.AddCorridorDoorEdges(corridorId, door, door.distFromA, door.distFromB);
        

            Debug.Log($"[ExplMgr] Porta censita: {door.gameObject.name} " +
                      $"corridorLink={isCorrLink}");
        }

        private void HandleDoorVisibleInConnectorAlreadyTransited(DoorVarcoScript door, VerticalConnector connectorForDoor)
        {
            if (expData.TryRegisterRoomDoor(door)) return;
            if (!expData.traversedConnectors.Contains(expData.currentNodeId))
                return;

            // Determina la stanza di destinazione
            string connRoomBeyond = door.GetRoomNameBeyondDoor(transform.position);

            // FIX self-loop: se GetRoomNameBeyondDoor restituisce il nodo corrente
            // (succede quando door.roomFront/roomBack non sono configurati correttamente),
            // cerco la stanza non ancora visitata del piano corrente più vicina alla porta.
            if (expData.IsRoomPointedByConnectorDoorInvalid(connRoomBeyond, connectorForDoor))
            {
                connRoomBeyond = FindNearestRoomToDoor(door, connectorForDoor);
            }

            // Se ancora non abbiamo una destinazione valida, abbandoniamo
            if (expData.IsRoomPointedByConnectorDoorInvalid(connRoomBeyond, connectorForDoor))
                return;

            string targetId = connRoomBeyond;

            graphConstruction.AddRoomNodeAndMarkEdgeExplored(targetId, door);
        }

        private void HandleDoorVisibleInConnector(DoorVarcoScript door, VerticalConnector connectorForDoor)
        {
            string corridorBeyond = door.GetRoomNameBeyondDoor(transform.position);
            if (!expData.TryRegisterRoomDoor(door))
            {
                if (!string.IsNullOrEmpty(corridorBeyond) && ExplorationUtils.FindCorridorData(corridorBeyond) != null)
                {
                    graphConstruction.AddEdgeToUnexploredCorridor(door, corridorBeyond);
                }
                else
                {
                    AddEdgeToUnknownCorridor(door, connectorForDoor);
                }
            }
        }
        private void HandleCorridorDoor(DoorVarcoScript door)
        {
            string corridorId = expData.currentNodeId.Replace("_B", "");

            if (expData.allCorridorDoors.Contains(door.gameObject.name)) return;

            float dB = Vector3.Distance(door.transform.position, expData.poleBPos);
            float dA = Vector3.Distance(door.transform.position, expData.poleAPos);

            if (dB > 0.6f && dA > 0.6)
            { Debug.Log($"[ExplMgr] {door.gameObject.name} fuori corridoio (dB={dB:F2}) → ignorata"); return; }

            expData.registeredTransitDoors.Add(door.gameObject.name);
            expData.allCorridorDoors.Add(door.gameObject.name);

            graphConstruction.AddCorridorDoorEdges(corridorId, door, dA, dB);
        
            Debug.Log($"Porta polo-B → {corridorId}: {door.gameObject.name} ");
        
            graphConstruction.ComputeCorridorOrders(corridorId);
        }

        private void HandleConnectorDoor(DoorVarcoScript door)
        {
            var connectorForDoor = expData.FindConnectorForDoor(door.gameObject.name);

            // La porta è accettata se:
            //   a) appartiene fisicamente a questa stanza (roomFront/roomBack)
            //   b) oppure il nodo corrente È il connettore stesso (es. scalaSolaio vede Door_Solaio2)
            bool frontMatch     = door.roomFront != null && door.roomFront.name == expData.currentNodeId;
            bool backMatch      = door.roomBack  != null && door.roomBack.name  == expData.currentNodeId;
            bool connectorMatch = connectorForDoor != null && connectorForDoor.id == expData.currentNodeId;

            if (!frontMatch && !backMatch && !connectorMatch)
            {
                expData.TryRegisterRoomDoor(door);
                return;
            }

            // Connettore già attraversato: creo arco verso la stanza al di là vs Connettore NON ancora attraversato
            if (connectorForDoor != null && expData.traversedConnectors.Contains(connectorForDoor.id))
                HandleDoorVisibleInConnectorAlreadyTransited(door, connectorForDoor);
            else
                HandleDoorVisibleInConnector(door, connectorForDoor);
        }

        private void HandleNonConnectorDoor(DoorVarcoScript door)
        {
            expData.registeredRoomDoors[door.gameObject.name] = expData.currentNodeId;
            door.discovered = true;

            if (graphConstruction.IsNodeOfType(expData.currentNodeId, NodeType.CorridorPoleB))
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
            if (!string.IsNullOrEmpty(toNodeId) &&
                graphConstruction.IsNodePhysicallyVisited(toNodeId, out var targetNode) &&
                !targetNode.HasUndiscoveredEdges())
            {
                return;
            }
        
            graphConstruction.AddNewEdgeAndMarkExplored(door, toNodeId, false);

        }
    
        // --------------------------------------------------------
        // HANDLER: VisionCone vede una porta dentro una stanza
        // --------------------------------------------------------
        private void HandleDoorVisibleInRoom(DoorVarcoScript door)
        {
            if (!stateMachine.IsState(State.Rotating360) && 
                !stateMachine.IsState(State.InsideRoom)) return;
            if (!expData.inRoomMode) return;
            if (expData.registeredRoomDoors.TryGetValue(door.gameObject.name, out string registeredFrom)
                && registeredFrom == expData.currentNodeId) return;

            // FIX spam: porte non-connettore in un nodo connettore → ignora UNA VOLTA SOLA
            if (expData.traversedConnectors.Contains(expData.currentNodeId) && !expData.IsConnectorDoor(door.gameObject.name))
            {
                expData.TryRegisterRoomDoor(door);
                return;
            }

            // ── PORTE CONNETTORE (scale/ascensori) vs PORTA NORMALE ──
            if (expData.IsConnectorDoor(door.gameObject.name))
                HandleConnectorDoor(door);
            else
                HandleNonConnectorDoor(door);
        }

        // REFACTOR: EXTRACTED METHOD

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
                    graphConstruction.AddNodeToFromGraph(fromGraph, connectorId, connPosHere);
                }

            
            });
        
            if(stateMachine.IsState(State.TraversingStairs))
                StartCoroutine(expNav.TraverseStairsThen(targetFloor));
        }
        
        private void AddEdgeToUnknownCorridor(DoorVarcoScript door, VerticalConnector connectorForDoor)
        {
            // Apri fisicamente la porta
            ExplorationNavigation.OpenDoor(door);
        
            graphConstruction.AddConnectorNodeAndEdgeAndMarkExplored(connectorForDoor, door);
        }
        
        private string FindNearestRoomToDoor(DoorVarcoScript door, VerticalConnector connectorForDoor)
        {
            var fd = expData.buildingGraph?.GetFloor(expData.currentFloor);
            if (fd == null) return null;
        
            float minDist = float.MaxValue;
            string nearest = null;
            foreach (string roomId in fd.roomIds)
            {
                if (roomId == expData.currentNodeId || roomId == connectorForDoor.id) continue;
                if (!graphConstruction.IsNodeNull(roomId, out _)) continue; // già visitata
                var rObj = ExplorationUtils.FindRoom(roomId, fd);
                if (rObj == null) continue;
                float d = Vector3.Distance(door.transform.position, rObj.transform.position);
                if (d < minDist) { minDist = d; nearest = roomId; }
            }

            return nearest;
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
            var otherCorrDataCheck = ExplorationUtils.FindCorridorData(otherCorrId);
            if (otherCorrDataCheck == null) return null;
        
            if (graphConstruction.IsNodeNull($"{otherCorrId}_A", out _) &&
                graphConstruction.IsNodeNull($"{otherCorrId}_B", out _)) return null; // non ancora scoperto
            return otherCorrId;
        }

        private void TouchedFirstPole(CorridorData corridorData)
        {
            graphConstruction.AddCorridorPolesToGraph(corridorData);

            string comingFromNodeId = expData.currentNodeId;
            string poleAId = $"{expData.pendingCorridorId}_A";
            expData.currentNodeId = poleAId;
            graphConstruction.EnterNode(poleAId);

            if (!string.IsNullOrEmpty(comingFromNodeId) && 
                (comingFromNodeId.EndsWith("_B") || comingFromNodeId.EndsWith("_A")))
            {
                string comingCorridor = comingFromNodeId[..^2];

                var linkDoor = ExplorationUtils.FindDoorScript(expData.lastCorridorLinkDoorName);

                string f = linkDoor?.roomFront?.name ?? "";
                string b = linkDoor?.roomBack?.name  ?? "";
                bool directlyLinked = linkDoor != null &&
                                      (f == comingCorridor || b == comingCorridor) &&
                                      (f == expData.pendingCorridorId || b == expData.pendingCorridorId);

                if (directlyLinked)
                {
                    graphConstruction.AddSegmentToCorridor(comingCorridor, linkDoor.transform.position, poleAId, $"{expData.pendingCorridorId}_B");
                }
                expData.lastCorridorLinkDoorName = null;
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
                if (otherCorrId != null) graphConstruction.AddSegmentToCorridor(otherCorrId, door.transform.position, newCorrA, newCorrB);
           
            }

            expData.transitCorridorId = expData.pendingCorridorId;
            expData.registeredTransitDoors.Clear();
            expNav.BeginTransitingToB(expData.poleAPos, expData.poleBPos);
            stateMachine.SetState(State.Transiting);
        

            Debug.Log($"[ExplMgr] Transito {expData.pendingCorridorId}: A={expData.poleAPos} → B={expData.poleBPos}");
        }


        private void StartRotation360(bool isRoom)
        {
            expNav.StartRotation(isRoom);
            stateMachine.SetState(State.Rotating360);

            if (isRoom)
                expData.registeredRoomDoors.Clear();
            
            graphConstruction.SetRoomNodeAsPhysicallyVisited(isRoom);
        }

        private void DoBacktrack()
        {
            (string targetNode, GraphNode targetData) = graphConstruction.BacktrackingLoop();
        
            stateMachine.DoBacktrack(targetNode, targetData);
        }
    
        // ====================================================
        // DECISIONE PROSSIMA AZIONE
        // ====================================================

        private void DecideNextAction()
        {
            if (string.IsNullOrEmpty(expData.currentNodeId)) return;

            GraphEdge nextEdge = graphConstruction.RetrieveNextEdgeToInspect();

            if (nextEdge != null)
            {
                expNav.MoveToNextEdge(nextEdge);
                stateMachine.SetState(State.MovingToDoor);
                Debug.Log($"[ExplMgr] Prossima porta: {nextEdge.id} navDist={nextEdge.navMeshDist:F1}");
            }
            else
            {
                DoBacktrack();
            }
        }

        private bool HasCorridorBeenVisited(string newNodeId)
        {
            if (graphConstruction.IsCorridorFullyTransited(newNodeId))
            {
                Debug.LogWarning($"[ExplMgr] Corridoio '{newNodeId}' già transitato.");
                expData.currentEdge = null;
                DecideNextAction();
                return true;
            }
            // Controlla anche se corridoio già fisicamente visitato e senza porte pendenti
            if (graphConstruction.IsNodePhysicallyVisited($"{newNodeId}_B", out var targetCorridorNode)
                && !targetCorridorNode.HasUndiscoveredEdges())
            {
                Debug.LogWarning($"[ExplMgr] Corridoio '{newNodeId}' già completato, salto transito.");
                expData.currentEdge = null;
                DecideNextAction();
                return true;
            }
            return false;
        }


        private bool HasEdgeAlreadyBeenExplored(string newNodeId, bool isTargetCorridor, GraphEdge edge)
        {
            if (graphConstruction.IsNodePhysicallyVisited(newNodeId, out var existingNode)
                && !existingNode.HasUndiscoveredEdges() 
                && !isTargetCorridor)
            {
                Debug.LogWarning($"[ExplMgr] LOOP EVITATO: '{newNodeId}' già completata.");
                graphConstruction.MarkGraphEdgeExplored(edge, newNodeId);
                graphConstruction.SyncTwinCorridorEdge(edge.id, newNodeId);
                expData.currentEdge = null;
                DecideNextAction();
                return true;
            }

            graphConstruction.MarkGraphEdgeExplored(edge, isTargetCorridor ? null : newNodeId);
            graphConstruction.SyncTwinCorridorEdge(edge.id, newNodeId);

            if (isTargetCorridor)
            {
                if (graphConstruction.IsNodeNull(expData.currentNodeId, out var currentNode))
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
                var roomObj2   = ExplorationUtils.FindRoom(newNodeId, floorData2);
                Vector3 roomCenter = roomObj2 != null ? ExplorationUtils.GetRoomCenter(roomObj2) : edge.position;
                graphConstruction.AddRoomAndEnterNode(newNodeId, roomCenter);
                expData.currentNodeId = newNodeId;
                stateMachine.SetState(State.EnteringRoom);
            }

            return false;
        }
    
        // ====================================================
        // COMPLETAMENTO PIANO
        // ====================================================
        private void CheckFloorCompletion()
        {
            if (expData.buildingGraph == null) { OnAllFloorsCompleted(); return; }
        
            var floorData = expData.buildingGraph.GetFloor(expData.currentFloor);

            if (!graphConstruction.IsCurrentFloorFullyExplored(floorData))
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
            if (stateMachine.TransitionWithCondition(State.MovingToConnector,
                    () => expNav.FindAndExploreAdjacentFloors(unexplored))) return;
            
            // STEP 2 — nessun connettore diretto: cerca un piano intermedio già esplorato
            // che abbia un connettore verso un piano non ancora esplorato
            if (stateMachine.TransitionWithCondition(State.MovingToConnector,
                    () => expNav.FindAndExploreOtherFloors(unexplored))) return;

            Debug.Log("[ExplMgr] Tutti i piani esplorati (o non raggiungibili).");
            OnAllFloorsCompleted();
        }
    

        // ──────────────────────────────────────────────────────────────
        // METODO 2: IsCurrentFloorFullyExplored
        // ──────────────────────────────────────────────────────────────

        // REFACTOR: extracted methods

        private void NavigateToUnexploredCorridor(FloorNode floorData)
        {
            if (floorData != null)
            {
                foreach (string roomId in floorData.roomIds)
                {
                    var roomObj = ExplorationUtils.FindRoom(roomId, floorData);
                
                    if (graphConstruction.CheckForRoomExistenceAndStatus(roomId, roomObj)) continue;
                
                    // se è un corridoio, usa MovingToPole invece di EnteringRoom
                    var corridorData = ExplorationUtils.FindCorridorData(roomId);
                    if (corridorData != null)
                    {
                        expNav.BeginMovingToPole(roomId, corridorData);
                        stateMachine.SetState(State.MovingToPole);
                        return;
                    }

                    Vector3 center = ExplorationUtils.GetRoomCenter(roomObj);
                    graphConstruction.AddRoomAndEnterNode(roomId, center);
                    graphConstruction.AddEdgeWithPreviousRoom(roomId, center);

                    graphConstruction.UpdateEdgeId(roomId);
                    expNav.MoveToRoomCenter(roomId, center);
                    stateMachine.SetState(State.EnteringRoom);
                    return;
                }
            }

            string targetId = graphConstruction.FindNearestNodeWithDiscoveredEdges(transform.position);
            if (targetId == null) { OnAllFloorsCompleted(); return; }

            expNav.SetDestination(graphConstruction.GetNode(targetId).position);
            expData.currentNodeId = targetId;
            stateMachine.SetState(State.Backtracking);
        }
    
        //REFACTOR: methods extracted from ExecuteFloorChange 

        private string SetupConnectorAndNewFloor([NotNull] VerticalConnector activeConnector, int targetFloor)
        {
            int fromFloor = expData.currentFloor;
            bool isReverse = (activeConnector.floorFrom == targetFloor);

        
            expData.traversedConnectors.Add(activeConnector.id);
            (string connNodeId, Vector3 connPosCurrentFloor, Vector3 connPosDestFloor) = 
                graphConstruction.AddRoomNodesToConnectedFloors(activeConnector, isReverse, targetFloor);
            
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
    
        private void SetupAdjacentRoomAsRoom(string adjRoom, Vector3 adjPos, string connNodeId, string adjDoorName)
        {
            graphConstruction.AddRoomNodeAndEnterRoom(connNodeId, adjDoorName, adjRoom, adjPos);

            expNav.MoveToAdjacentRoomWithTimeout(adjPos);
            stateMachine.SetState(State.EnteringRoom);

            Debug.Log($"[ExplMgr] Cammino verso '{adjRoom}' via '{adjDoorName}'.");
        }

        private bool HandleNextFloorArrival(string connNodeId, int targetFloor)
        {
            var floorData = expData.buildingGraph?.GetFloor(targetFloor);
            if (floorData != null)
            {
                EnterInNextFloorSpawnNode(floorData, targetFloor, connNodeId);
                if (graphConstruction.IsCurrentFloorFullyExplored(floorData))
                {
                    CheckFloorCompletion();
                    return true;
                }
                if (!string.IsNullOrEmpty(connNodeId))
                {
                    CorridorData exitCorridor = ExplorationUtils.FindCorridorContainingPosition(
                        expNav.AgentPosition,
                        3f);
                    if (exitCorridor != null)
                    { 
                        expNav.BeginMovingToPole1(exitCorridor, true);
                        stateMachine.SetState(State.MovingToPole);
                        return true;  // ← salta Tentativo 1/2 e il blocco corridoio finale
                    }

                    (string adjRoom, string adjDoorName, Vector3 adjPos) = expData.FindAdjacentRoom(connNodeId, targetFloor);
                
                    expData.pendingArrivalDoor = null;
                
                    if (!string.IsNullOrEmpty(adjRoom))
                    {
                        if (stateMachine.TransitionWithCondition(State.MovingToPole,
                                () => expNav.IsAdjacentRoomACorridorWithNavigation(adjRoom))) return true;

                        // adjRoom è una stanza normale, gestione originale
                        SetupAdjacentRoomAsRoom(adjRoom, adjPos, connNodeId, adjDoorName);
                        return true;
                    }
                    // pre-collega le stanze già note del piano destinazione al nodo connettore
                    graphConstruction.AddEdgesOfKnownRoomsToConnectorNode(connNodeId, targetFloor);

                }
            }

            return false;
        }

        private void EnterInNextFloorSpawnNode(FloorNode floorData, int targetFloor, string connNodeId)
        {
            string nextFloorSpawnId =
                (floorData.spawnObject != null ? floorData.spawnObject.name : $"floor_{targetFloor}_entry");   
            string spawnId = !string.IsNullOrEmpty(connNodeId)
                ? connNodeId
                : nextFloorSpawnId;

            expData.currentNodeId = spawnId;
            graphConstruction.AddRoomAndEnterNode(expData.currentNodeId, floorData.spawnPosition);
        }
        
        public void EnterThroughEdge(GraphEdge edge)
        {
            var doorScript = ExplorationUtils.FindDoorScript(edge.id);

            string newNodeId = expData.FindNextNodeId(doorScript, edge);
        
            if (doorScript != null && expData.IsConnectorDoor(doorScript.gameObject.name))
            {
                graphConstruction.RegisterConnectorDoor(doorScript, edge, newNodeId);
                DecideNextAction();
                return;
            }
        
            bool isTargetCorridor = ExplorationUtils.FindCorridorData(newNodeId) != null;

            if(HasEdgeAlreadyBeenExplored(newNodeId, isTargetCorridor, edge)) return;

            expNav.MoveToNextTarget(isTargetCorridor, doorScript, edge, newNodeId);

            if (isTargetCorridor)
                expData.lastCorridorLinkDoorName = (edge.id.StartsWith("bw_") || edge.id.StartsWith("fw_"))
                    ? edge.id.Substring(3) : edge.id;
            expData.currentEdge = null;
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
        
            expData.registeredRoomDoors.Clear();
            expData.registeredTransitDoors.Clear();
            expData.allCorridorDoors.Clear();

            if (HandleNextFloorArrival(connNodeId, targetFloor)) return;

            expData.inRoomMode             = false;
            expData.movingToPoleCorridorId = null;
            expData.idleTargetCorridorId   = null;

            CorridorData spawnCorridor = ExplorationUtils.FindCorridorContainingPosition(
                expNav.AgentPosition,
                3f);
            if (spawnCorridor == null) { 
                StartRotation360(isRoom: true);
                return;
            }
            // Controlla se il corridoio è già stato completamente esplorato in questo piano
            var poleANode = graphConstruction.GetCorridorPoleNode($"{spawnCorridor.corridorId}_A");
            bool alreadyDone = GraphConstruction.HasCorridorAlreadyBeenExplored(poleANode, spawnCorridor);
        
            if (alreadyDone)
            {
                Debug.Log($"[ExplMgr] Corridoio '{spawnCorridor.corridorId}' già esplorato — skip.");
                StartRotation360(isRoom: true);
            }
            else
            {
                Debug.Log($"[ExplMgr] Spawn in corridoio '{spawnCorridor.corridorId}' — avvio modalità corridoio.");
                expNav.BeginMovingToPole1(spawnCorridor, false);
                stateMachine.SetState(State.MovingToPole);
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
                expData.floorGraphs, expData.ConnectorLinks, roomPairs, expData._roomArtifacts, expData.buildingId);

            snapshot.discoveredObjects.AddRange(expData._artifactData.Values);
            // ── 3. Salva su disco ────────────────────────────────────
            bool saved = GraphPersistence.Save(snapshot);
            if (!saved)
                Debug.LogWarning("[ExplMgr] Salvataggio snapshot fallito: " +
                                 "la trasmissione a JaCaMo procede comunque.");
        
            // ── 4. Salva JSON artefatti separato ─────────────────────
            ArtifactsPersistence.Save(expData.buildingId, expData._roomArtifacts, expData._artifactData);

            // ── 6. Trasmetti a JaCaMo ────────────────────────────────
            var bt = GetComponent<BeliefTransmitter>();
            bt?.TransmitSnapshot(snapshot);

            // ── 7. Aggiorna current_room col nodo reale di fine esplorazione ──
            var loc = GetComponent<LocalizationTest>();  
            loc?.ForceCurrentRoomAndSend(ExplorationUtils.NormalizeRoomId(expData.currentNodeId));
            Debug.Log($"[ExplMgr] Edificio '{expData.buildingId}' esplorato. " +
                      $"Piani: {expData.floorGraphs.Count}  Link: {expData.ConnectorLinks.Count}  " +
                      $"Distanze: {roomPairs.Count}  Snapshot salvato: {saved}");
        }
   
        // ====================================================
        // HELPER
        // ====================================================

        private void ScanCorridorDoorsFromScript(string corridorId)
        {
            foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
            {
                if (!ExplorationUtils.IsDoorConnectedToRoom(
                        door, 
                        corridorId, 
                        out var frontName, 
                        out var backName))
                    continue;

                if (expData.IsCorridorDoorAlreadyRegistered(door)) { continue; }
            
                graphConstruction.AddEdgesToCorridorDoor(corridorId, door, frontName, backName);

                expData.registeredTransitDoors.Add(door.gameObject.name);
                expData.allCorridorDoors.Add(door.gameObject.name);
            }
        }

        private void ScanRoomDoorsFromScript(string roomId)
        {
            foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
            {
                if (door.roomFront == null 
                    || door.roomBack == null 
                    || expData.IsConnectorDoor(door.gameObject.name)) continue;

                if(!ExplorationUtils.IsDoorConnectedToRoom(
                       door, 
                       roomId, 
                       out var frontName, 
                       out var backName)) 
                    continue;

                // Con il nuovo Dictionary: controlla se già registrata da QUESTA stanza
                if (expData.IsRoomDoorAlreadyRegistered(roomId, door)) continue;
                string roomBeyond = (frontName == roomId) ? backName : frontName;

                // ← NON aggiungere se la destinazione è già stata fisicamente visitata
                graphConstruction.AddEdgeBetweenRooms(roomId, roomBeyond, door);
            }
        }

        private void EndRotating360()
        {
            expData.rotationAccum = 0f;
            Debug.Log($"[ExplMgr] 360° completato in '{expData.currentNodeId}'.");

            if (expData.currentNodeId != null && expData.currentNodeId.EndsWith("_B"))
            {
                string corrId = expData.currentNodeId.Replace("_B", "");
                graphConstruction.ComputeCorridorOrders(corrId);
            }

            if (expData.inRoomMode && !string.IsNullOrEmpty(expData.currentNodeId))
            {
                bool hasUnexploredDoors = !graphConstruction.IsNodeNull(expData.currentNodeId, out var currentNode) &&
                                          currentNode.edges.Exists(e => e != null &&
                                                                        e.state != EdgeState.Explored &&
                                                                        e.edgeType == EdgeType.Door);
                if (!hasUnexploredDoors)
                    ScanRoomDoorsFromScript(expData.currentNodeId);
            }
        }
    }
}