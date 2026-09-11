using System;
using UnityEngine;
using UnityEngine.AI;

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
        private NavMeshAgent navAgent;

        void Awake()
        {
            expData = GetComponent<ExplorationData>();
            expManager = GetComponent<ExplorationManager>();
            navAgent = GetComponent<NavMeshAgent>();
        }
        
        // --------------------------------------------------------
        // UPDATE
        // --------------------------------------------------------
        
        void Update()
        {
            // WARN: con la mappa è più facile da leggere e più veloce, se voglio aggiungere altri metodi in futuro
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

            if (navAgent.pathPending) return;
            if (navAgent.remainingDistance < expData.poleArrivalThreshold)
            {
                expData.movingToPoleCorridorId = null;
                expData.movingToPoleTimer = 0f;
                expManager.ProcessPoleTouched();
            }
        }
        
        private void UpdateTransiting()
        {
            transitTimer += Time.deltaTime;

            if (navAgent.pathPending && transitTimer < expData.poleTimeoutSeconds) return;

            if (navAgent.remainingDistance < expData.poleArrivalThreshold 
                || transitTimer >= expData.poleTimeoutSeconds)
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
            navAgent.isStopped = true;
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
        if (navAgent.pathPending) return;
        if (navAgent.remainingDistance <= expData.inspectionDistance)
        {
            navAgent.isStopped = true;
            state              = State.InspectingDoor;
        }
    }

    private void UpdateInspectingDoor()
    {
        if (expData.currentEdge == null) { expManager.DecideNextAction(); return; }

        if (expData.currentEdge.doorState == DoorState.Locked)
        {
            expData.graph.MarkEdgeExplored(expData.currentNodeId, expData.currentEdge.id, null);
            expData.currentEdge = null;
            navAgent.isStopped = false;
            expManager.DecideNextAction();
            return;
        }

        var door = expManager.FindDoorScript(expData.currentEdge.id);
        if (door != null && !door.IsTraversable && !door.TryOpen())
        {
            expData.currentEdge.doorState = DoorState.Locked;
            expData.graph.MarkEdgeExplored(expData.currentNodeId, expData.currentEdge.id, null);
            expData.currentEdge = null;
            navAgent.isStopped = false;
            expManager.DecideNextAction();
            return;
        }

        expManager.EnterThroughEdge(expData.currentEdge);
    }



    private void UpdateEnteringRoom()
    {
        if (navAgent.pathPending) return;
        expData.enteringRoomTimer += Time.deltaTime;

      if (navAgent.remainingDistance < expData.arrivalThreshold && !navAgent.pathPending 
          || expData.enteringRoomTimer >= expData.enteringRoomTimeout)
        {
            expData.enteringRoomTimer = 0f;
            expManager.StartRotation360(isRoom: true);
        }
    }

    private void UpdateInsideRoom() => expManager.StartRotation360(isRoom: true);

    private void UpdateBacktracking()
    {
        if (navAgent.pathPending) return;

        backtrackTimer += Time.deltaTime;
        bool arrived    = navAgent.hasPath && navAgent.remainingDistance <= expData.arrivalThreshold;
        bool pathFailed = !navAgent.hasPath;
        bool timedOut   = backtrackTimer >= expData.backtrackTimeout;

        if (arrived || pathFailed || timedOut)
        {
            backtrackTimer = 0f;
            
            expData.currentNodeId = expData.graph.CurrentNodeId;
            if (string.IsNullOrEmpty(expData.currentNodeId)) { expManager.CheckFloorCompletion(); return; }

            Debug.Log($"[ExplMgr] Backtrack completato in {expData.currentNodeId}. Prossima azione!");
            state = State.Idle;
            expManager.DecideNextAction();
        }
    }

    private void UpdateMovingToConnector()
    {
        if (navAgent.pathPending) return;
        if (expData.pendingConnector != null &&
            navAgent.remainingDistance <= expData.arrivalThreshold * 2f)
        {
            int targetFloor = (expData.pendingConnector.floorFrom == expData.currentFloor)
                ? expData.pendingConnector.floorTo
                : expData.pendingConnector.floorFrom;
            state = State.TraversingStairs;
            
            StartCoroutine(expManager.TraverseStairsThen(targetFloor));
        }
    }
    
    private void UpdateIdle()
    {
        if (string.IsNullOrEmpty(expData.idleTargetCorridorId)) return;
        if (navAgent.pathPending) return;

        idleTimer += Time.deltaTime;

        bool arrived  = navAgent.hasPath && navAgent.remainingDistance <= expData.arrivalThreshold;
        bool timedOut = idleTimer >= 2.0f;

        if (!arrived && !timedOut) return;

        idleTimer = 0f;
        var corridorData = expManager.FindCorridorData(expData.idleTargetCorridorId);
        if (corridorData == null) { expData.idleTargetCorridorId = null; return; }

        expManager.BeginMovingToPole(transform.position, expData.idleTargetCorridorId, corridorData);
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

        expManager.ResetCorridorPoles(corridorId);
    }
    
    public void HandleCorridorPoleVisible(string corridorId, string poleId, Vector3 polePos)
    {
        if (state != State.Idle && state != State.Backtracking) return;
        if (!string.IsNullOrEmpty(expData.idleTargetCorridorId) && corridorId != expData.idleTargetCorridorId) return;

        var corridorData = expManager.FindCorridorData(corridorId);
        if (corridorData == null) return;
        
        if (expData.movingToPoleCorridorId == corridorId) return;
        
        expManager.BeginMovingToPole(transform.position, corridorId, corridorData);

        Debug.Log($"[ExplMgr] Corridoio {corridorId} individuato → mi avvicino.");
    }

    public void DoBacktrack()
    {
        (string targetNode, GraphNode targetData) = expManager.BacktrackingLoop();

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
        navAgent.isStopped = false;
        navAgent.SetDestination(targetData.position);
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
    }
}