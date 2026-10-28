using System;
using System.Linq;
using JetBrains.Annotations;
using script.avatar.ExplorationHelpers.Interfaces;
using script.environment;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    public class GraphConstruction : MonoBehaviour
    {
        /// <summary>graph of the current floor.</summary>
        private TopologicalGraph Graph
            => _expData.floorGraphs.TryGetValue(_expData.currentFloor, out var g) ? g : null;

        public IGraphReader GraphReader => Graph;
        
        private ExplorationData _expData;

        public static event Action OnBacktrackNodeNull;

        public static event Action OnCurrentNodeNull;

        void Awake()
        {
            _expData = GetComponent<ExplorationData>();
        }

        public (string connId2, Vector3 connectorPos) AddConnectorRoomToGraph(
            VerticalConnector connectorForDoor, 
            bool isRev3, 
            TopologicalGraph graph = null
            )
        {
            graph ??= Graph;
            
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
            Graph.AddRoomNode(nodeId, nodePos);
            Graph.EnterNode(nodeId);   
        }

        public void AddNewEdgeAndMarkExplored(DoorVarcoScript door, string toNodeId, bool physicallyVisited)
        {
            float navDist = door.NavMeshDistanceTo(transform.position);
            var edgeConn = Graph.AddRoomDoorEdge(_expData.currentNodeId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, navDist, toNodeId);
            if (edgeConn != null && physicallyVisited)
                Graph.MarkEdgeExplored(_expData.currentNodeId, edgeConn.id, toNodeId);
        }
        
        public void AddNewEdgeAndMarkExplored(
            TopologicalGraph graph, 
            string roomId, 
            string adjDoorName, 
            string adjRoom, 
            Vector3 adjPos, 
            bool isPhysical)
        {
            graph ??= Graph;
            float dAdj = Vector3.Distance(graph.GetNode(roomId).position, adjPos);
            var adjEdge = graph.AddRoomDoorEdge(roomId, adjDoorName,
                isPhysical, adjPos, dAdj, adjRoom);
            if (adjEdge != null)
                graph.MarkEdgeExplored(roomId, adjEdge.id, adjRoom);
        }

        public void AddConnectorAndMarkExplored(VerticalConnector connectorForDoor, DoorVarcoScript door)
        {
            bool isRev3 = (connectorForDoor.floorFrom == _expData.currentFloor);
            (string connId2, _) = AddConnectorRoomToGraph(connectorForDoor, isRev3, Graph);
            
            if (!string.IsNullOrEmpty(connId2))
            {
                AddNewEdgeAndMarkExplored(door, connId2, true);
            }

            Debug.Log(
                $"[GraphConstruction] Marked corridor stairs from {_expData.currentNodeId} to {connId2} as explored.");
        }

        public bool TryToAddEdgeToExploredCorridor(string poleANodeName, string corridorId, string poleId)
        {
            var poleANode = Graph.GetCorridorPoleNode(poleANodeName);
            if (poleANode == null) return true;

            if (!Graph.HasCorridorAlreadyBeenExplored(corridorId)) return false;
            
            if (!string.IsNullOrEmpty(_expData.currentNodeId) &&
                (_expData.currentNodeId.EndsWith("_A") || _expData.currentNodeId.EndsWith("_B")))
            {
                string nearestTo = Graph.NearestPoleNode(corridorId, transform.position); 
                Graph.AddSegmentArc(_expData.currentNodeId, nearestTo,
                    Vector3.Distance(transform.position,
                        Graph.GetNode(nearestTo)?.position ?? transform.position));
            }

            Debug.Log($"[GraphConstruction] Pole {corridorId}_{poleId} ignored — corridor has already been transited.");
            return true;

        }
        
        public void AddRoomNodeAndMarkEdgeExplored(string targetId, DoorVarcoScript door)
        {
            // Add the destination node to the graph if it doesn't exist
            if (Graph.GetNode(targetId) == null)
            {
                var floorData = _expData.buildingGraph?.GetFloor(_expData.currentFloor);
                var roomObj = ExplorationUtils.FindRoom(targetId, floorData);
                if (roomObj != null)
                    Graph.AddRoomNode(targetId, roomObj.transform.position);
            }

            if (Graph.GetNode(targetId) == null) return; // can't add node, skip

            var beyondNode = Graph.GetNode(targetId);
            AddNewEdgeAndMarkExplored(door, targetId, beyondNode is { physicallyVisited: true });

            Debug.Log($"[GraphConstruction] Connector '{door.gameObject.name}' has already been crossed: " +
                      $"RoomDoor {(beyondNode is { physicallyVisited: true } ? "explored" : "discovered")}" +
                      $" towards '{targetId}'.");
        }
        
        public void AddRoomNodeToGraph( 
            string adjRoom,
            Vector3 pos, 
            bool isPhysical,
            TopologicalGraph graph = null,
            string sourceId = null,
            string adjDoorName = null
        )
        {
            graph ??= Graph;
            if (graph.GetNode(adjRoom) == null)
                graph.AddRoomNode(adjRoom, pos);
            sourceId ??= _expData.currentNodeId;
            adjDoorName ??= adjRoom;
            if (!string.IsNullOrEmpty(sourceId))
            {
                AddNewEdgeAndMarkExplored(graph,
                    sourceId, 
                    adjDoorName,
                    adjRoom,
                    pos,
                    isPhysical);
            }

            graph.EnterNode(adjRoom);
        }
        
        public void AddCorridorDoorEdges(string corridorId, DoorVarcoScript door, float dA, float dB)
        {
            string computedSide = ExplorationUtils.ComputeSideRelativeToCorridorAxis(
                door.transform.position, _expData.poleAPos, _expData.poleBPos);

            Graph.AddDoorEdges(corridorId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, computedSide, dA, dB);

            Debug.Log($"[GraphConstruction] Door registered {corridorId}: {door.gameObject.name} " +
                      $"dA={dA:F1} dB={dB:F1} side={computedSide}.");
        }

        public void AddCorridorPolesToGraph(CorridorData corridorData)
        {
            _expData.poleAPos = _expData.pendingPolePos;
            Vector3 targetB = _expData.pendingPoleId == "1"
                ? corridorData.Pole2Position
                : corridorData.Pole1Position;
            _expData.poleBPos = targetB;

            Graph.AddCorridorPoles(
                _expData.pendingCorridorId, 
                _expData.poleAPos, 
                _expData.poleBPos,
                Vector3.Distance(_expData.poleAPos, _expData.poleBPos));
        }
        
        

        public void AddSegmentToCorridor(string corridorId, Vector3 doorPos, string doorName, string poleAId, string poleBId)
        {
            string fromNode = Graph.NearestPoleNode(corridorId, doorPos);
            Debug.Assert(!string.IsNullOrEmpty(fromNode), 
                "[GraphConstruction] No pole found near door.");
            float distToNewA = Vector3.Distance(doorPos, _expData.poleAPos);
            float distToNewB = Vector3.Distance(doorPos, _expData.poleBPos);
            string toNode = distToNewA <= distToNewB ? poleAId : poleBId;

            Vector3 fromPos = Graph.GetNode(fromNode)?.position ?? Vector3.zero;
            Vector3 toPos = Graph.GetNode(toNode)?.position ?? Vector3.zero;
            float distFromDoor = ExplorationUtils.NavMeshDist(fromPos, doorPos);
            float distDoorTo = ExplorationUtils.NavMeshDist(doorPos, toPos);
            float dist = (distFromDoor >= 0f && distDoorTo >= 0f)
                ? distFromDoor + distDoorTo
                : Vector3.Distance(fromPos, toPos);

            Graph.AddSegmentArc(fromNode, toNode, dist, doorName, doorPos);
        }

        public void AddEdgesToCorridorDoor(string corridorId, DoorVarcoScript door, string frontName, string backName)
        {
            Debug.Log("[GraphConstruction] Adding edge: " + door.name+ ".");
            float dA = Vector3.Distance(door.transform.position, _expData.poleAPos);
            float dB = Vector3.Distance(door.transform.position, _expData.poleBPos);
            string otherSide = (frontName == corridorId) ? backName : frontName;

            if (door.isCorridorLink)
            {
                string poleBId = $"{corridorId}_B";
                float dist = Vector3.Distance(
                    Graph.GetNode(poleBId)?.position ?? door.transform.position,
                    door.transform.position);
                var corrLinkEdge = Graph.AddRoomDoorEdge(poleBId, door.gameObject.name,
                    false, door.transform.position, dist,
                    toNodeId: otherSide);

                // Mark explored if the destination corridor has already been visited
                var targetCorrNode = Graph.GetNode(otherSide) ?? Graph.GetNode($"{otherSide}_A");
                bool targetAlreadyVisited = targetCorrNode is { physicallyVisited: true };
                if (corrLinkEdge != null && targetAlreadyVisited)
                    Graph.MarkEdgeExplored(poleBId, corrLinkEdge.id, otherSide);
            }
            else
            {
                AddCorridorDoorEdges(corridorId, door, dA, dB);
            }
        }

        private bool AddEdgeToDoorBetweenCorridors(string roomId, string roomBeyond, DoorVarcoScript door)
        {
            bool leadsToCorridor = ExplorationUtils.FindCorridorData(roomBeyond) != null;
            if (leadsToCorridor)
            {
                // If the current room has some poles, ad an edge connected to pole B
                bool currentHasPoles = ExplorationUtils.FindCorridorData(roomId) != null;
                if (currentHasPoles)
                {
                    string poleBId = $"{roomId}_B";
                    float dist = door.NavMeshDistanceTo(transform.position);
                    Graph.AddRoomDoorEdge(poleBId, door.gameObject.name,
                        door.elementType == DoorVarcoScript.ElementType.Door,
                        door.transform.position, dist,
                        toNodeId: roomBeyond);
                }

                return true;
            }
            return false;
        }
        
        
        public void AddEdgesOfKnownRoomsToConnectorNode(
            string connNodeId,
            int targetFloor, 
            IDoorLookup doorLookup, 
            IConnectorQueries connectorQueries)
        {
            var fd2 = _expData.buildingGraph?.GetFloor(targetFloor);
            if (fd2 != null)
            {
                foreach (string rid in fd2.roomIds)
                {
                    // Look for the physical door between connector and door
                    string dName = doorLookup.FindDoorBetweenNodes(connNodeId, rid);
                    GameObject roomObj = FindRoomIfAttachedToConnectorThroughEdge(
                        dName, 
                        connNodeId, 
                        rid, 
                        fd2, 
                        connectorQueries);
                    if (roomObj == null) continue;
                    Vector3 rCenter = ExplorationUtils.GetRoomCenter(roomObj);
                    float d = Vector3.Distance(Graph.GetNode(connNodeId).position, rCenter);
                    Graph.AddRoomDoorEdge(connNodeId, dName, false, rCenter, d, rid);
                }
            }
        }
        
        public void AddEdgeWithPreviousRoom(string roomId, Vector3 center, IDoorLookup doorLookup)
        {
            var prevNode = Graph.GetNode(_expData.currentNodeId);
            if (prevNode != null)
            {
                bool alreadyLinked = prevNode.edges.Exists(e => e != null && e.toNodeId == roomId);
                if (!alreadyLinked)
                {
                    float dist = Vector3.Distance(prevNode.position, center);
                    // Find the nearest physical door to the current connector node
                    string synDoorName = doorLookup.FindDoorBetweenNodes(_expData.currentNodeId, roomId);
                    if (string.IsNullOrEmpty(synDoorName))
                        synDoorName = $"nav_{_expData.currentNodeId}_{roomId}";

                    var synEdge = Graph.AddRoomDoorEdge(_expData.currentNodeId, synDoorName,
                        false, center, dist, roomId);
                    if (synEdge != null)
                        Graph.MarkEdgeExplored(_expData.currentNodeId, synEdge.id, roomId);
                }
            }
        }
        
        public bool TryToMarkEdgeExplored(string nodeId, GraphEdge edge, bool isTargetCorridor)
        {
            if (GraphReader.IsNodePhysicallyVisited(nodeId)
                && !GraphReader.HasUnexploredEdges(nodeId)
                && !isTargetCorridor)
            {
                MarkGraphEdgeExplored(edge, nodeId);
                SyncTwinCorridorEdge(edge.id, nodeId);
                return true;
            }

            return false;
        }
        
        public void RemoveEdgesToNode(GraphEdge edge)
        {
            if (!GraphReader.IsNodeNull(_expData.currentNodeId))
            {
                GraphNode currentNode = Graph.GetNode(_expData.currentNodeId);
                currentNode.edges.RemoveAll(e =>
                    e is { edgeType: EdgeType.Door } &&
                    e.toNodeId == edge.toNodeId);
            }
        }
        
        public bool IsCurrentFloorFullyExplored(FloorNode floorData)
        {
            // 1. All current floor's edges must be explored
            if (!Graph.IsFullyExplored())
            {
                Debug.LogWarning("[GraphConstruction] Floor has unexplored edges.");
                return false;
            }

            // 2. All roomObjects of the BuildingGraph for this floor must be visited as nodes of the graph
            if (floorData == null || floorData.roomObjects.Count <= 0) return true;
            foreach (string roomId in floorData.roomIds)
            {
                // direct check: look for the gameObject's node via its name
                if (Graph.GetNode(roomId) != null) continue;

                
                // If the graph contains both corridor poles, then the corridor was explored
                var poleANode = Graph.GetCorridorPoleNode($"{roomId}_A");
                if (poleANode != null)
                {
                    if(Graph.HasCorridorAlreadyBeenExplored(roomId))
                        continue;

                    var poleBNode = Graph.GetNode($"{roomId}_B");
                    if (poleANode.physicallyVisited && poleBNode is { physicallyVisited: true })
                    {
                        Debug.Log($"[GraphConstruction] Both poles of '{roomId}' have been visited," +
                                  $" marking corridor as completed.");
                        continue;
                    }

                    Debug.Log($"[GraphConstruction] Floor {_expData.currentFloor}: " +
                              $"'{roomId}' has both poles, but hasn't been transited.");
                }

                Debug.LogWarning("[GraphConstruction] Floor has unexplored Rooms.");
                return false;
            }

            return true;
        }

        public void UpdateEdgeId(string roomId)
        {
            foreach (var node in Graph.AllNodes())
            {
                if (node == null) continue;
                foreach (var edge in node.edges)
                {
                    if (edge != null && edge.toNodeId == node.id && edge.state == EdgeState.Explored)
                    {
                        edge.toNodeId = roomId;
                        Debug.Log($"[GraphConstruction] Modified edge from {node.id} to {edge.id}:" +
                                  $" toNodeId updated to '{roomId}'.");
                    }
                }
            }
        }

        public void AddEdgeBetweenRooms(string roomId, string roomBeyond, DoorVarcoScript door)
        {
            var beyondNode = Graph.GetNode(roomBeyond);
            if (beyondNode is { physicallyVisited: true }) return;
            
            Debug.Log($"[GraphConstruction] Adding edge between {roomId} and {roomBeyond}.");

            if(AddEdgeToDoorBetweenCorridors(roomId, roomBeyond, door)) return;

            _expData.registeredRoomDoors[door.gameObject.name] = roomId;
            door.discovered = true;

            float navDist = door.NavMeshDistanceTo(transform.position);
            
            Graph.AddRoomDoorEdge(roomId, door.gameObject.name,
                door.elementType == DoorVarcoScript.ElementType.Door,
                door.transform.position, navDist,
                toNodeId: null);
        }
        
        public (string targetNode, GraphNode targetData) BacktrackingLoop()
        {
            string    targetNode;
            GraphNode targetData = null;

            while (true)
            {
                targetNode = Graph.Backtrack();
                if (targetNode == null) { OnBacktrackNodeNull?.Invoke(); break; }

                targetData = Graph.GetNode(targetNode);
                if (targetData != null && targetData.HasUnexploredEdges())
                {
                    if (targetNode.EndsWith("_A") || targetNode.EndsWith("_B"))
                    {
                        string corridorId = targetNode.Substring(0, targetNode.Length - 2);
                        var nodeA = Graph.GetNode($"{corridorId}_A");
                        var nodeB = Graph.GetNode($"{corridorId}_B");
                        bool corridorDone = nodeA is { physicallyVisited: true }
                                            && nodeB is { physicallyVisited: true }
                                            && Graph.IsCorridorFullyTransited(corridorId);
                        if (corridorDone)
                            continue;
                    }
                    break;
                }
                Debug.Log($"[Backtrack] {targetNode} already explored, rolling back...");
            }
            return (targetNode, targetData);
        }
        
        public void SyncTwinCorridorEdge(string edgeId, string toNodeId)
        {
            if (string.IsNullOrEmpty(_expData.currentNodeId)) return;

            if (_expData.currentNodeId.EndsWith("_B") && edgeId.StartsWith("bw_"))
            {
                Graph.MarkEdgeExplored(_expData.currentNodeId[..^2] + "_A",
                    "fw_" + edgeId[3..],
                    toNodeId);
            }
            else if (_expData.currentNodeId.EndsWith("_A") && edgeId.StartsWith("fw_"))
            {
                Graph.MarkEdgeExplored(_expData.currentNodeId[..^2] + "_B",
                    "bw_" + edgeId[3..],
                    toNodeId);
            }
        }
        
        public void ComputeCorridorOrders(string corridorId)
        {
            Graph.ComputeForwardOrders(corridorId);
            Graph.ComputeBackwardOrders(corridorId);
            Graph.ComputeExplorationOrder(corridorId);
        }

        public void SetRoomNodeAsPhysicallyVisited(bool isRoom)
        {
            if (isRoom && Graph != null)
            {
                var visitNode = Graph.GetNode(_expData.currentNodeId);
                if (visitNode != null) visitNode.physicallyVisited = true;
            }
        }

        public GraphEdge RetrieveNextEdgeToInspect()
        {

            var node = Graph.GetNode(_expData.currentNodeId);
            if (node == null) { OnCurrentNodeNull?.Invoke(); return null; }

            GraphEdge nextEdge;

            if (node.type == NodeType.CorridorPoleB)
            {

                nextEdge = Graph.NextCorridorEdgeToInspect(_expData.currentNodeId) 
                           ?? Graph.NextRoomEdgeToInspect(_expData.currentNodeId);

                if (nextEdge == null)
                {
                    nextEdge = node.edges.FirstOrDefault(e =>
                        e != null &&
                        e.state != EdgeState.Explored &&
                        e.isCorridorLink);

                    if (nextEdge != null)
                        Debug.Log(
                            $"[GraphConstruction] Found an unexplored corridor link: {nextEdge.id}.");
                        
                }
            }
            else if (node.type == NodeType.Room)
                return Graph.NextRoomEdgeToInspect(_expData.currentNodeId);
            else
                return Graph.NextEdgeToInspect(_expData.currentNodeId);

            return nextEdge;
        }

        public void CreateNodeIfNotExists(string newNodeId, GraphEdge edge)
        {
            if (Graph.GetNode(newNodeId) == null)
                Graph.AddRoomNode(newNodeId, edge.position);
        }

        public void MarkGraphEdgeExplored(GraphEdge edge, string toNodeId)
        {
            Graph.MarkEdgeExplored(_expData.currentNodeId, edge.id, toNodeId);
        }
        
        public bool CheckForRoomExistenceAndStatus(string roomId, GameObject roomObj, string doorId) => 
            !GraphReader.IsNodeNull(roomId) ||
            GraphReader.HasRoomAlreadyBeenExplored(_expData.currentNodeId, roomId, doorId) || !roomObj;

        public void EnterNode(string nodeId) => Graph.EnterNode(nodeId);
        
        
        [CanBeNull]
        private GameObject FindRoomIfAttachedToConnectorThroughEdge(string dName, 
            string connNodeId, 
            string rid, 
            FloorNode fd2,
            IConnectorQueries floorElements)
        {
            // Checks both for door and edge existence
            if (string.IsNullOrEmpty(dName)) return null;
            bool isConnTrigger = floorElements.FindConnectorForDoor(dName) != null;
            if (isConnTrigger) return null;
            var existingEdge = Graph.GetNode(connNodeId)?.edges
                .Find(e => e?.toNodeId == rid || e?.id == dName);
            if (existingEdge != null) return null;
            var roomObj = ExplorationUtils.FindRoom(rid, fd2);
            return roomObj;
        }



        public void EnterCorridorNodeAndMarkExplored(string poleBId)
        {
            Graph.EnterNode(poleBId);
            
            ComputeCorridorOrders(_expData.pendingCorridorId);

            Graph.MarkEdgeExplored($"{_expData.pendingCorridorId}_A",
                $"central_{_expData.pendingCorridorId}", poleBId);
        }
    }
}