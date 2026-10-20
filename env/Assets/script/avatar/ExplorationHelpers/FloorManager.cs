using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using script.avatar.ExplorationHelpers.Interfaces;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    public class FloorManager : MonoBehaviour, IConnectorQueries
    {
        private ExplorationData _expData;
        private GraphConstruction _graphConstruction;
        private ExplorationNavigation _expNav;
        private StateMachine _stateMachine;
        private CorridorManager _corridorManager;
        private RoomHelper _roomHelper;

        public static event Action OnAllFloorsCompleted;
        public static event Action OnNextActionNeeded;
        public static event Action<bool> OnNewFloorRoomOrExploredCorridor;
        
        
        void Awake()
        {
            _expData = GetComponent<ExplorationData>();
            _graphConstruction = GetComponent<GraphConstruction>();
            _expNav = GetComponent<ExplorationNavigation>();
            _stateMachine = GetComponent<StateMachine>();
            _corridorManager = GetComponent<CorridorManager>();
            _roomHelper = GetComponent<RoomHelper>();
        }

        public bool IsRoomBeyondConnectorInvalid(string connRoomBeyond, VerticalConnector connectorForDoor) =>
            string.IsNullOrEmpty(connRoomBeyond) || connRoomBeyond == _expData.currentNodeId
                                                 || connRoomBeyond == connectorForDoor.id;

        public VerticalConnector FindConnectorForDoor(string doorName)
        {
            if (_expData.buildingGraph == null) return null;
            foreach (var c in _expData.buildingGraph.connectors)
            {
                if (c.triggerStartObject?.name == doorName) return c;
                if (c.triggerEndObject?.name == doorName) return c;
            }
            return null;
        }

        public bool IsConnectorDoor(string dName)
        {
            if (_expData.buildingGraph == null) return false;
            foreach (var connector in _expData.buildingGraph.connectors)
            {
                if (connector.triggerStartObject?.name == dName) return true;
                if (connector.triggerEndObject?.name == dName) return true;
            }

            return false;
        }
        
        private string SetupConnectorAndNewFloor([NotNull] VerticalConnector activeConnector, int targetFloor)
        {
            int fromFloor = _expData.currentFloor;
            bool isReverse = (activeConnector.floorFrom == targetFloor);
        
            _expData.traversedConnectors.Add(activeConnector.id);
            (string connNodeId, Vector3 connPosCurrentFloor, Vector3 connPosDestFloor) = 
                AddRoomNodesToConnectedFloors(activeConnector, isReverse, targetFloor);
            
            bool alreadyExists = _expData.ConnectorLinks.Exists(l =>
                l.connectorId == activeConnector.id &&
                ((l.floorFrom == fromFloor   && l.floorTo == targetFloor) ||
                 (l.floorFrom == targetFloor && l.floorTo == fromFloor)));

            if (!alreadyExists)
            {
                _expData.ConnectorLinks.Add(new FloorConnectorLink
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

        private (string connNodeId, Vector3 connPosCurrentFloor, Vector3 connPosDestFloor)
            AddRoomNodesToConnectedFloors(VerticalConnector activeConnector, bool isReverse, int targetFloor)
        {
            (_, Vector3 connPosCurrentFloor) = _graphConstruction.AddConnectorRoomToGraph(activeConnector, isReverse);
            
            if (!_expData.floorGraphs.ContainsKey(targetFloor))
                _expData.floorGraphs[targetFloor] = new TopologicalGraph();

            var destGraph = _expData.floorGraphs[targetFloor];
            (_, Vector3 connPosDestFloor) = _graphConstruction.AddConnectorRoomToGraph(activeConnector, !isReverse, destGraph);
            
            return (activeConnector.id, connPosCurrentFloor, connPosDestFloor);
        }

        public void HandleConnectorReached(string connectorId, int targetFloor, Vector3 pos)
        {
            if (_expData.pendingConnector == null || _expData.pendingConnector.id != connectorId) return;

            _stateMachine.TryTransition(State.MovingToConnector, State.TraversingStairs, () =>
            {
                bool isReverse = (_expData.pendingConnector.floorFrom == targetFloor);
                Vector3 connPosHere = isReverse
                    ? _expData.pendingConnector.triggerEndPosition
                    : _expData.pendingConnector.triggerStartPosition;

                int fromFloor = isReverse ? _expData.pendingConnector.floorTo : _expData.pendingConnector.floorFrom;
                _expData.floorGraphs.TryGetValue(fromFloor, out var fromGraph);

                if (fromGraph != null && fromGraph.GetNode(connectorId) == null)
                {
                    _graphConstruction.AddRoomNodeToGraph(connectorId,
                        connPosHere, 
                        true, fromGraph);
                }

            
            });
        
            if(_stateMachine.IsState(State.TraversingStairs))
                StartCoroutine(_expNav.TraverseStairsThen(targetFloor));
        }

        private bool ArrivesInFloor(string connNodeId, int targetFloor)
        {
            var floorData = _expData.buildingGraph?.GetFloor(targetFloor);
            if (floorData != null)
            {
                EnterInNextFloorSpawnNode(floorData, targetFloor, connNodeId);
                if (_graphConstruction.IsCurrentFloorFullyExplored(floorData))
                {
                    CheckFloorCompletion();
                    return true;
                }
                if (!string.IsNullOrEmpty(connNodeId))
                {
                    if (_corridorManager.ArrivesInCorridor()) return true;

                    (string adjRoom, string adjDoorName, Vector3 adjPos) = 
                        _roomHelper.FindAdjacentRoom(connNodeId, targetFloor);
                
                    _expData.pendingArrivalDoor = null;
                
                    if (!string.IsNullOrEmpty(adjRoom))
                    {
                        if (_stateMachine.TransitionWithCondition(State.MovingToPole,
                                () => _expNav.IsAdjacentRoomACorridorWithNavigation(adjRoom))) return true;
                        
                        _roomHelper.SetupAdjacentRoomAsRoom(adjRoom, adjPos, connNodeId, adjDoorName);
                        return true;
                    }

                    _graphConstruction.AddEdgesOfKnownRoomsToConnectorNode(connNodeId,
                        targetFloor,
                        _roomHelper,
                        this);

                }
            }

            return false;
        }

        public void ExecuteFloorChange(int targetFloor)
        {
            if (_stateMachine.IsState(State.Completed)) return;

            var connNodeId = string.Empty;
            var activeConnector = _expData.pendingConnector;

            if (activeConnector != null)
            {
                connNodeId = SetupConnectorAndNewFloor(activeConnector, targetFloor);
            }
            else if (!_expData.floorGraphs.ContainsKey(targetFloor))
            {
                _expData.floorGraphs[targetFloor] = new TopologicalGraph();
            }
            
            _expData.currentFloor     = targetFloor;
            _expData.pendingConnector = null;
        
            _expData.registeredRoomDoors.Clear();
            _expData.registeredTransitDoors.Clear();
            _expData.allCorridorDoors.Clear();

            if (ArrivesInFloor(connNodeId, targetFloor)) return;

            _expData.inRoomMode             = false;
            _expData.movingToPoleCorridorId = null;
            _expData.idleTargetCorridorId   = null;

            CorridorData spawnCorridor = ExplorationUtils.FindCorridorContainingPosition(
                _expNav.AgentPosition,
                3f);
            if (!spawnCorridor) {
                OnNewFloorRoomOrExploredCorridor?.Invoke(true);
                return;
            }

            bool alreadyDone = _graphConstruction.GraphReader.HasCorridorAlreadyBeenExplored(spawnCorridor.corridorId);
        
            if (alreadyDone)
            {
                Debug.Log($"[FloorMgr] Corridor '{spawnCorridor.corridorId}' has already been explored — skip.");
                OnNewFloorRoomOrExploredCorridor?.Invoke(false);
            }
            else
            {
                Debug.Log($"[FloorMgr] Spawned in corridor '{spawnCorridor.corridorId}' — starting corridor mode.");
                _expNav.BeginMovingToFirstPole(spawnCorridor, false);
                _stateMachine.SetState(State.MovingToPole);
            }
        
        }
        

        public void CheckFloorCompletion()
        {
            if (_expData.buildingGraph == null) { OnAllFloorsCompleted?.Invoke(); return; }
        
            var floorData = _expData.buildingGraph.GetFloor(_expData.currentFloor);

            if (!_graphConstruction.IsCurrentFloorFullyExplored(floorData))
            {
                Debug.LogWarning("[FloorMgr] Current floor not explored completely!");
                NavigateToUnexploredUnit(floorData);
                return;
            }

            if (floorData != null) floorData.explored = true;
            Debug.Log($"[FloorMgr] Floor {_expData.currentFloor} completed.");

            var unexplored = _expData.buildingGraph.GetUnexploredFloors();
            if (unexplored.Count == 0)
            {
                Debug.Log("[FloorMgr] All floors explored.");
                OnAllFloorsCompleted?.Invoke();
                return;
            }
        
            // Looks for adjacent unexplored floors, if one of them exists, starts the exploration on that floor.
            if (_stateMachine.TransitionWithCondition(State.MovingToConnector,
                    () => FindAndExploreAdjacentFloors(unexplored))) return;
            
            // No direct connector -> look for a connector that connects another floor to the unexplored floor.
            if (_stateMachine.TransitionWithCondition(State.MovingToConnector,
                    () => FindAndExploreOtherFloors(unexplored))) return;

            Debug.Log("[FloorMgr] All floors explored (unless unreachable).");
            OnAllFloorsCompleted?.Invoke();
        }

        private bool FindAndExploreAdjacentFloors(List<FloorNode> unexplored)
        {
            unexplored.Sort((a, b) =>
                Mathf.Abs(a.floorIndex - _expData.currentFloor)
                    .CompareTo(Mathf.Abs(b.floorIndex - _expData.currentFloor)));

            foreach (var targetFloor in unexplored)
            {
                var connectors = _expData.buildingGraph.GetConnectors(
                    _expData.currentFloor, 
                    targetFloor.floorIndex
                );
                connectors.RemoveAll(c => _expData.traversedConnectors.Contains(c.id));
                if (connectors.Count == 0) continue;

                connectors.Sort((a, b) => a.costUp.CompareTo(b.costUp));
                _expData.pendingConnector = connectors[0];

                _expNav.MoveToNextFloor();
                Debug.Log($"[FloorMgr] Moving from floor {_expData.currentFloor} to floor {targetFloor.floorIndex} " +
                          $"through connector '{_expData.pendingConnector.id}'");
                return true;
            }

            return false;
        }

        private bool FindAndExploreOtherFloors(List<FloorNode> unexplored)
        {
            Debug.Log($"[FloorMgr] No connector connected from {_expData.currentFloor} to an unexplored floor. " +
                      $"Looking for an indirect path...");

            unexplored.Sort((a, b) => a.floorIndex.CompareTo(b.floorIndex));

            // Get already visited floors and order them by distance
            var visitedFloors = new List<int>(_expData.floorGraphs.Keys);
            visitedFloors.Sort((a, b) =>
                Mathf.Abs(a - _expData.currentFloor).CompareTo(Mathf.Abs(b - _expData.currentFloor)));

            foreach (var targetFloor in unexplored)
            {
                foreach (int midFloor in visitedFloors)
                {
                    if (midFloor == _expData.currentFloor) continue;

                    // The middle floor must have a non-traversed connector linked to the unexplored floor
                    var connToTarget = 
                        _expData.buildingGraph.GetConnectors(midFloor, targetFloor.floorIndex);
                    connToTarget.RemoveAll(c => _expData.traversedConnectors.Contains(c.id));
                    if (connToTarget.Count == 0) continue;

                    // Look for the connector between currentFloor and midFloor
                    var connToMid = 
                        _expData.buildingGraph.GetConnectors(_expData.currentFloor, midFloor);
                    if (connToMid.Count == 0) continue;

                    connToMid.Sort((a, b) => a.costUp.CompareTo(b.costUp));
                    _expData.pendingConnector = connToMid[0];

                    _expNav.MoveToNextFloor();

                    Debug.Log("[FloorMgr] Indirect path: " +
                              $"moving from floor {_expData.currentFloor} to floor {midFloor} " +
                              $" and floor {targetFloor.floorIndex} through '{_expData.pendingConnector.id}'.");
                    return true;
                }
            }

            return false;
        }
        
        private void NavigateToUnexploredUnit(FloorNode floorData)
        {
            if (floorData != null)
            {
                foreach (string roomId in floorData.roomIds)
                {
                    var roomObj = ExplorationUtils.FindRoom(roomId, floorData);
                
                    // Skip if the physical door towards this room has already been explored.
                    string doorId = _roomHelper.FindDoorBetweenNodes(_expData.currentNodeId, roomId);
                    if (_graphConstruction.CheckForRoomExistenceAndStatus(roomId, roomObj, doorId)) continue;
                
                    // If the target is a corridor, handle the movement differently and return
                    if (_corridorManager.IsMovingToCorridor(roomId)) return;

                    Vector3 center = ExplorationUtils.GetRoomCenter(roomObj);
                    _graphConstruction.AddRoomAndEnterNode(roomId, center);
                    _graphConstruction.AddEdgeWithPreviousRoom(roomId, center, _roomHelper);

                    _graphConstruction.UpdateEdgeId(roomId);
                    _expNav.MoveToRoomCenter(roomId, center);
                    _stateMachine.SetState(State.EnteringRoom);
                    return;
                }
            }

            string targetId = _graphConstruction.GraphReader.FindNearestNodeWithDiscoveredEdges(transform.position);
            if (targetId == null) { OnAllFloorsCompleted?.Invoke(); return; }

            _expNav.SetDestination(_graphConstruction.GraphReader.GetNodePosition(targetId));
            _expData.currentNodeId = targetId;
            _stateMachine.SetState(State.Backtracking);
        }

        private string FindNextNodeId(DoorVarcoScript doorScript, GraphEdge edge)
        {
            string newNodeId;
            if (doorScript && (edge.id.StartsWith("bw_") || edge.id.StartsWith("fw_")))
            {
                string corridorId = (_expData.currentNodeId.EndsWith("_A") || _expData.currentNodeId.EndsWith("_B"))
                    ? _expData.currentNodeId.Substring(0, _expData.currentNodeId.Length - 2)
                    : _expData.currentNodeId;
                string front = doorScript.roomFront?.name ?? "";
                string back = doorScript.roomBack?.name ?? "";
                if (front == corridorId) newNodeId = back;
                else if (back == corridorId) newNodeId = front;
                else newNodeId = doorScript.GetRoomNameBeyondDoor(transform.position);
            }
            else if (doorScript)
                newNodeId = doorScript.GetRoomNameBeyondDoor(transform.position);
            else
                newNodeId = edge.id;

            return newNodeId;
        }
        

        private void EnterInNextFloorSpawnNode(FloorNode floorData, int targetFloor, string connNodeId)
        {
            string nextFloorSpawnId =
                (floorData.spawnObject ? floorData.spawnObject.name : $"floor_{targetFloor}_entry");   
            string spawnId = !string.IsNullOrEmpty(connNodeId)
                ? connNodeId
                : nextFloorSpawnId;

            _expData.currentNodeId = spawnId;
            _graphConstruction.AddRoomAndEnterNode(_expData.currentNodeId, floorData.spawnPosition);
        }
        
        public void EnterThroughEdge(GraphEdge edge)
        {
            var doorScript = ExplorationUtils.FindDoorScript(edge.id);

            string newNodeId = FindNextNodeId(doorScript, edge);
        
            if (doorScript && IsConnectorDoor(doorScript.gameObject.name))
            {
                RegisterConnectorDoor(doorScript, edge, newNodeId);
                OnNextActionNeeded?.Invoke();
                return;
            }
        
            bool isTargetCorridor = ExplorationUtils.FindCorridorData(newNodeId) != null;

            if(HasEdgeAlreadyBeenExplored(newNodeId, isTargetCorridor, edge)) return;

            _expNav.MoveToNextTarget(isTargetCorridor, doorScript, edge, newNodeId);

            if (isTargetCorridor)
                _expData.lastCorridorLinkDoorName = (edge.id.StartsWith("bw_") || edge.id.StartsWith("fw_"))
                    ? edge.id.Substring(3) : edge.id;
            _expData.currentEdge = null;
        }
        
        private bool HasEdgeAlreadyBeenExplored(string newNodeId, bool isTargetCorridor, GraphEdge edge)
        {
            if (_graphConstruction.TryToMarkEdgeExplored(newNodeId, edge, isTargetCorridor))
            {
                Debug.LogWarning($"[FloorMgr] LOOP AVOIDED: '{newNodeId}' already fully explored.");
                _expData.currentEdge = null;
                OnNextActionNeeded?.Invoke();
                return true;
            }

            _graphConstruction.MarkGraphEdgeExplored(edge, isTargetCorridor ? null : newNodeId);
            _graphConstruction.SyncTwinCorridorEdge(edge.id, newNodeId);

            if (isTargetCorridor)
            {
                return _corridorManager.IsCorridorExploredAndSetIdle(edge, newNodeId);
            }
            var floorData2 = _expData.buildingGraph?.GetFloor(_expData.currentFloor);
            var roomObj2   = ExplorationUtils.FindRoom(newNodeId, floorData2);
            Vector3 roomCenter = roomObj2 ? ExplorationUtils.GetRoomCenter(roomObj2) : edge.position;
            _graphConstruction.AddRoomAndEnterNode(newNodeId, roomCenter);
            _expData.currentNodeId = newNodeId;
            _stateMachine.SetState(State.EnteringRoom);
            
            return false;
        }

        
        
        public void AddEdgeToConnectorDoor(DoorVarcoScript door, VerticalConnector connectorForDoor)
        {
            ExplorationNavigation.OpenDoor(door);
        
            _graphConstruction.AddConnectorAndMarkExplored(connectorForDoor, door);
        }
        
        public void RegisterDiscoveredObject(string artifactId, string roomId,
            string artifactType, int wsPort, float x, float y, float z)
        {
            _expData.artifactData[artifactId] = new RoomObjectSnapshot
            {
                artifactId   = artifactId,
                artifactType = artifactType,
                roomId       = roomId,
                wsPort       = wsPort,
                x = x, y = y, z = z,
            };

            // Remove object from previous room if its location changed.
            foreach (var kv in _expData.roomArtifacts)
                if (kv.Key != roomId)
                    kv.Value.Remove(artifactId);

            // Add node to current room
            if (!_expData.roomArtifacts.ContainsKey(roomId))
                _expData.roomArtifacts[roomId] = new HashSet<string>();
            _expData.roomArtifacts[roomId].Add(artifactId);
        }
        
        private void RegisterConnectorDoor(
            DoorVarcoScript doorScript, 
            GraphEdge edge, 
            string newNodeId)
        {
            var connector = FindConnectorForDoor(doorScript.gameObject.name);
            if (connector != null) newNodeId = connector.id;

            if (newNodeId == _expData.currentNodeId)
            {
                string beyond = doorScript.GetRoomNameBeyondDoor(transform.position);
                if (!string.IsNullOrEmpty(beyond) && beyond != _expData.currentNodeId)
                    newNodeId = beyond;
            }

            _graphConstruction.CreateNodeIfNotExists(newNodeId, edge);
            _graphConstruction.MarkGraphEdgeExplored(edge, newNodeId);
            _graphConstruction.SyncTwinCorridorEdge(edge.id, newNodeId);

            _expData.currentEdge = null;
        }
    }
}