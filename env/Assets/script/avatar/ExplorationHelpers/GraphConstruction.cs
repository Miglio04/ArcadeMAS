using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.AI;

namespace script.avatar.ExplorationHelpers
{
    public class GraphConstruction : MonoBehaviour
    {
        /// <summary>Grafo del piano corrente (proprietà calcolata).</summary>
        private TopologicalGraph graph
            => expData.floorGraphs.TryGetValue(expData.currentFloor, out var g) ? g : null;
        
        // Alias pubblico per retrocompatibilità
        public TopologicalGraph Graph => graph;
        
        private ExplorationData expData;


        public static event System.Action<string> OnCorridorDoorsScanNeeded;

        public static event System.Action<bool> OnRotationRequested;

        public static event System.Action OnBacktrackNodeNull;

        public static event System.Action OnCurrentNodeNull;

        void Awake()
        {
            expData = GetComponent<ExplorationData>();
        }
        
        private static (string connId2, Vector3 connectorPos) AddConnectorRoomToGraph(
            VerticalConnector connectorForDoor, 
            bool isRev3, 
            TopologicalGraph graph
            )
        {
            string connId2 = connectorForDoor?.id;
            if (!string.IsNullOrEmpty(connId2) && graph.GetNode(connId2) == null)
            {
                Vector3 connPos3 = isRev3
                    ? connectorForDoor.triggerEndPosition
                    : connectorForDoor.triggerStartPosition;
                graph.AddRoomNode(connId2, connPos3);
                return (connId2, connPos3);
            }

            return (null,  Vector3.zero);
        }

        public void AddRoomAndEnterNode(string nodeId, Vector3 nodePos)
        {
            graph.AddRoomNode(nodeId, nodePos);
            graph.EnterNode(nodeId);   
        }

        public void AddNewEdgeAndMarkExplored(DoorVarcoScript door, string toNodeId, bool physicallyVisited)
        {
            float navDist = door.NavMeshDistanceTo(transform.position);
            var edgeConn = graph.AddRoomDoorEdge(expData.currentNodeId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, navDist, toNodeId);
            if (edgeConn != null && physicallyVisited)
                graph.MarkEdgeExplored(expData.currentNodeId, edgeConn.id, toNodeId);
        }
        
        private void AddNewEdgeAndMarkExplored(string roomId, string adjDoorName, string adjRoom, Vector3 adjPos, 
            bool isPhysical)
        {
            float dAdj = Vector3.Distance(graph.GetNode(roomId).position, adjPos);
            var adjEdge = graph.AddRoomDoorEdge(roomId, adjDoorName,
                isPhysical, adjPos, dAdj, adjRoom);
            if (adjEdge != null)
                graph.MarkEdgeExplored(roomId, adjEdge.id, adjRoom);
        }

        public void AddConnectorNodeAndEdgeAndMarkExplored(VerticalConnector connectorForDoor, DoorVarcoScript door)
        {
            // Crea il nodo connettore nel grafo se non esiste ancora
            bool isRev3 = (connectorForDoor.floorFrom == expData.currentFloor);
            (string connId2, _) = AddConnectorRoomToGraph(connectorForDoor, isRev3, graph);
            

            // Arco currentNodeId→door→connettore, marcato explored (porta aperta = visitata)
            if (!string.IsNullOrEmpty(connId2))
            {
                AddNewEdgeAndMarkExplored(door, connId2, true);
            }

            Debug.Log(
                $"[ExplMgr] Scala '{door.gameObject.name}' aperta da '{expData.currentNodeId}' → '{connId2}' (non entro)");
        }

        public bool HandleAlreadyExploredPole(CorridorPoleNode poleANode, string corridorId, string poleId)
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
                    float distToA = Vector3.Distance(doorPos, graph.GetNode(poleAId)?.position ?? doorPos);
                    float distToB = Vector3.Distance(doorPos, graph.GetNode(poleBId)?.position ?? doorPos);
                    string nearestTo = distToA <= distToB ? poleAId : poleBId;
                    graph.AddSegmentArc(expData.currentNodeId, nearestTo,
                        Vector3.Distance(transform.position,
                            graph.GetNode(nearestTo)?.position ?? transform.position));
                }

                Debug.Log($"[ExplMgr] Polo {corridorId}_{poleId} ignorato — corridoio già transitato.");
                return true;
            }

            return false;
        }

        public (string connNodeId, Vector3 connPosCurrentFloor, Vector3 connPosDestFloor)
            AddRoomNodesToConnectedFloors(VerticalConnector activeConnector, bool isReverse, int targetFloor)
        {
            (_, Vector3 connPosCurrentFloor) = AddConnectorRoomToGraph(activeConnector, isReverse, graph);
            
            if (!expData.floorGraphs.ContainsKey(targetFloor))
                expData.floorGraphs[targetFloor] = new TopologicalGraph();

            var destGraph = expData.floorGraphs[targetFloor];
            (_, Vector3 connPosDestFloor) = AddConnectorRoomToGraph(activeConnector, !isReverse, destGraph);
            
            return (activeConnector.id, connPosCurrentFloor, connPosDestFloor);
        }
        
        public void AddRoomNodeAndMarkEdgeExplored(string targetId, DoorVarcoScript door)
        {
            // Aggiungi il nodo destinazione al grafo se non esiste ancora
            if (graph.GetNode(targetId) == null)
            {
                var floorData = expData.buildingGraph?.GetFloor(expData.currentFloor);
                var roomObj = ExplorationUtils.FindRoom(targetId, floorData);
                if (roomObj != null)
                    graph.AddRoomNode(targetId, roomObj.transform.position);
            }

            if (graph.GetNode(targetId) == null) return; // nodo non trovabile, skip

            var beyondNode = graph.GetNode(targetId);
            AddNewEdgeAndMarkExplored(door, targetId, beyondNode is { physicallyVisited: true });

            Debug.Log($"[ExplMgr] Connettore '{door.gameObject.name}' già attraversato → " +
                      $"RoomDoor {(beyondNode is { physicallyVisited: true } ? "explored" : "discovered")} verso '{targetId}'.");
        }

        public void AddRoomNodeAndEnterRoom(string connNodeId, string adjDoorName, string adjRoom, Vector3 adjPos)
        {
            if (graph.GetNode(adjRoom) == null)
                graph.AddRoomNode(adjRoom, adjPos);
            
            AddNewEdgeAndMarkExplored(connNodeId, adjDoorName, adjRoom, adjPos, false);

            graph.EnterNode(adjRoom);
            expData.currentNodeId = adjRoom;
        }

        public void AddCorridorDoorEdges(string corridorId, DoorVarcoScript door, float dA, float dB)
        {
            string computedSide = ExplorationUtils.ComputeSideRelativeToCorridorAxis(
                door.transform.position, expData.poleAPos, expData.poleBPos);

            graph.AddDoorEdges(corridorId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, computedSide, dA, dB);

            Debug.Log($"[ExplMgr] Porta censita → {corridorId}: {door.gameObject.name} " +
                      $"dA={dA:F1} dB={dB:F1} side={computedSide}");
        }

        public void AddNodeToFromGraph(TopologicalGraph fromGraph, string connectorId, Vector3 connPosHere)
        {
            fromGraph.AddRoomNode(connectorId, connPosHere);
            string sourceId = expData.currentNodeId;
            if (!string.IsNullOrEmpty(sourceId))
            {
                AddNewEdgeAndMarkExplored(sourceId, 
                    connectorId,
                    connectorId,
                    connPosHere,
                    true);
            }

            fromGraph.EnterNode(connectorId);
        }

        public void AddCorridorPolesToGraph(CorridorData corridorData)
        {
            expData.poleAPos = expData.pendingPolePos;
            Vector3 targetB = expData.pendingPoleId == "1"
                ? corridorData.Pole2Position
                : corridorData.Pole1Position;
            expData.poleBPos = targetB;

            graph.AddCorridorPoles(
                expData.pendingCorridorId, 
                expData.poleAPos, 
                expData.poleBPos,
                Vector3.Distance(expData.poleAPos, expData.poleBPos));
        }

        public void AddSegmentToCorridor(string corridorId, Vector3 doorPos, string poleAId, string poleBId)
        {
            var nodeA = graph.GetNode($"{corridorId}_A");
            var nodeB = graph.GetNode($"{corridorId}_B");

            string fromNode = null;
            float bestDist = float.MaxValue;
            if (nodeA != null)
            {
                float d = Vector3.Distance(doorPos, nodeA.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    fromNode = $"{corridorId}_A";
                }
            }

            if (nodeB != null)
            {
                float d = Vector3.Distance(doorPos, nodeB.position);
                if (d < bestDist)
                {
                    fromNode = $"{corridorId}_B";
                }
            }

            float distToNewA = Vector3.Distance(doorPos, expData.poleAPos);
            float distToNewB = Vector3.Distance(doorPos, expData.poleBPos);
            string toNode = distToNewA <= distToNewB ? poleAId : poleBId;

            Vector3 fromPos = graph.GetNode(fromNode)?.position ?? Vector3.zero;
            Vector3 toPos = graph.GetNode(toNode)?.position ?? Vector3.zero;
            float distFromDoor = ExplorationUtils.NavMeshDist(fromPos, doorPos);
            float distDoorTo = ExplorationUtils.NavMeshDist(doorPos, toPos);
            float dist = (distFromDoor >= 0f && distDoorTo >= 0f)
                ? distFromDoor + distDoorTo
                : Vector3.Distance(fromPos, toPos);

            graph.AddSegmentArc(fromNode, toNode, dist, expData.lastCorridorLinkDoorName, doorPos);
        }

        public void AddEdgesToCorridorDoor(string corridorId, DoorVarcoScript door, string frontName, string backName)
        {
            float dA = Vector3.Distance(door.transform.position, expData.poleAPos);
            float dB = Vector3.Distance(door.transform.position, expData.poleBPos);
            string otherSide = (frontName == corridorId) ? backName : frontName;
            string side =
                ExplorationUtils.ComputeSideRelativeToCorridorAxis(
                    door.transform.position, 
                    expData.poleAPos,
                    expData.poleBPos);

            if (door.isCorridorLink)
            {
                string poleBId = $"{corridorId}_B";
                float dist = Vector3.Distance(
                    graph.GetNode(poleBId)?.position ?? door.transform.position,
                    door.transform.position);
                var corrLinkEdge = graph.AddRoomDoorEdge(poleBId, door.gameObject.name,
                    false, door.transform.position, dist,
                    toNodeId: otherSide);

                // Marca subito esplorato se il corridoio destinazione è già visitato
                var targetCorrNode = graph.GetNode(otherSide) ?? graph.GetNode($"{otherSide}_A");
                bool targetAlreadyVisited = targetCorrNode is { physicallyVisited: true };
                if (corrLinkEdge != null && targetAlreadyVisited)
                    graph.MarkEdgeExplored(poleBId, corrLinkEdge.id, otherSide);
            }
            else
            {
                graph.AddDoorEdges(corridorId, door.gameObject.name,
                    door.elementType == DoorVarcoScript.ElementType.Door,
                    door.transform.position, side, dA, dB,
                    isCorridorLink: false);
            }
        }

        private bool AddEdgeToCorridorDoor(string roomId, string roomBeyond, DoorVarcoScript door)
        {
            bool leadsToCorridor = ExplorationUtils.FindCorridorData(roomBeyond) != null;
            if (leadsToCorridor)
            {
                // Se la stanza corrente ha poli propri, aggiungi arco dal polo B
                bool currentHasPoles = ExplorationUtils.FindCorridorData(roomId) != null;
                if (currentHasPoles)
                {
                    string poleBId = $"{roomId}_B";
                    float dist = door.NavMeshDistanceTo(transform.position);
                    graph.AddRoomDoorEdge(poleBId, door.gameObject.name,
                        door.elementType == DoorVarcoScript.ElementType.Door,
                        door.transform.position, dist,
                        toNodeId: roomBeyond);
                }

                return true;
            }

            return false;
        }

        public void AddEdgeToUnexploredCorridor(DoorVarcoScript door, string corridorBeyond)
        {
            NavMeshPath path = new NavMeshPath();
            float corridorNavDist = 0f;
            if (NavMesh.CalculatePath(transform.position, door.transform.position, NavMesh.AllAreas, path))
                for (int i = 1; i < path.corners.Length; i++)
                    corridorNavDist += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            graph.AddRoomDoorEdge(expData.currentNodeId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, corridorNavDist, corridorBeyond);
        }

        
        
        public void AddEdgesOfKnownRoomsToConnectorNode(string connNodeId, int targetFloor)
        {
            var fd2 = expData.buildingGraph?.GetFloor(targetFloor);
            if (fd2 != null)
            {
                foreach (string rid in fd2.roomIds)
                {
                    // cerca la porta fisica tra connettore e stanza
                    string dName = expData.FindDoorBetweenNodes(connNodeId, rid);
                    GameObject roomObj = FindRoomIfAttachedToConnectorThroughEdge(dName, connNodeId, rid, fd2);
                    if (roomObj == null) return;
                    Vector3 rCenter = ExplorationUtils.GetRoomCenter(roomObj);
                    float d = Vector3.Distance(graph.GetNode(connNodeId).position, rCenter);
                    graph.AddRoomDoorEdge(connNodeId, dName, false, rCenter, d, rid);
                }
            }
        }
        
        public void AddEdgeWithPreviousRoom(string roomId, Vector3 center)
        {
            var prevNode = graph.GetNode(expData.currentNodeId);
            if (prevNode != null)
            {
                bool alreadyLinked = prevNode.edges.Exists(e => e != null && e.toNodeId == roomId);
                if (!alreadyLinked)
                {
                    float dist = Vector3.Distance(prevNode.position, center);
                    // Trova la porta fisica più vicina al nodo connettore corrente
                    string synDoorName = expData.FindDoorBetweenNodes(expData.currentNodeId, roomId);
                    if (string.IsNullOrEmpty(synDoorName))
                        synDoorName = $"nav_{expData.currentNodeId}_{roomId}";

                    var synEdge = graph.AddRoomDoorEdge(expData.currentNodeId, synDoorName,
                        false, center, dist, roomId);
                    if (synEdge != null)
                        graph.MarkEdgeExplored(expData.currentNodeId, synEdge.id, roomId);
                }
            }
        }
        
        
        public bool IsCorridorFullyTransited(string nodeId) => graph.IsCorridorFullyTransited(nodeId);
        
        public bool IsNodeOfType(string nodeId, NodeType nodeType)
        {
            var node = graph.GetNode(nodeId);
            return node != null && node.type == nodeType;
        }

        public bool IsNodePhysicallyVisited(string nodeId, out GraphNode node)
        {
            node = graph.GetNode(nodeId);
            return node is { physicallyVisited: true };
        }

        public bool IsNodeNull(string nodeId, out GraphNode node)
        {
            node = graph.GetNode(nodeId);
            return node == null;
        }

        public bool IsCurrentFloorFullyExplored(FloorNode floorData)
        {
            // 1. Il grafo del piano corrente deve avere tutti gli archi esplorati
            if (!graph.IsFullyExplored())
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
                if (graph.GetNode(roomId) != null) continue;

                // ── FIX BUG 2: i corridoi nel grafo si chiamano {id}_A e {id}_B ──
                // Un corridoio nel BuildingGraph si chiama es. "corridoio1",
                // ma nel TopologicalGraph esiste come "corridoio1_A" e "corridoio1_B".
                // Se almeno il polo A è presente, il corridoio è stato attraversato.
                var poleANode = graph.GetCorridorPoleNode($"{roomId}_A");
                if (poleANode != null)
                {
                    var centralEdge = poleANode.edges?.Find(e => e?.id == $"central_{roomId}");
                    if (centralEdge is { state: EdgeState.Explored })
                        continue;

                    // FIX: considera completato anche se entrambi i poli sono fisicamente visitati
                    var poleBNode = graph.GetNode($"{roomId}_B");
                    if (poleANode.physicallyVisited && poleBNode is { physicallyVisited: true })
                    {
                        Debug.Log($"[ExplMgr] '{roomId}' — entrambi i poli visitati, considero completato.");
                        continue;
                    }

                    Debug.Log(
                        $"[ExplMgr] Piano {expData.currentFloor}: '{roomId}' ha polo ma transito non completato.");
                }

                Debug.LogWarning("[ExplMgr] Floor has unexplored Rooms.");
                return false;
            }

            return true;
        }

        private bool HasRoomAlreadyBeenExplored(string roomId)
        {
            // Skip se la porta fisica verso questa stanza è già stata esplorata (anche con nome nodo diverso)
            string doorCheck = expData.FindDoorBetweenNodes(expData.currentNodeId, roomId);
            if (!string.IsNullOrEmpty(doorCheck))
            {
                var currNode = graph.GetNode(expData.currentNodeId);
                if (currNode != null && currNode.edges.Exists(e =>
                        e?.id == doorCheck && e.state == EdgeState.Explored))
                    return true;
            }

            var poleANodeNav = graph.GetCorridorPoleNode($"{roomId}_A");
            var centralEdgeNav = poleANodeNav?.edges?.Find(e => e?.id == $"central_{roomId}");
            return centralEdgeNav is { state: EdgeState.Explored };
            // davvero già transitato
            // altrimenti cade giù e NavigateToUnexploredCorridor lo naviga normalmente
        }

        public static bool HasCorridorAlreadyBeenExplored(CorridorPoleNode poleANode, CorridorData spawnCorridor) =>
            poleANode != null &&
            poleANode.edges.Exists(e => e?.id == $"central_{spawnCorridor.corridorId}"
                                        && e.state == EdgeState.Explored);
        
        public void TouchedSecondPole()
        {
            expData.explorationVisionCone?.StopTransitScan();

            string poleBId = $"{expData.pendingCorridorId}_B";
            expData.currentNodeId = poleBId;
            graph.EnterNode(poleBId);

            OnCorridorDoorsScanNeeded?.Invoke(expData.pendingCorridorId);

            ComputeCorridorOrders(expData.pendingCorridorId);

            graph.MarkEdgeExplored($"{expData.pendingCorridorId}_A",
                $"central_{expData.pendingCorridorId}", poleBId);

            expData.movingToPoleCorridorId = null;
            Debug.Log($"[ExplMgr] Polo B toccato. Inizio ispezione {expData.pendingCorridorId}.");
            OnRotationRequested?.Invoke(false);
        }

        public void UpdateEdgeId(string roomId)
        {
            foreach (var node in graph.AllNodes())
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

        public void AddEdgeBetweenRooms(string roomId, string roomBeyond, DoorVarcoScript door)
        {
            var beyondNode = graph.GetNode(roomBeyond);
            if (beyondNode is { physicallyVisited: true }) return;

            if(AddEdgeToCorridorDoor(roomId, roomBeyond, door)) return;

            expData.registeredRoomDoors[door.gameObject.name] = roomId;
            door.discovered = true;

            float navDist = door.NavMeshDistanceTo(transform.position);
            
            graph.AddRoomDoorEdge(roomId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, navDist,
                toNodeId: null);
        }
        
        public void RegisterConnectorDoor(DoorVarcoScript doorScript, GraphEdge edge, string newNodeId)
        {
            var connector = expData.FindConnectorForDoor(doorScript.gameObject.name);
            if (connector != null) newNodeId = connector.id;

            if (newNodeId == expData.currentNodeId)
            {
                string beyond = doorScript.GetRoomNameBeyondDoor(transform.position);
                if (!string.IsNullOrEmpty(beyond) && beyond != expData.currentNodeId)
                    newNodeId = beyond;
            }

            CreateNodeIfNotExists(newNodeId, edge);
            MarkGraphEdgeExplored(edge, newNodeId);
            SyncTwinCorridorEdge(edge.id, newNodeId);

            expData.currentEdge = null;
        }

        
        public (string targetNode, GraphNode targetData) BacktrackingLoop()
        {
            string    targetNode;
            GraphNode targetData = null;

            while (true)
            {
                targetNode = graph.Backtrack();
                if (targetNode == null) { OnBacktrackNodeNull?.Invoke(); break; }

                targetData = graph.GetNode(targetNode);
                if (targetData != null && targetData.HasUndiscoveredEdges())
                {
                    if (targetNode.EndsWith("_A") || targetNode.EndsWith("_B"))
                    {
                        string corridorId = targetNode.Substring(0, targetNode.Length - 2);
                        var nodeA = graph.GetNode($"{corridorId}_A");
                        var nodeB = graph.GetNode($"{corridorId}_B");
                        bool corridorDone = nodeA is { physicallyVisited: true }
                                            && nodeB is { physicallyVisited: true }
                                            && graph.IsCorridorFullyTransited(corridorId);
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
        
        public void SyncTwinCorridorEdge(string edgeId, string toNodeId)
        {
            if (string.IsNullOrEmpty(expData.currentNodeId)) return;

            if (expData.currentNodeId.EndsWith("_B") && edgeId.StartsWith("bw_"))
            {
                string poleA  = expData.currentNodeId.Replace("_B", "_A");
                string fwEdge = edgeId.Replace("bw_", "fw_");
                graph.MarkEdgeExplored(poleA, fwEdge, toNodeId);
            }
            else if (expData.currentNodeId.EndsWith("_A") && edgeId.StartsWith("fw_"))
            {
                string poleB  = expData.currentNodeId.Replace("_A", "_B");
                string bwEdge = edgeId.Replace("fw_", "bw_");
                graph.MarkEdgeExplored(poleB, bwEdge, toNodeId);
            }
        }
        
        public void ComputeCorridorOrders(string corridorId)
        {
            graph.ComputeForwardOrders(corridorId);
            graph.ComputeBackwardOrders(corridorId);
            graph.ComputeExplorationOrder(corridorId);
        }

        public void SetRoomNodeAsPhysicallyVisited(bool isRoom)
        {
            if (isRoom && graph != null)
            {
                var visitNode = graph.GetNode(expData.currentNodeId);
                if (visitNode != null) visitNode.physicallyVisited = true;
            }
        }

        public GraphEdge RetrieveNextEdgeToInspect()
        {
            var node = graph.GetNode(expData.currentNodeId);
            if (node == null) { OnCurrentNodeNull?.Invoke(); return null; }

            GraphEdge nextEdge;

            if (node.type == NodeType.CorridorPoleB)
            {
                nextEdge = graph.NextCorridorEdgeToInspect(expData.currentNodeId) 
                           ?? graph.NextRoomEdgeToInspect(expData.currentNodeId);

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
                return graph.NextRoomEdgeToInspect(expData.currentNodeId);
            else
                return graph.NextEdgeToInspect(expData.currentNodeId);

            return nextEdge;
        }

        private void CreateNodeIfNotExists(string newNodeId, GraphEdge edge)
        {
            if (graph.GetNode(newNodeId) == null)
                graph.AddRoomNode(newNodeId, edge.position);
        }

        public void MarkGraphEdgeExplored(GraphEdge edge, string toNodeId)
        {
            graph.MarkEdgeExplored(expData.currentNodeId, edge.id, toNodeId);
        }
        
        public bool CheckForRoomExistenceAndStatus(string roomId, GameObject roomObj) => 
            graph.GetNode(roomId) != null ||
            HasRoomAlreadyBeenExplored(roomId) || roomObj == null;

        public void EnterNode(string nodeId) => graph.EnterNode(nodeId);

        public CorridorPoleNode GetCorridorPoleNode(string corridorId) => graph.GetCorridorPoleNode(corridorId);

        public GraphNode GetNode(string nodeId) => graph.GetNode(nodeId);
        
        [CanBeNull]
        private GameObject FindRoomIfAttachedToConnectorThroughEdge(string dName, 
            string connNodeId, 
            string rid, 
            FloorNode fd2)
        {
            // Checks both for door and edge existence
            if (string.IsNullOrEmpty(dName)) return null;
            bool isConnTrigger = expData.FindConnectorForDoor(dName) != null;
            if (isConnTrigger) return null;
            var existingEdge = GetNode(connNodeId)?.edges
                .Find(e => e?.toNodeId == rid || e?.id == dName);
            if (existingEdge != null) return null;
            var roomObj = ExplorationUtils.FindRoom(rid, fd2);
            return roomObj;
        }

        public string FindNearestNodeWithDiscoveredEdges(Vector3 transformPosition) => 
            graph.FindNearestNodeWithDiscoveredEdges(transformPosition);

        public string GetCurrentNodeId() => graph.CurrentNodeId;
    }
}