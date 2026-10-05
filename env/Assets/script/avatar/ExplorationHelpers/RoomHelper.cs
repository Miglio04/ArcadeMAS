using System;
using System.Collections.Generic;
using script.avatar.ExplorationHelpers.Interfaces;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    public class RoomHelper : MonoBehaviour, IDoorLookup
    {
        private ExplorationData _expData;
        private GraphConstruction _graphConstruction;
        private ExplorationNavigation _expNav;
        private StateMachine _stateMachine;
        private void Awake()
        {
            _expData = GetComponent<ExplorationData>();
            _graphConstruction = GetComponent<GraphConstruction>();
            _expNav = GetComponent<ExplorationNavigation>();
            _stateMachine = GetComponent<StateMachine>();
        }

        public string FindDoorBetweenNodes(string nodeA, string nodeB)
        {
            string dName = null;
            foreach (var conn in _expData.buildingGraph?.connectors ?? new List<VerticalConnector>())
            {
                dName = ExplorationUtils.FindDoorBetweenNodesByConnector(conn, nodeA, nodeB) ?? dName;
            }

            if(dName != null) return dName;
            
            // DoorVarcoScript fallback
            foreach (var d in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
            {
                dName = ExplorationUtils.FindDoorBetweenNodesByDoor(d, nodeA, nodeB) ?? dName;
            }

            return dName;
        }
        
        private (string adjRoom, string adjDoorName, Vector3 adjPos) FindAdjacentRoomThroughPhysicalDoor(
            string connNodeId, int targetFloor)
        {
            string adjRoom = null;
            string adjDoorName = null;
            Vector3 adjPos = Vector3.zero;
            var arrDoor = _expData.pendingArrivalDoor;
            string front = arrDoor.roomFront?.name;
            string back = arrDoor.roomBack?.name;
            string fallbackCandidate = string.IsNullOrEmpty(front) ? back : front;
            string candidate = (back == connNodeId)
                ? front
                : fallbackCandidate;
            candidate = (front == connNodeId) ? back : candidate;

            var fdAdj = _expData.buildingGraph?.GetFloor(targetFloor);
            if (!string.IsNullOrEmpty(candidate) && candidate != connNodeId
                                                 && (fdAdj?.roomIds.Contains(candidate) ?? false))
            {
                adjRoom = candidate;
                adjDoorName = arrDoor.gameObject.name;
                var rObj = ExplorationUtils.FindRoom(adjRoom, fdAdj);
                adjPos = rObj != null ? ExplorationUtils.GetRoomCenter(rObj) : arrDoor.transform.position;
            }

            return (adjRoom, adjDoorName, adjPos);
        }

        private (string adjRoom, string adjDoorName, Vector3 adjPos) FindAdjacentRoomThroughRoomNodes(string connNodeId,
            int targetFloor)
        {
            string adjRoom = null;
            string adjDoorName = null;
            Vector3 adjPos = Vector3.zero;
            var fdAdj = _expData.buildingGraph?.GetFloor(targetFloor);
            if (fdAdj != null)
            {
                foreach (string rid in fdAdj.roomIds)
                {
                    string dName = FindDoorBetweenNodes(connNodeId, rid);
                    if (string.IsNullOrEmpty(dName)) continue;
                    var rObj = ExplorationUtils.FindRoom(rid, fdAdj);
                    if (!rObj) continue;
                    adjRoom = rid;
                    adjDoorName = dName;
                    adjPos = ExplorationUtils.GetRoomCenter(rObj);
                    break;
                }
            }

            return (adjRoom, adjDoorName, adjPos);
        }

        public (string adjRoom, string adjDoorName, Vector3 adjPos) FindAdjacentRoom(string connNodeId, int targetFloor)
        {
            string adjRoom = null;
            string adjDoorName = null;
            Vector3 adjPos = Vector3.zero;
            if (_expData.pendingArrivalDoor != null)
            {
                (adjRoom, adjDoorName, adjPos) = FindAdjacentRoomThroughPhysicalDoor(connNodeId, targetFloor);
            }

            if (string.IsNullOrEmpty(adjRoom))
            {
                (adjRoom, adjDoorName, adjPos) = FindAdjacentRoomThroughRoomNodes(connNodeId, targetFloor);
            }

            return (adjRoom, adjDoorName, adjPos);
        }
        
        public string FindNearestRoomToDoor(DoorVarcoScript door, VerticalConnector connectorForDoor)
        {
            var fd = _expData.buildingGraph?.GetFloor(_expData.currentFloor);
            if (fd == null) return null;
        
            float minDist = float.MaxValue;
            string nearest = null;
            foreach (string roomId in fd.roomIds)
            {
                if (roomId == _expData.currentNodeId || roomId == connectorForDoor.id) continue;
                if (!_graphConstruction.IsNodeNull(roomId, out _)) continue; // already visited
                var rObj = ExplorationUtils.FindRoom(roomId, fd);
                if (rObj == null) continue;
                float d = Vector3.Distance(door.transform.position, rObj.transform.position);
                if (d < minDist) { minDist = d; nearest = roomId; }
            }

            return nearest;
        }

        private bool IsRoomDoorAlreadyRegistered(string roomId, DoorVarcoScript door) =>
            _expData.registeredRoomDoors.TryGetValue(door.gameObject.name, out string from)
            && from == roomId;
        
        public void ScanRoomDoorsFromScript(string roomId, IConnectorQueries connectorQueries)
        {
            foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
            {
                if (door.roomFront == null 
                    || door.roomBack == null 
                    || connectorQueries.IsConnectorDoor(door.gameObject.name)) continue;

                if(!ExplorationUtils.IsDoorConnectedToRoom(
                       door, 
                       roomId, 
                       out var frontName, 
                       out var backName)) 
                    continue;
                
                if (IsRoomDoorAlreadyRegistered(roomId, door)) continue;
                string roomBeyond = (frontName == roomId) ? backName : frontName;
                
                _graphConstruction.AddEdgeBetweenRooms(roomId, roomBeyond, door);
            }
        }

        public void SetupAdjacentRoomAsRoom(string adjRoom, Vector3 adjPos, string connNodeId, string adjDoorName)
        {
            _graphConstruction.AddRoomNodeAndEnterRoom(connNodeId, adjDoorName, adjRoom, adjPos);

            _expNav.MoveToAdjacentRoomWithTimeout(adjPos);
            _stateMachine.SetState(State.EnteringRoom);

            Debug.Log($"[RoomHelper] Moving towards '{adjRoom}' through '{adjDoorName}'.");
        }
    }
}