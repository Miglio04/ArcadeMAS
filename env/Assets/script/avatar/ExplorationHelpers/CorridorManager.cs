using System;
using JetBrains.Annotations;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    public class CorridorManager : MonoBehaviour
    {
        private ExplorationData _expData;
        private GraphConstruction _graphConstruction;
        private StateMachine _stateMachine;
        private ExplorationNavigation _expNav;
        

        public static event Action<bool> OnRotationRequested;

        public static event Action OnNextActionNeeded;

        void Awake()
        {
            _expData = GetComponent<ExplorationData>();
            _graphConstruction = GetComponent<GraphConstruction>();
            _stateMachine = GetComponent<StateMachine>();
            _expNav = GetComponent<ExplorationNavigation>();
        }
        public void SetPendingCorridorAndPole(string corridorId, string poleId, Vector3 polePos)
        {
            _expData.pendingCorridorId = corridorId;
            _expData.pendingPoleId = poleId;
            _expData.pendingPolePos = polePos;
        }
        
        [CanBeNull]
        private string GetOtherCorridorIdIfDoorMatchesCorridorCriteria(DoorVarcoScript door)
        {
            if (!door.isCorridorLink) return null;
            string front = door.roomFront?.name ?? "";
            string back  = door.roomBack?.name  ?? "";

            // The door must be accessible from the new corridor
            bool newIsA = (front == _expData.pendingCorridorId || back == _expData.pendingCorridorId);
            if (!newIsA) return null;

            // The other side must be a corridor already discovered
            string otherCorrId = (front == _expData.pendingCorridorId) ? back : front;
            var otherCorrDataCheck = ExplorationUtils.FindCorridorData(otherCorrId);
            if (!otherCorrDataCheck) return null;
        
            if (_graphConstruction.IsNodeNull($"{otherCorrId}_A", out _) &&
                _graphConstruction.IsNodeNull($"{otherCorrId}_B", out _)) return null; // not discovered yet
            return otherCorrId;
        }

        public void PoleTouchedWhileIdle(string corridorId, string poleId, Vector3 polePos)
        {
            if (string.IsNullOrEmpty(_expData.idleTargetCorridorId)) return;
            // If the target of the movement is a defined corridor, don't stop when touching another pole
            if (corridorId != _expData.idleTargetCorridorId)
            {
                ExplorationUtils.ResetCorridorPoles(corridorId);
                return;
            }
            
            var poleANode = _graphConstruction.GetCorridorPoleNode($"{corridorId}_A");
            // Even if the corridor has been explored, some doors might not be
            if (poleANode != null 
                && !_graphConstruction.HandlePoleInAlreadyExploredCorridor(poleANode, corridorId, poleId))
                return;
            // Clean target if the intended corridor gets reached
            _expData.idleTargetCorridorId = null;
            SetPendingCorridorAndPole(corridorId, poleId, polePos);
            ProcessPoleTouched();
        }

        public void PoleTouchedWhileTransiting(string corridorId, string poleId, Vector3 polePos)
        {
            float distToB = Vector3.Distance(transform.position, _expData.poleBPos);
            float distToA = Vector3.Distance(transform.position, _expData.poleAPos);
            if (distToA < distToB && distToB > _expData.poleArrivalThreshold)
            {
                return;
            }
            SetPendingCorridorAndPole(corridorId, poleId, polePos);
            ProcessPoleTouched();
        }

        public void ProcessPoleTouched()
        {
            var corridorData = ExplorationUtils.FindCorridorData(_expData.pendingCorridorId);
            if (corridorData == null) return;
        
            if (!_stateMachine.IsState(State.Transiting))
            {
                TouchedFirstPole(corridorData);
            }
            else
            {
                TouchedSecondPole();
            }

            _expData.pendingCorridorId = null;
            _expData.pendingPoleId     = null;
        }
        
        private void TouchedFirstPole(CorridorData corridorData)
        {
            Debug.Log("[CorrMgr] First pole touched.");
            _graphConstruction.AddCorridorPolesToGraph(corridorData);

            string comingFromNodeId = _expData.currentNodeId;
            string poleAId = $"{_expData.pendingCorridorId}_A";
            _expData.currentNodeId = poleAId;
            _graphConstruction.EnterNode(poleAId);

            if (!string.IsNullOrEmpty(comingFromNodeId) && 
                (comingFromNodeId.EndsWith("_B") || comingFromNodeId.EndsWith("_A")))
            {
                string comingCorridor = comingFromNodeId[..^2];
                
                var linkDoor = ExplorationUtils.FindDoorScript(_expData.lastCorridorLinkDoorName);
                

                string f = linkDoor?.roomFront?.name ?? "";
                string b = linkDoor?.roomBack?.name  ?? "";
                bool directlyLinked = linkDoor != null &&
                                      (f == comingCorridor || b == comingCorridor) &&
                                      (f == _expData.pendingCorridorId || b == _expData.pendingCorridorId);

                if (directlyLinked)
                {
                    _graphConstruction.AddSegmentToCorridor(
                        comingCorridor,
                        linkDoor.transform.position,
                        poleAId,
                        $"{_expData.pendingCorridorId}_B");
                }
                _expData.lastCorridorLinkDoorName = null;
            }
            // After creating the new corridor's poles,
            // start looking for corridorLinks already registered
            // that connect an already discovered corridor to this new corridor.
            string newCorrA = $"{_expData.pendingCorridorId}_A";
            string newCorrB = $"{_expData.pendingCorridorId}_B";
            foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
            { 
                string otherCorrId = GetOtherCorridorIdIfDoorMatchesCorridorCriteria(door);

                if (otherCorrId != null) _graphConstruction.AddSegmentToCorridor(
                    otherCorrId, 
                    door.transform.position, 
                    newCorrA, 
                    newCorrB);
           
            }

            _expData.transitCorridorId = _expData.pendingCorridorId;
            _expData.registeredTransitDoors.Clear();
            _expNav.BeginTransitingToB(_expData.poleAPos, _expData.poleBPos);
            _expData.explorationVisionCone?.StartTransitScan(
                _expData.poleAPos, 
                _expData.poleBPos,
                _expData.pendingCorridorId);
            _stateMachine.SetState(State.Transiting);
        

            Debug.Log(
                $"[CorrMgr] Transiting {_expData.pendingCorridorId}: A={_expData.poleAPos},  B={_expData.poleBPos}");
        }

        private void TouchedSecondPole()
        {
            _expData.explorationVisionCone?.StopTransitScan();

            string poleBId = $"{_expData.pendingCorridorId}_B";
            _expData.currentNodeId = poleBId;
            
            ScanCorridorDoorsFromScript(_expData.pendingCorridorId);

            _graphConstruction.EnterCorridorNodeAndMarkExplored(poleBId);

            _expData.movingToPoleCorridorId = null;
            Debug.Log($"[CorrMgr] Pole B touched. Starting inspection {_expData.pendingCorridorId}.");
            OnRotationRequested?.Invoke(false);
        }
        
        
        private bool HasCorridorBeenTransited(string newNodeId)
        {
            if (_graphConstruction.IsCorridorFullyTransited(newNodeId))
            {
                Debug.LogWarning($"[CorrMgr] Corridor '{newNodeId}' already transited.");
                _expData.currentEdge = null;
                OnNextActionNeeded?.Invoke();
                return true;
            }
            // Also check if the corridor has already been physically visited and is without pending doors
            if (!_graphConstruction.IsNodePhysicallyVisited($"{newNodeId}_B", out var targetCorridorNode)
                || targetCorridorNode.HasUnexploredEdges()) return false;
            Debug.LogWarning($"[CorrMgr] Corridor '{newNodeId}' already completed.");
            _expData.currentEdge = null;
            OnNextActionNeeded?.Invoke();
            return true;
        }
        
        public static bool HasCorridorAlreadyBeenExplored(CorridorPoleNode poleANode, CorridorData spawnCorridor) =>
            poleANode != null &&
            poleANode.edges.Exists(e => e?.id == $"central_{spawnCorridor.corridorId}"
                                        && e.state == EdgeState.Explored);

        public bool IsMovingToCorridor(string roomId)
        {
            var corridorData = ExplorationUtils.FindCorridorData(roomId);
            if (!corridorData) return false;
            _expNav.BeginMovingToPole(roomId, corridorData);
            _stateMachine.SetState(State.MovingToPole);
            return true;

        }
        
        private bool IsCorridorDoorAlreadyRegistered(DoorVarcoScript door) =>
            !door.isCorridorLink
            && (_expData.registeredTransitDoors.Contains(door.gameObject.name)
                || _expData.allCorridorDoors.Contains(door.gameObject.name));
        
        public bool IsDoorInsideCorridor(DoorVarcoScript door)
        {
            float corridorLength = Vector3.Distance(_expData.poleAPos, _expData.poleBPos);
            if (door.distFromB > corridorLength + 0.6f && door.distFromA > corridorLength + 0.6f)
            {
                Debug.Log($"[CorrMgr] {door.gameObject.name} is too far from the corridor, hence ignored.");
                return true;
            }

            return false;
        }

        public bool SetIdleIfCorridorIsNotExplored(GraphEdge edge, string newNodeId)
        {
            if (!_graphConstruction.IsNodeNull(_expData.currentNodeId, out var currentNode))
            {
                currentNode.edges.RemoveAll(e =>
                    e is { edgeType: EdgeType.Door } &&
                    e.toNodeId == edge.toNodeId);
            }

            if (HasCorridorBeenTransited(newNodeId)) return true;
            
            _expData.idleTargetCorridorId = newNodeId;
            _stateMachine.SetState(State.Idle);
            return false;
        }

        public bool ArrivedInCorridor()
        {
            CorridorData exitCorridor = ExplorationUtils.FindCorridorContainingPosition(
                _expNav.AgentPosition,
                3f);
            if (exitCorridor != null)
            { 
                _expNav.BeginMovingToPole1(exitCorridor, true);
                _stateMachine.SetState(State.MovingToPole);
                return true;
            }

            return false;
        }

        private void ScanCorridorDoorsFromScript(string corridorId)
        {
            Debug.Log("[CorrMgr] Scanning corridor: " + corridorId);
            foreach (var door in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
            {
                if (!ExplorationUtils.IsDoorConnectedToRoom(
                        door, 
                        corridorId, 
                        out var frontName, 
                        out var backName))
                    continue;
                
                Debug.Log($"[CorrMgr] Door {door.name} is connected to {corridorId}");

                if (IsCorridorDoorAlreadyRegistered(door))
                {
                    Debug.Log("[CorrMgr] Corridor is already registered.");
                    continue;
                }
            
                _graphConstruction.AddEdgesToCorridorDoor(corridorId, door, frontName, backName);

                _expData.registeredTransitDoors.Add(door.gameObject.name);
                _expData.allCorridorDoors.Add(door.gameObject.name);
            }
        }
        
        
        public void HandleCorridorPoleVisible(string corridorId, string poleId, Vector3 polePos)
        {
            if (!(_stateMachine.IsState(State.Idle) || _stateMachine.IsState(State.Backtracking))) return;
            if (!string.IsNullOrEmpty(_expData.idleTargetCorridorId) && corridorId != _expData.idleTargetCorridorId) return;

            var corridorData = ExplorationUtils.FindCorridorData(corridorId);
            if (corridorData == null) return;
        
            if (_expData.movingToPoleCorridorId == corridorId) return;
        
            _expNav.BeginMovingToPole(transform.position, corridorId, corridorData);
            _stateMachine.SetState(State.MovingToPole);

            Debug.Log($"[CorrMgr] Corridor {corridorId} spotted. Moving towards corridor.");
        }
        
    }
}