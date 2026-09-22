using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;
using UnityEngine.AI;

namespace script.avatar.ExplorationHelpers
{
    public class ExplorationNavigation : MonoBehaviour
    {
        private ExplorationData expData;
        private NavMeshAgent navAgent;
        
        private ExplorationManager expManager;

        public Vector3 AgentPosition => navAgent.transform.position;
        public bool PathPending => navAgent.pathPending;
        public bool ArrivedAtPole => navAgent.remainingDistance<expData.poleArrivalThreshold;
        public bool Arrived => IsWithin(expData.arrivalThreshold) && HasPath;
        public bool ArrivedAtConnector => IsWithin(expData.arrivalThreshold * 2f);
        public bool InsideInspectionDistance => IsWithin(expData.inspectionDistance);
        public bool HasPath => navAgent.hasPath;

        public void Stop() => navAgent.isStopped = true;

        private bool IsWithin(float remainingDistance) => navAgent.remainingDistance <= remainingDistance;

        private void Awake()
        {
            expData = GetComponent<ExplorationData>();
            expManager = GetComponent<ExplorationManager>();
            navAgent = GetComponent<NavMeshAgent>();
        }

        public FloorNode PrepareExploration()
        {
            foreach (var floor in expData.buildingGraph.floors)
                floor.explored = false;
    
            Vector3   agentPos  = navAgent.transform.position;
            FloorNode floorData = null;
            float     minDist   = float.MaxValue;
    
            foreach (var floor in expData.buildingGraph.floors)
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
                Debug.LogWarning("[ExplMgr] Piano non trovato o spawnObject mancante!");
                return null;
            }
    
            Vector3 startPos = floorData.spawnPosition;
            if (Vector3.Distance(agentPos, startPos) > 0.05f)
            {
                navAgent.enabled = false;
                navAgent.transform.position = startPos;
                navAgent.enabled = true;
                navAgent.Warp(startPos);
            }
            return floorData;
        }
        
        public void SetDestination(Vector3 destination) => navAgent.SetDestination(destination);
    

        public void BacktrackToNode(Vector3 targetPos)
        {
            navAgent.isStopped = false;
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
            expData.inRoomMode    = isRoom;
            expData.rotationAccum = 0f;
            navAgent.isStopped = true;
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
                // Corridoio: comportamento originale — entra oltre la porta
                Vector3 doorNormal = doorScript != null ? doorScript.transform.forward : transform.forward;
                Vector3 toDoor     = (edge.position - transform.position).normalized;
                if (Vector3.Dot(doorNormal, toDoor) < 0) doorNormal = -doorNormal;
                enterTarget = edge.position + doorNormal * expData.enterOffset;
            }
            else
            {
                // Stanza: vai al centro del GameObject della stanza
                var floorData = expData.buildingGraph?.GetFloor(expData.currentFloor);
                var roomObj   = ExplorationUtils.FindRoom(newNodeId, floorData);
                if (roomObj != null)
                    enterTarget = ExplorationUtils.GetRoomCenter(roomObj);
                else
                {
                    // Fallback: offset oltre la porta
                    Vector3 doorNormal = doorScript != null ? doorScript.transform.forward : transform.forward;
                    Vector3 toDoor     = (edge.position - transform.position).normalized;
                    if (Vector3.Dot(doorNormal, toDoor) < 0) doorNormal = -doorNormal;
                    enterTarget = edge.position + doorNormal * expData.enterOffset;
                }
            }
            return enterTarget;
        }
        
        
        public bool IsAdjacentRoomACorridorWithNavigation(string adjRoom)
        {
            // Se adjRoom è un corridoio con poli, non trattarlo come stanza normale
            var adjCorridor = ExplorationUtils.FindCorridorData(adjRoom);
            if (adjCorridor != null)
            {
                BeginMovingToPole1(adjRoom, adjCorridor, false);
                Debug.Log($"[ExplMgr] Destinazione è corridoio '{adjRoom}' → vado al polo.");
                return true;
            }

            return false;
        }
        
    
    
    public void BeginMovingToPole(string roomId, CorridorData corridorData)
    {
        float d1 = Vector3.Distance(transform.position, corridorData.Pole1Position);
        float d2 = Vector3.Distance(transform.position, corridorData.Pole2Position);
        expData.SetPendingCorridorAndPole(roomId, 
            d1 <= d2 ? "1" : "2", 
            d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
        expData.movingToPoleCorridorId = roomId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        SetDestination(d1 <= d2 ? corridorData.Pole1Position : corridorData.Pole2Position);
    }
    
    public void BeginMovingToPole(Vector3 startPos, string startNodeId, CorridorData spawnCorridor)
    {
        float d1 = Vector3.Distance(startPos, spawnCorridor.Pole1Position);
        float d2 = Vector3.Distance(startPos, spawnCorridor.Pole2Position);
        expData.SetPendingCorridorAndPole(startNodeId, 
            d1 <= d2 ? "1" : "2", 
            d1 <= d2 ? spawnCorridor.Pole1Position : spawnCorridor.Pole2Position);
        expData.movingToPoleCorridorId = startNodeId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        SetDestination(d1 <= d2 ? spawnCorridor.Pole1Position : spawnCorridor.Pole2Position);
    }
    
    public void BeginMovingToPole1(CorridorData corridor, bool nullDoor)
    {
        if(nullDoor) expData.pendingArrivalDoor = null;
        expData.SetPendingCorridorAndPole(corridor.corridorId, "1",  corridor.Pole1Position);
        expData.movingToPoleCorridorId = corridor.corridorId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        SetDestination(corridor.Pole1Position);
    }

    private void BeginMovingToPole1(string roomId, CorridorData corridor, bool nullDoor)
    {
        if(nullDoor) expData.pendingArrivalDoor = null;
        expData.SetPendingCorridorAndPole(roomId, "1",  corridor.Pole1Position);
        expData.movingToPoleCorridorId = roomId;
        expData.movingToPoleTimer      = 0f;
        navAgent.isStopped     = false;
        SetDestination(corridor.Pole1Position);
    }
    public void BeginTransitingToB(Vector3 poleAPos, Vector3 targetB)
    {
        navAgent.isStopped = false;
        SetDestination(targetB);
    }
    
    public void MoveToNextEdge(GraphEdge nextEdge)
    {
        expData.currentEdge = nextEdge;
        navAgent.isStopped = false;
        SetDestination(nextEdge.position);
    }

    public void MoveToAdjacentRoomWithTimeout(Vector3 adjPos)
    {
        expData.inRoomMode             = false;
        expData.movingToPoleCorridorId = null;
        expData.idleTargetCorridorId   = null;

        navAgent.isStopped = false;
        SetDestination(adjPos);
        expData.enteringRoomTimer   = 0f;
        expData.enteringRoomTimeout = 6f;
    }

    public void MoveToRoomCenter(string roomId, Vector3 center)
    {
        expData.currentNodeId = roomId;
        navAgent.isStopped = false;
        SetDestination(center);
    }
    
    //PORTE PER LE SCALE
    public IEnumerator TraverseStairsThen(int targetFloor) {
        var activeConnector = expData.pendingConnector;

        // Apri porta di partenza
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

        // Aspetta che il NavMesh porti l'agente fisicamente all'altro piano
        // (il NavMesh cammina sulle scale autonomamente)
        Vector3 destination = isNormalDir
            ? activeConnector.triggerEndPosition
            : activeConnector.triggerStartPosition;

        navAgent.isStopped = false;
        SetDestination(destination);

        // Aspetta l'arrivo
        yield return new WaitForSeconds(0.5f); // piccolo delay per far partire il path
        while (PathPending || ArrivedAtConnector)
        {
            yield return null;
        }

        // Apri porta di arrivo
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

        expManager.ExecuteFloorChange(targetFloor);
    }
    
    
    
    public bool FindAndExploreAdjacentFloors(List<FloorNode> unexplored)
    {
        unexplored.Sort((a, b) =>
            Mathf.Abs(a.floorIndex - expData.currentFloor)
                .CompareTo(Mathf.Abs(b.floorIndex - expData.currentFloor)));

        foreach (var targetFloor in unexplored)
        {
            var connectors = expData.buildingGraph.GetConnectors(
                expData.currentFloor, 
                targetFloor.floorIndex
            );
            connectors.RemoveAll(c => expData.traversedConnectors.Contains(c.id));
            if (connectors.Count == 0) continue;

            connectors.Sort((a, b) => a.costUp.CompareTo(b.costUp));
            expData.pendingConnector = connectors[0];

            bool isReverse = (expData.pendingConnector.floorFrom != expData.currentFloor);
            Vector3 navTarget = isReverse
                ? expData.pendingConnector.triggerEndPosition
                : expData.pendingConnector.triggerStartPosition;

            Debug.Log($"[ExplMgr] Piano {expData.currentFloor} → piano {targetFloor.floorIndex} " +
                      $"via '{expData.pendingConnector.id}' (diretto)");
            navAgent.isStopped = false;
            SetDestination(navTarget);
            return true;
        }

        return false;
    }

    public bool FindAndExploreOtherFloors(List<FloorNode> unexplored)
    {
        Debug.Log($"[ExplMgr] Nessun connettore diretto da piano {expData.currentFloor}. " +
                  $"Cerco percorso indiretto...");

        unexplored.Sort((a, b) => a.floorIndex.CompareTo(b.floorIndex));

        // Piani già visitati, ordinati per vicinanza al piano corrente
        var visitedFloors = new List<int>(expData.floorGraphs.Keys);
        visitedFloors.Sort((a, b) =>
            Mathf.Abs(a - expData.currentFloor).CompareTo(Mathf.Abs(b - expData.currentFloor)));

        foreach (var targetFloor in unexplored)
        {
            foreach (int midFloor in visitedFloors)
            {
                if (midFloor == expData.currentFloor) continue;

                // Il piano intermedio deve avere un connettore non traversato verso targetFloor
                var connToTarget = expData.buildingGraph.GetConnectors(midFloor, targetFloor.floorIndex);
                connToTarget.RemoveAll(c => expData.traversedConnectors.Contains(c.id));
                if (connToTarget.Count == 0) continue;

                // Cerca il connettore tra currentFloor e midFloor
                // (può essere già traversato — vogliamo ri-attraversarlo)
                var connToMid = expData.buildingGraph.GetConnectors(expData.currentFloor, midFloor);
                if (connToMid.Count == 0) continue;

                connToMid.Sort((a, b) => a.costUp.CompareTo(b.costUp));
                expData.pendingConnector = connToMid[0];

                bool isReverse = (expData.pendingConnector.floorFrom != expData.currentFloor);
                Vector3 navTarget = isReverse
                    ? expData.pendingConnector.triggerEndPosition
                    : expData.pendingConnector.triggerStartPosition;

                Debug.Log($"[ExplMgr] Percorso indiretto: piano {expData.currentFloor} → piano {midFloor} " +
                          $"→ piano {targetFloor.floorIndex} via '{expData.pendingConnector.id}'");
                navAgent.isStopped = false;
                SetDestination(navTarget);
                return true;
            }
        }

        return false;
    }


    public void MoveToNextTarget(bool isTargetCorridor, DoorVarcoScript doorScript, GraphEdge edge, string newNodeId)
    {
        // Calcolo target di navigazione
        Vector3 enterTarget = ComputeNavigationTarget(isTargetCorridor, doorScript, edge, newNodeId);

        if (!NavMesh.SamplePosition(enterTarget, out var hit, 2f, NavMesh.AllAreas))
        {
            enterTarget = edge.position;
            if (!NavMesh.SamplePosition(enterTarget, out hit, 2f, NavMesh.AllAreas))
            {
                hit.position = edge.position;
                Debug.LogWarning($"[ExplMgr] NavMesh non trovato vicino a '{newNodeId}'.");
            }
        }
        enterTarget = hit.position;

        navAgent.isStopped = false;
        SetDestination(enterTarget);
    }
    }
}