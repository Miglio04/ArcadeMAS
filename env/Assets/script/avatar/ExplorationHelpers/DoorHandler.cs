using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    public class DoorHandler : MonoBehaviour
    {
        private ExplorationData _expData;
        private StateMachine _stateMachine;
        private GraphConstruction _graphConstruction;
        private RoomHelper _roomHelper;
        private CorridorManager _corridorManager;
        private FloorManager _floorManager;
        
        private void Awake()
        {
            _expData = GetComponent<ExplorationData>();
            _stateMachine = GetComponent<StateMachine>();
            _graphConstruction = GetComponent<GraphConstruction>();
            _roomHelper = GetComponent<RoomHelper>();
            _corridorManager = GetComponent<CorridorManager>();
            _floorManager = GetComponent<FloorManager>();
        }

        private bool TryRegisterRoomDoor(DoorVarcoScript door)
         => !_expData.registeredRoomDoors.TryAdd(door.gameObject.name, _expData.currentNodeId);

        public void HandleDoorDiscovered(DoorVarcoScript door, string corridorId, int orderFW)
        {
            Debug.Log("[DoorHandler] Door Discovered: " + door.gameObject.name);
            if (!_stateMachine.IsState(State.Transiting)) return;
            if (corridorId != _expData.transitCorridorId) return;
            if (_expData.registeredTransitDoors.Contains(door.gameObject.name)) return;
            if (_expData.allCorridorDoors.Contains(door.gameObject.name)) return;

            if(_corridorManager.IsDoorInsideCorridor(door)) return;
        
            string frontName = door.roomFront?.name ?? "";
            string backName  = door.roomBack?.name  ?? "";
            if (frontName != corridorId && backName != corridorId) return;
            bool isCorrLink = door.isCorridorLink;

            _expData.registeredTransitDoors.Add(door.gameObject.name);
            if (!isCorrLink)
            {
                _expData.allCorridorDoors.Add(door.gameObject.name);
                _graphConstruction.AddCorridorDoorEdges(corridorId, door, door.distFromA, door.distFromB);
            }
            
            Debug.Log($"[DoorHandler] Door registered: {door.gameObject.name} " +
                      $"corridorLink={isCorrLink}");
        }

        private void HandleDoorVisibleInConnectorAlreadyTraversed(
            DoorVarcoScript door, 
            VerticalConnector connectorForDoor)
        {
            if (TryRegisterRoomDoor(door)) return;
            if (!_expData.traversedConnectors.Contains(_expData.currentNodeId))
                return;
            
            string connRoomBeyond = door.GetRoomNameBeyondDoor(transform.position);

            // FIX self-loop: if GetRoomNameBeyondDoor returns the incorrect value,
            // which happens if door.roomBack or door.roomFront are not set correctly,
            // try to find the correct name for the room behind the door.
            if (_floorManager.IsRoomBeyondConnectorInvalid(connRoomBeyond, connectorForDoor))
            {
                connRoomBeyond = _roomHelper.FindNearestRoomToDoor(door, connectorForDoor);
            }

            // leave if the destination is still incorrect.
            if (_floorManager.IsRoomBeyondConnectorInvalid(connRoomBeyond, connectorForDoor))
                return;

            string targetId = connRoomBeyond;

            _graphConstruction.AddRoomNodeAndMarkEdgeExplored(targetId, door);
        }

        private void HandleDoorVisibleInConnector(DoorVarcoScript door, VerticalConnector connectorForDoor)
        {
            string corridorBeyond = door.GetRoomNameBeyondDoor(transform.position);
            if (!TryRegisterRoomDoor(door))
            {
                if (!string.IsNullOrEmpty(corridorBeyond) 
                    && ExplorationUtils.FindCorridorData(corridorBeyond) != null)
                {
                    _graphConstruction.AddNewEdgeAndMarkExplored(door, corridorBeyond, false);
                }
                else
                {
                    _floorManager.AddEdgeToConnectorDoor(door, connectorForDoor);
                }
            }
        }
        

        private void HandleConnectorDoor(DoorVarcoScript door)
        {
            var connectorForDoor = _floorManager.FindConnectorForDoor(door.gameObject.name);
            
            // If:
            // - the door belongs to this room
            // - the current node is the connector itself
            // the door is accepted.
            bool frontMatch     = door.roomFront && door.roomFront.name == _expData.currentNodeId;
            bool backMatch      = door.roomBack && door.roomBack.name  == _expData.currentNodeId;
            bool connectorMatch = connectorForDoor != null && connectorForDoor.id == _expData.currentNodeId;

            if (!frontMatch && !backMatch && !connectorMatch)
            {
                TryRegisterRoomDoor(door);
                return;
            }

            if (connectorForDoor != null && _expData.traversedConnectors.Contains(connectorForDoor.id))
                HandleDoorVisibleInConnectorAlreadyTraversed(door, connectorForDoor);
            else
                HandleDoorVisibleInConnector(door, connectorForDoor);
        }
        
        private void HandleCorridorDoor(DoorVarcoScript door)
        {
            string corridorId = _expData.currentNodeId.Replace("_B", "");

            if (_expData.allCorridorDoors.Contains(door.gameObject.name)) return;

            float dB = Vector3.Distance(door.transform.position, _expData.poleBPos);
            float dA = Vector3.Distance(door.transform.position, _expData.poleAPos);

            if (dB > 0.6f && dA > 0.6)
            { Debug.Log($"[DoorHandler] {door.gameObject.name} door outside corridor (dB={dB:F2}) and ignored."); return; }

            _expData.registeredTransitDoors.Add(door.gameObject.name);
            _expData.allCorridorDoors.Add(door.gameObject.name);

            _graphConstruction.AddCorridorDoorEdges(corridorId, door, dA, dB);
        
            Debug.Log($"[DoorHandler] Pole-B Door {corridorId}: {door.gameObject.name} ");
        
            _graphConstruction.ComputeCorridorOrders(corridorId);
        }
        
        private void HandleNonConnectorDoor(DoorVarcoScript door)
        {
            Debug.Log("[DoorHandler] Handling non Connector Door.");
            _expData.registeredRoomDoors[door.gameObject.name] = _expData.currentNodeId;
            door.discovered = true;

            if (_graphConstruction.GraphReader.IsNodeOfType(_expData.currentNodeId, NodeType.CorridorPoleB))
            {
                Debug.Log("[DoorHandler] Node is second pole of corridor.");
                HandleCorridorDoor(door);
                return;
            }

            // skip doors not in this room
            string frontName      = door.roomFront?.name;
            string backName       = door.roomBack?.name;
            bool   doorBelongsHere = (frontName == _expData.currentNodeId || backName == _expData.currentNodeId);
            if (!doorBelongsHere)
            {
                return;
            }

            string roomBeyond = door.GetRoomNameBeyondDoor(transform.position);
            bool leadsToCorridor = !string.IsNullOrEmpty(roomBeyond)
                                   && roomBeyond.ToLower().Contains("corridoio");

            string toNodeId = leadsToCorridor ? roomBeyond : null;
            if (!string.IsNullOrEmpty(toNodeId) &&
                _graphConstruction.GraphReader.IsNodePhysicallyVisited(toNodeId) &&
                !_graphConstruction.GraphReader.HasUnexploredEdges(toNodeId))
            {
                return;
            }
        
            _graphConstruction.AddNewEdgeAndMarkExplored(door, toNodeId, false);

        }
        
        public void HandleDoorVisibleInRoom(DoorVarcoScript door)
        {
            if (!_stateMachine.IsState(State.Rotating360) && 
                !_stateMachine.IsState(State.InsideRoom)) return;
            if (!_expData.inRoomMode) return;
            if (_expData.registeredRoomDoors.TryGetValue(door.gameObject.name, out string registeredFrom)
                && registeredFrom == _expData.currentNodeId) return;

            // ignore non-connector doors in connector node once.
            if (_expData.traversedConnectors.Contains(_expData.currentNodeId) 
                && !_floorManager.IsConnectorDoor(door.gameObject.name))
            {
                TryRegisterRoomDoor(door);
                return;
            }
            
            if (_floorManager.IsConnectorDoor(door.gameObject.name))
                HandleConnectorDoor(door);
            else
                HandleNonConnectorDoor(door);
        }
    }
}