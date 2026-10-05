using System;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    public enum State
    {
        Idle, MovingToPole, Transiting, WaitingAtPoleB,
        Rotating360, MovingToDoor, InspectingDoor,
        EnteringRoom, InsideRoom, Backtracking,
        MovingToConnector, Completed, TraversingStairs
    }
    
    
    public class StateMachine : MonoBehaviour
    {
        private float _backtrackTimer = 0f;

        private float _idleTimer = 0f;
        
        private float _transitTimer = 0f;
        
        private State _state = State.Idle;
        
        private ExplorationData _expData;
        private GraphConstruction _graphConstruction;
        private ExplorationNavigation _expNav;

        public static event Action OnCurrentNodeNullAfterBacktracking;
        public static event Action OnNavigationToCorridorEnded;
        public static event Action<string, string, Vector3> OnPoleTouchedWhileTransiting;
        public static event Action<string, string, Vector3> OnPoleTouchedWhileIdle;
        public static event Action OnNextActionNeeded;
        public static event Action OnRotationCompleted;
        public static event Action<bool> OnRotationRequested;
        public static event Action<GraphEdge> OnDoorTraversable;


        void Awake()
        {
            _expData = GetComponent<ExplorationData>();
            _expNav = GetComponent<ExplorationNavigation>();
            _graphConstruction = GetComponent<GraphConstruction>();
        }
        
        // --------------------------------------------------------
        // EXPLORATION UPDATE
        // --------------------------------------------------------
        
        public void Tick()
        {
            switch (_state)
            {
                case State.MovingToPole:      UpdateMovingToPole();      break;
                case State.Transiting:        UpdateTransiting();        break;
                case State.Rotating360:       UpdateRotating360();       break;
                case State.MovingToDoor:      UpdateMovingToDoor();      break;
                case State.InspectingDoor:    UpdateInspectingDoor();    break;
                case State.EnteringRoom:      UpdateEnteringRoom();      break;
                case State.InsideRoom:        UpdateInsideRoom();        break;
                case State.Idle:          UpdateIdle();          break;
                case State.Backtracking:  UpdateBacktracking();  break;
                case State.MovingToConnector: UpdateMovingToConnector(); break;
            }
        }
        
        private void UpdateMovingToPole()
        {
            _expData.movingToPoleTimer += Time.deltaTime;

            if (_expData.movingToPoleTimer >= _expData.poleTimeoutSeconds)
            {
                _expData.movingToPoleCorridorId = null;
                _expData.movingToPoleTimer = 0f;
                OnNavigationToCorridorEnded?.Invoke();
                return;
            }

            if (_expNav.PathPending) return;
            if (_expNav.ArrivedAtPole)
            {
                _expData.movingToPoleCorridorId = null;
                _expData.movingToPoleTimer = 0f;
                OnNavigationToCorridorEnded?.Invoke();
            }
        }
        
        private void UpdateTransiting()
        {
            _transitTimer += Time.deltaTime;

            if (_expNav.PathPending && _transitTimer < _expData.poleTimeoutSeconds) return;

            if (_expNav.ArrivedAtPole || _transitTimer >= _expData.poleTimeoutSeconds)
            {
                _transitTimer = 0f;
                _expData.pendingCorridorId = _expData.transitCorridorId;
                _expData.pendingPoleId     = "B_fallback";
                _expData.pendingPolePos    = _expData.poleBPos;
                OnNavigationToCorridorEnded?.Invoke();
            }
        }
        
        private void UpdateRotating360()
        {
            _expNav.Stop();
            float step = _expData.rotationSpeed * Time.deltaTime;
            transform.Rotate(0, step, 0);
            _expData.rotationAccum += step;

            if (_expData.rotationAccum >= 360f)
            {
                OnRotationCompleted?.Invoke();
                OnNextActionNeeded?.Invoke();
            }
        }

        private void UpdateMovingToDoor()
    {
        if (_expNav.PathPending) return;
        if (_expNav.InsideInspectionDistance)
        {
            _expNav.Stop();
            _state = State.InspectingDoor;
        }
    }

    private void UpdateInspectingDoor()
    {
        if (_expData.currentEdge == null) { OnNextActionNeeded?.Invoke(); return; }

        if (_expData.currentEdge.doorState == DoorState.Locked)
        {
            _graphConstruction.MarkGraphEdgeExplored(_expData.currentEdge, null);
            _expData.currentEdge = null;
            OnNextActionNeeded?.Invoke();
            return;
        }

        var door = ExplorationUtils.FindDoorScript(_expData.currentEdge.id);
        if (door != null && !door.IsTraversable && !door.TryOpen())
        {
            _expData.currentEdge.doorState = DoorState.Locked;
            _graphConstruction.MarkGraphEdgeExplored(_expData.currentEdge, null);
            _expData.currentEdge = null;
            OnNextActionNeeded?.Invoke();
            return;
        }
        OnDoorTraversable?.Invoke(_expData.currentEdge);
    }

    private void UpdateEnteringRoom()
    {
        if (_expNav.PathPending) return;
        _expData.enteringRoomTimer += Time.deltaTime;

      if (_expNav.Arrived || _expData.enteringRoomTimer >= _expData.enteringRoomTimeout)
        {
            _expData.enteringRoomTimer = 0f;
            // Parameter is true if we are inside a room.
            OnRotationRequested?.Invoke(true);
        }
    }

    private static void UpdateInsideRoom() => OnRotationRequested?.Invoke(true);

    private void UpdateBacktracking()
    {
        if (_expNav.PathPending) return;

        _backtrackTimer += Time.deltaTime;
        bool arrived = _expNav.Arrived;
        bool pathFailed = !_expNav.HasPath;
        bool timedOut   = _backtrackTimer >= _expData.backtrackTimeout;

        if (arrived || pathFailed || timedOut)
        {
            _backtrackTimer = 0f;
            
            _expData.currentNodeId = _graphConstruction.GetCurrentNodeId();
            if (string.IsNullOrEmpty(_expData.currentNodeId)) { OnCurrentNodeNullAfterBacktracking?.Invoke(); return; }

            Debug.Log($"[StateMachine] Backtrack completed in {_expData.currentNodeId}. Waiting for next action.");
            _state = State.Idle;
            OnNextActionNeeded?.Invoke();
        }
    }

    private void UpdateMovingToConnector()
    {
        if (_expNav.PathPending) return;
        if (_expData.pendingConnector != null && _expNav.ArrivedAtConnector)
        {
            int targetFloor = (_expData.pendingConnector.floorFrom == _expData.currentFloor)
                ? _expData.pendingConnector.floorTo
                : _expData.pendingConnector.floorFrom;
            _state = State.TraversingStairs;
            
            StartCoroutine(_expNav.TraverseStairsThen(targetFloor));
        }
    }
    
    private void UpdateIdle()
    {
        if (string.IsNullOrEmpty(_expData.idleTargetCorridorId)) return;
        if (_expNav.PathPending) return;

        _idleTimer += Time.deltaTime;

        bool arrived  = _expNav.Arrived;
        bool timedOut = _idleTimer >= 2.0f;

        if (!arrived && !timedOut) return;

        _idleTimer = 0f;
        var corridorData = ExplorationUtils.FindCorridorData(_expData.idleTargetCorridorId);
        if (corridorData == null) { _expData.idleTargetCorridorId = null; return; }

        _expNav.BeginMovingToPole(transform.position, _expData.idleTargetCorridorId, corridorData);
        SetState(State.MovingToPole);
    }

    public void SetState(State otherState) => _state = otherState;

    
    public bool IsState(State otherState) => _state == otherState;
        
    public void HandlePoleTouched(string corridorId, string poleId, Vector3 polePos)
    {
        Debug.Log($"[StateMachine] Physical signal sent by {corridorId}_{poleId}. State: {_state}");

        if (_state == State.Transiting && corridorId == _expData.transitCorridorId)
        {
            OnPoleTouchedWhileTransiting?.Invoke(corridorId, poleId, polePos);
            return;
        }

        if (_state == State.MovingToPole && corridorId == _expData.pendingCorridorId)
        {
            _expData.pendingPolePos = polePos;
            OnNavigationToCorridorEnded?.Invoke();
            return;
        }

        if (_state == State.Idle)
        {
            OnPoleTouchedWhileIdle?.Invoke(corridorId, poleId, polePos);
            return;
        }

        ExplorationUtils.ResetCorridorPoles(corridorId);
    }

    public void DoBacktrack(string targetNode, GraphNode targetData)
    {
        if (targetNode == null) { return; }

        // If the target is a corridor pole, skip the physical movement.
        if (targetNode.EndsWith("_A") || targetNode.EndsWith("_B"))
        {
            _backtrackTimer = 0f;
            _expData.currentNodeId  = targetNode;
            Debug.Log($"[StateMachine] Backtrack pointing to corridor node '{targetNode}' — Skip physical movement.");
            _state = State.Idle;
            OnNextActionNeeded?.Invoke();
            return;
        }

        _backtrackTimer = 0f;
        _expNav.BacktrackToNode(targetData.position);
        _state = State.Backtracking;
    }

    public bool TryTransition(State initialState, State endingState, Action action)
    {
        if (!IsState(initialState))
            return false;
        
        action?.Invoke();
        SetState(endingState);
        return true;
    }

    public bool TransitionWithCondition(State endingState, Func<bool> condition)
    {
        if (condition?.Invoke() != true) return false;
        SetState(endingState);
        return true;
    }

    }
}