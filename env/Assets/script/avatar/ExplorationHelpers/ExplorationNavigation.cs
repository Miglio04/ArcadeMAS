using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;
using UnityEngine.AI;

namespace script.avatar.ExplorationHelpers
{
    public class ExplorationNavigation : MonoBehaviour
    {
        private ExplorationData _expData;
        private NavMeshAgent _navAgent;
        
        public Vector3 AgentPosition => _navAgent.transform.position;
        public bool PathPending => _navAgent.pathPending;
        public bool ArrivedAtPole => _navAgent.remainingDistance<_expData.poleArrivalThreshold;
        public bool Arrived => IsWithin(_expData.arrivalThreshold) && HasPath;
        public bool ArrivedAtConnector => IsWithin(_expData.arrivalThreshold * 2f);
        public bool InsideInspectionDistance => IsWithin(_expData.inspectionDistance);
        public bool HasPath => _navAgent.hasPath;

        public void Stop() => _navAgent.isStopped = true;

        private bool IsWithin(float remainingDistance) => _navAgent.remainingDistance <= remainingDistance;

        private void Awake()
        {
            _expData = GetComponent<ExplorationData>();
            _navAgent = GetComponent<NavMeshAgent>();
        }
        
        public static event System.Action<int> OnStairsTraversed;
        public static event System.Action<string, string, Vector3> OnMovingToPole;

        public FloorNode PrepareExploration()
        {
            foreach (var floor in _expData.buildingGraph.floors)
                floor.explored = false;
    
            Vector3   agentPos  = _navAgent.transform.position;
            FloorNode floorData = null;
            float     minDist   = float.MaxValue;
    
            foreach (var floor in _expData.buildingGraph.floors)
            {
                foreach (var room in floor.roomObjects)
                {
                    if (room == null) continue;
                    float d = Vector3.Distance(agentPos, room.transform.position);
                    if (d < minDist) { minDist = d; floorData = floor; }
                }
            }
    
            if (floorData == null || floorData.spawnObject == null)
            {
                Debug.LogWarning("[ExplNav] Floor not found or missing spawnObject!");
                return null;
            }
    
            Vector3 startPos = floorData.spawnPosition;
            if (Vector3.Distance(agentPos, startPos) > 0.05f)
            {
                _navAgent.enabled = false;
                _navAgent.transform.position = startPos;
                _navAgent.enabled = true;
                _navAgent.Warp(startPos);
            }
            return floorData;
        }
        
        public void SetDestination(Vector3 destination) => _navAgent.SetDestination(destination);


        public void BacktrackToNode(Vector3 targetPos)
        {
            _navAgent.isStopped = false;
            SetDestination(targetPos);
        }

        public static void OpenDoor(DoorVarcoScript door)
        {
            if(!door.IsTraversable)
            {
                door.TryOpen();
            }
        }

        public void StartRotation(bool isRoom)
        {
            _expData.inRoomMode    = isRoom;
            _expData.rotationAccum = 0f;
            _navAgent.isStopped = true;
        }

        private Vector3 ComputeNavigationTarget(
            bool isTargetCorridor, 
            DoorVarcoScript doorScript, 
            GraphEdge edge, 
            string newNodeId)
        {
            Vector3 enterTarget;
            if (isTargetCorridor)
            {
                // Corridor: move through door
                Vector3 doorNormal = doorScript != null ? doorScript.transform.forward : transform.forward;
                Vector3 toDoor     = (edge.position - transform.position).normalized;
                if (Vector3.Dot(doorNormal, toDoor) < 0) doorNormal = -doorNormal;
                enterTarget = edge.position + doorNormal * _expData.enterOffset;
            }
            else
            {
                // Room: move towards the center of the room GameObject
                var floorData = _expData.buildingGraph?.GetFloor(_expData.currentFloor);
                var roomObj   = ExplorationUtils.FindRoom(newNodeId, floorData);
                if (roomObj != null)
                    enterTarget = ExplorationUtils.GetRoomCenter(roomObj);
                else
                {
                    // Fallback: offset behind the door
                    Vector3 doorNormal = doorScript != null ? doorScript.transform.forward : transform.forward;
                    Vector3 toDoor     = (edge.position - transform.position).normalized;
                    if (Vector3.Dot(doorNormal, toDoor) < 0) doorNormal = -doorNormal;
                    enterTarget = edge.position + doorNormal * _expData.enterOffset;
                }
            }
            return enterTarget;
        }
        
        
        public bool IsAdjacentRoomACorridorWithNavigation(string adjRoom)
        {
            var adjCorridor = ExplorationUtils.FindCorridorData(adjRoom);
            if (adjCorridor != null)
            {
                BeginMovingToPole1(adjRoom, adjCorridor, false);
                Debug.Log($"[ExplNav] The destination is corridor '{adjRoom}'." +
                          $"Moving to pole.");
                return true;
            }

            return false;
        }
            
        
        
        public void BeginMovingToPole(string roomId, CorridorData corridorData)
        {
            float d1 = Vector3.Distance(transform.position, corridorData.Pole1Position);
            float d2 = Vector3.Distance(transform.position, corridorData.Pole2Position);
            OnMovingToPole?.Invoke(roomId,
                d1 <= d2 ? "1" : "2",
                d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
            _expData.movingToPoleCorridorId = roomId;
            _expData.movingToPoleTimer      = 0f;
            _navAgent.isStopped     = false;
            SetDestination(d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
        }
        
        public void BeginMovingToPole(Vector3 startPos, string startNodeId, CorridorData corridorData)
        {
            float d1 = Vector3.Distance(startPos, corridorData.Pole1Position);
            float d2 = Vector3.Distance(startPos, corridorData.Pole2Position);
            OnMovingToPole?.Invoke(startNodeId,
                d1 <= d2 ? "1" : "2",
                d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
            _expData.movingToPoleCorridorId = startNodeId;
            _expData.movingToPoleTimer      = 0f;
            _navAgent.isStopped     = false;
            SetDestination(d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
        }
        
        public void BeginMovingToPole1(CorridorData corridor, bool nullDoor)
        {
            if(nullDoor) _expData.pendingArrivalDoor = null;
            OnMovingToPole?.Invoke(corridor.corridorId, "1",  corridor.Pole1Position);
            _expData.movingToPoleCorridorId = corridor.corridorId;
            _expData.movingToPoleTimer      = 0f;
            _navAgent.isStopped     = false;
            SetDestination(corridor.Pole1Position);
        }

        private void BeginMovingToPole1(string roomId, CorridorData corridor, bool nullDoor)
        {
            if(nullDoor) _expData.pendingArrivalDoor = null;
            OnMovingToPole?.Invoke(roomId, "1",  corridor.Pole1Position);
            _expData.movingToPoleCorridorId = roomId;
            _expData.movingToPoleTimer      = 0f;
            _navAgent.isStopped     = false;
            SetDestination(corridor.Pole1Position);
        }
        public void BeginTransitingToB(Vector3 poleAPos, Vector3 targetB)
        {
            _navAgent.isStopped = false;
            SetDestination(targetB);
        }
        
        public void MoveToNextEdge(GraphEdge nextEdge)
        {
            _expData.currentEdge = nextEdge;
            _navAgent.isStopped = false;
            SetDestination(nextEdge.position);
        }

        public void MoveToAdjacentRoomWithTimeout(Vector3 adjPos)
        {
            _expData.inRoomMode             = false;
            _expData.movingToPoleCorridorId = null;
            _expData.idleTargetCorridorId   = null;

            _navAgent.isStopped = false;
            SetDestination(adjPos);
            _expData.enteringRoomTimer   = 0f;
            _expData.enteringRoomTimeout = 6f;
        }

        public void MoveToRoomCenter(string roomId, Vector3 center)
        {
            _expData.currentNodeId = roomId;
            _navAgent.isStopped = false;
            SetDestination(center);
        }
        
        public IEnumerator TraverseStairsThen(int targetFloor) {
            var activeConnector = _expData.pendingConnector;

            // Open the first door
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

            // Wait for the NavMesh to physically move the agent to the other floor
            Vector3 destination = isNormalDir
                ? activeConnector.triggerEndPosition
                : activeConnector.triggerStartPosition;

            _navAgent.isStopped = false;
            SetDestination(destination);

            // Wait for the arrival of the agent
            yield return new WaitForSeconds(0.5f);
            while (PathPending || ArrivedAtConnector)
            {
                yield return null;
            }

            // Open arrival door
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
            
            OnStairsTraversed?.Invoke(targetFloor);
        }
        
        
        
        public bool FindAndExploreAdjacentFloors(List<FloorNode> unexplored)
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

                bool isReverse = (_expData.pendingConnector.floorFrom != _expData.currentFloor);
                Vector3 navTarget = isReverse
                    ? _expData.pendingConnector.triggerEndPosition
                    : _expData.pendingConnector.triggerStartPosition;

                Debug.Log($"[ExplNav] Moving from floor {_expData.currentFloor} to floor {targetFloor.floorIndex} " +
                          $"through connector '{_expData.pendingConnector.id}'");
                _navAgent.isStopped = false;
                SetDestination(navTarget);
                return true;
            }

            return false;
        }

        public bool FindAndExploreOtherFloors(List<FloorNode> unexplored)
        {
            Debug.Log($"[ExplNav] No connector connected from {_expData.currentFloor} to an unexplored floor. " +
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

                    bool isReverse = (_expData.pendingConnector.floorFrom != _expData.currentFloor);
                    Vector3 navTarget = isReverse
                        ? _expData.pendingConnector.triggerEndPosition
                        : _expData.pendingConnector.triggerStartPosition;

                    Debug.Log("[ExplNav] Indirect path: " +
                              $"moving from floor {_expData.currentFloor} to floor {midFloor} " +
                              $" and floor {targetFloor.floorIndex} through '{_expData.pendingConnector.id}'");
                    _navAgent.isStopped = false;
                    SetDestination(navTarget);
                    return true;
                }
            }

            return false;
        }


        public void MoveToNextTarget(bool isTargetCorridor, DoorVarcoScript doorScript, GraphEdge edge, string newNodeId)
        {
            Vector3 enterTarget = ComputeNavigationTarget(isTargetCorridor, doorScript, edge, newNodeId);

            if (!NavMesh.SamplePosition(enterTarget, out var hit, 2f, NavMesh.AllAreas))
            {
                enterTarget = edge.position;
                if (!NavMesh.SamplePosition(enterTarget, out hit, 2f, NavMesh.AllAreas))
                {
                    hit.position = edge.position;
                    Debug.LogWarning($"[ExplNav] NavMesh not found near '{newNodeId}'.");
                }
            }
            enterTarget = hit.position;

            _navAgent.isStopped = false;
            SetDestination(enterTarget);
        }
    }
}