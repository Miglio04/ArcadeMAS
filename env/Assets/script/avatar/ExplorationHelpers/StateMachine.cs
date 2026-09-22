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
        private float backtrackTimer = 0f;

        private float idleTimer = 0f;
        
        private float transitTimer = 0f;
        
        private State state = State.Idle;
        
        private ExplorationData expData;
        private ExplorationManager expManager;
        private GraphConstruction graphConstruction;
        private ExplorationNavigation expNav;

        void Awake()
        {
            expData = GetComponent<ExplorationData>();
            expManager = GetComponent<ExplorationManager>();
            expNav = GetComponent<ExplorationNavigation>();
            graphConstruction = GetComponent<GraphConstruction>();
        }
        
        // --------------------------------------------------------
        // UPDATE
        // --------------------------------------------------------
        
        public void Tick()
        {
            switch (state)
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
            expData.movingToPoleTimer += Time.deltaTime;

            if (expData.movingToPoleTimer >= expData.poleTimeoutSeconds)
            {
                expData.movingToPoleCorridorId = null;
                expData.movingToPoleTimer = 0f;
                expManager.ProcessPoleTouched();
                return;
            }

            if (expNav.PathPending) return;
            if (expNav.ArrivedAtPole)
            {
                expData.movingToPoleCorridorId = null;
                expData.movingToPoleTimer = 0f;
                expManager.ProcessPoleTouched();
            }
        }
        
        private void UpdateTransiting()
        {
            transitTimer += Time.deltaTime;

            if (expNav.PathPending && transitTimer < expData.poleTimeoutSeconds) return;

            if (expNav.ArrivedAtPole || transitTimer >= expData.poleTimeoutSeconds)
            {
                transitTimer = 0f;
                expData.pendingCorridorId = expData.transitCorridorId;
                expData.pendingPoleId     = "B_fallback";
                expData.pendingPolePos    = expData.poleBPos;
                expManager.ProcessPoleTouched();
            }
        }
        
        private void UpdateRotating360()
        {
            expNav.Stop();
            float step = expData.rotationSpeed * Time.deltaTime;
            transform.Rotate(0, step, 0);
            expData.rotationAccum += step;

            if (expData.rotationAccum >= 360f)
            {
                expManager.EndRotating360();
                expManager.DecideNextAction();
            }
        }

        private void UpdateMovingToDoor()
    {
        if (expNav.PathPending) return;
        if (expNav.InsideInspectionDistance)
        {
            expNav.Stop();
            state = State.InspectingDoor;
        }
    }

    private void UpdateInspectingDoor()
    {
        if (expData.currentEdge == null) { expManager.DecideNextAction(); return; }

        if (expData.currentEdge.doorState == DoorState.Locked)
        {
            graphConstruction.MarkGraphEdgeExplored(expData.currentEdge, null);
            expData.currentEdge = null;
            expManager.DecideNextAction();
            return;
        }

        var door = ExplorationUtils.FindDoorScript(expData.currentEdge.id);
        if (door != null && !door.IsTraversable && !door.TryOpen())
        {
            expData.currentEdge.doorState = DoorState.Locked;
            graphConstruction.MarkGraphEdgeExplored(expData.currentEdge, null);
            expData.currentEdge = null;
            expManager.DecideNextAction();
            return;
        }

        expManager.EnterThroughEdge(expData.currentEdge);
    }

    private void UpdateEnteringRoom()
    {
        if (expNav.PathPending) return;
        expData.enteringRoomTimer += Time.deltaTime;

      if (expNav.Arrived || expData.enteringRoomTimer >= expData.enteringRoomTimeout)
        {
            expData.enteringRoomTimer = 0f;
            expManager.StartRotation360(isRoom: true);
        }
    }

    private void UpdateInsideRoom() => expManager.StartRotation360(isRoom: true);

    private void UpdateBacktracking()
    {
        if (expNav.PathPending) return;

        backtrackTimer += Time.deltaTime;
        bool arrived = expNav.Arrived;
        bool pathFailed = !expNav.HasPath;
        bool timedOut   = backtrackTimer >= expData.backtrackTimeout;

        if (arrived || pathFailed || timedOut)
        {
            backtrackTimer = 0f;
            
            expData.currentNodeId = graphConstruction.GetCurrentNodeId();
            if (string.IsNullOrEmpty(expData.currentNodeId)) { expManager.CheckFloorCompletion(); return; }

            Debug.Log($"[ExplMgr] Backtrack completato in {expData.currentNodeId}. Prossima azione!");
            state = State.Idle;
            expManager.DecideNextAction();
        }
    }

    private void UpdateMovingToConnector()
    {
        if (expNav.PathPending) return;
        if (expData.pendingConnector != null && expNav.ArrivedAtConnector)
        {
            int targetFloor = (expData.pendingConnector.floorFrom == expData.currentFloor)
                ? expData.pendingConnector.floorTo
                : expData.pendingConnector.floorFrom;
            state = State.TraversingStairs;
            
            StartCoroutine(expNav.TraverseStairsThen(targetFloor));
        }
    }
    
    private void UpdateIdle()
    {
        if (string.IsNullOrEmpty(expData.idleTargetCorridorId)) return;
        if (expNav.PathPending) return;

        idleTimer += Time.deltaTime;

        bool arrived  = expNav.Arrived;
        bool timedOut = idleTimer >= 2.0f;

        if (!arrived && !timedOut) return;

        idleTimer = 0f;
        var corridorData = ExplorationUtils.FindCorridorData(expData.idleTargetCorridorId);
        if (corridorData == null) { expData.idleTargetCorridorId = null; return; }

        expNav.BeginMovingToPole(transform.position, expData.idleTargetCorridorId, corridorData);
        SetState(State.MovingToPole);
    }

    public void SetState(State otherState) => state = otherState;

    
    public bool IsState(State otherState) => state == otherState;
    
    // UPDATE STATES BASED OFF SITUATIONS
        
    public void HandlePoleTouched(string corridorId, string poleId, Vector3 polePos)
    {
        Debug.Log($"[ExplMgr] Segnale fisico da {corridorId}_{poleId}. Stato: {state}");

        if (state == State.Transiting && corridorId == expData.transitCorridorId)
        {
            expManager.PoleTouchedWhileTransiting(corridorId, poleId, polePos);
            return;
        }

        if (state == State.MovingToPole && corridorId == expData.pendingCorridorId)
        {
            expData.pendingPolePos = polePos;
            expManager.ProcessPoleTouched();
            return;
        }

        if (state == State.Idle)
        {
            expManager.PoleTouchedWhileIdle(corridorId, poleId, polePos);
            return;
        }

        ExplorationUtils.ResetCorridorPoles(corridorId);
    }
    
    public void HandleCorridorPoleVisible(string corridorId, string poleId, Vector3 polePos)
    {
        if (state != State.Idle && state != State.Backtracking) return;
        if (!string.IsNullOrEmpty(expData.idleTargetCorridorId) && corridorId != expData.idleTargetCorridorId) return;

        var corridorData = ExplorationUtils.FindCorridorData(corridorId);
        if (corridorData == null) return;
        
        if (expData.movingToPoleCorridorId == corridorId) return;
        
        expNav.BeginMovingToPole(transform.position, corridorId, corridorData);
        SetState(State.MovingToPole);

        Debug.Log($"[ExplMgr] Corridoio {corridorId} individuato → mi avvicino.");
    }

    public void DoBacktrack(string targetNode, GraphNode targetData)
    {
        if (targetNode == null) { return; }

        // Se il backtrack target è un polo corridoio, salta il viaggio fisico
        // e vai direttamente alla porta successiva
        if (targetNode.EndsWith("_A") || targetNode.EndsWith("_B"))
        {
            backtrackTimer = 0f;
            expData.currentNodeId  = targetNode;
            Debug.Log($"[ExplMgr] Backtrack diretto a '{targetNode}' — salto viaggio fisico.");
            state = State.Idle;
            expManager.DecideNextAction();
            return;
        }

        backtrackTimer = 0f;
        expNav.BacktrackToNode(targetData.position);
        state = State.Backtracking;
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

    public void ProcessPoleTouched(CorridorData corridorData)
    {

        if (!IsState(State.Transiting))
        {
            expManager.TouchedFirstPole(corridorData);
        }
        else
        {
            graphConstruction.TouchedSecondPole();
        }
    }
    }
}