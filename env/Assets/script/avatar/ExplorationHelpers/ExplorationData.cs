using System;
using System.Collections.Generic;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(ExplorationManager))]
    public class ExplorationData : MonoBehaviour
    {
        [Header("Agente")] public ExplorationVisionCone explorationVisionCone;

        [Header("Macro-Grafo edificio")] 
        public BuildingGraph buildingGraph;

        [Header("Persistenza")] public string buildingId = "building"; // es. "PalazzoRossi_P1"

        [ContextMenu("Apri cartella snapshot")]
        private void OpenSnapshotFolder()
        {
            System.Diagnostics.Process.Start(GraphPersistence.BaseDirectory);
        }

        [Header("Impostazioni")] public float arrivalThreshold = 0.5f;
        public float enterOffset = 1.5f;
        public float poleArrivalThreshold = 1.2f;
        public float poleTimeoutSeconds = 4f;
        public float rotationSpeed = 120f;
        public float inspectionDistance = 1.2f;
        public float movingToPoleTimer = 0f;
        public float backtrackTimeout = 5f;

        public bool inRoomMode = false;

        [NonSerialized] public string movingToPoleCorridorId;


        // --------------------------------------------------------
        // MACCHINA A STATI
        // --------------------------------------------------------



        // ─────────────────────────────────────────────────────────
        // MULTI-GRAFO: un TopologicalGraph per piano
        // ─────────────────────────────────────────────────────────
        [NonSerialized] public Dictionary<int, TopologicalGraph> floorGraphs
            = new Dictionary<int, TopologicalGraph>();

        /// <summary>Tutti i grafi, accessibili da BeliefTransmitter.</summary>
        public Dictionary<int, TopologicalGraph> AllFloorGraphs => floorGraphs;

        /// <summary>
        /// Link cross-floor: descrive come il nodo conn_X nel grafo pianoA si
        /// collega al nodo conn_X nel grafo pianoB. Usato da BeliefTransmitter
        /// per ricostruire il grafo completo dell'edificio.
        /// </summary>
        public List<FloorConnectorLink> ConnectorLinks { get; }
            = new List<FloorConnectorLink>();

        [NonSerialized] public string currentNodeId;
        public string CurrentNodeId => currentNodeId;

        [NonSerialized] public string transitCorridorId;
        [NonSerialized] public Vector3 poleAPos;
        [NonSerialized] public Vector3 poleBPos;

        [NonSerialized] public string pendingCorridorId;
        [NonSerialized] public string pendingPoleId;
        [NonSerialized] public Vector3 pendingPolePos;

        [NonSerialized] public GraphEdge currentEdge;

        [NonSerialized] public float rotationAccum = 0f;

        [NonSerialized] public int currentFloor;

        [NonSerialized] public VerticalConnector pendingConnector;

        // Corridoio verso cui l'agente sta navigando in stato Idle
        // (dopo EnterThroughEdge su un varco corridoio). Blocca segnali
        // di poli di altri corridoi finché non si tocca il polo corretto.
        [NonSerialized] public string idleTargetCorridorId;
        [NonSerialized] public float enteringRoomTimer = 0f;
        [NonSerialized] public float enteringRoomTimeout = 3f;
        [NonSerialized] public Dictionary<string, string> registeredRoomDoors = new Dictionary<string, string>();
        [NonSerialized] public string lastCorridorLinkDoorName;
        [NonSerialized] public DoorVarcoScript pendingArrivalDoor = null;
        [NonSerialized] public HashSet<string> registeredTransitDoors = new HashSet<string>();
        [NonSerialized] public HashSet<string> allCorridorDoors = new HashSet<string>();


        void Awake()
        {
            if (buildingGraph != null)
                foreach (var floor in buildingGraph.floors)
                    floor.explored = false;
        }

        public void SetPendingCorridorAndPole(string corridorId, string poleId, Vector3 polePos)
        {
            pendingCorridorId = corridorId;
            pendingPoleId = poleId;
            pendingPolePos = polePos;
        }

        public bool TryRegisterRoomDoor(DoorVarcoScript door)
        {
            if (registeredRoomDoors.TryAdd(door.gameObject.name, currentNodeId))
            {
                return false;
            }

            return true;
        }

        public bool IsRoomPointedByConnectorDoorInvalid(string connRoomBeyond, VerticalConnector connectorForDoor) =>
            string.IsNullOrEmpty(connRoomBeyond) || connRoomBeyond == currentNodeId
                                                 || connRoomBeyond == connectorForDoor.id;


        public string FindNextNodeId(DoorVarcoScript doorScript, GraphEdge edge)
        {
            string newNodeId;
            if (doorScript != null && (edge.id.StartsWith("bw_") || edge.id.StartsWith("fw_")))
            {
                string corridorId = (currentNodeId.EndsWith("_A") || currentNodeId.EndsWith("_B"))
                    ? currentNodeId.Substring(0, currentNodeId.Length - 2)
                    : currentNodeId;
                string front = doorScript.roomFront?.name ?? "";
                string back = doorScript.roomBack?.name ?? "";
                if (front == corridorId) newNodeId = back;
                else if (back == corridorId) newNodeId = front;
                else newNodeId = doorScript.GetRoomNameBeyondDoor(transform.position);
            }
            else if (doorScript != null)
                newNodeId = doorScript.GetRoomNameBeyondDoor(transform.position);
            else
                newNodeId = edge.id;

            return newNodeId;
        }

        private (string adjRoom, string adjDoorName, Vector3 adjPos) FindAdjacentRoomThroughPhysicalDoor(
            string connNodeId, int targetFloor)
        {
            string adjRoom = null;
            string adjDoorName = null;
            Vector3 adjPos = Vector3.zero;
            var arrDoor = pendingArrivalDoor;
            string front = arrDoor.roomFront?.name;
            string back = arrDoor.roomBack?.name;
            string notNullCandidate = string.IsNullOrEmpty(front) ? back : front;
            string candidate = (back == connNodeId)
                ? front
                : notNullCandidate;
            candidate = (front == connNodeId) ? back : candidate;

            var fdAdj = buildingGraph?.GetFloor(targetFloor);
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
            var fdAdj = buildingGraph?.GetFloor(targetFloor);
            if (fdAdj != null)
            {
                foreach (string rid in fdAdj.roomIds)
                {
                    string dName = FindDoorBetweenNodes(connNodeId, rid);
                    if (string.IsNullOrEmpty(dName)) continue;
                    var rObj = ExplorationUtils.FindRoom(rid, fdAdj);
                    if (rObj == null) continue;
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
            // Tentativo 1: usa la porta di arrivo fisica
            if (pendingArrivalDoor != null)
            {
                (adjRoom, adjDoorName, adjPos) = FindAdjacentRoomThroughPhysicalDoor(connNodeId, targetFloor);
            }

            // Tentativo 2: fallback tramite FindDoorBetweenNodes
            if (string.IsNullOrEmpty(adjRoom))
            {
                (adjRoom, adjDoorName, adjPos) = FindAdjacentRoomThroughRoomNodes(connNodeId, targetFloor);
            }

            return (adjRoom, adjDoorName, adjPos);
        }

        public string FindDoorBetweenNodes(string nodeA, string nodeB)
        {
            string dName = null;
            foreach (var conn in buildingGraph?.connectors ?? new List<VerticalConnector>())
            {
                dName = ExplorationUtils.FindDoorBetweenNodesByConnector(conn, nodeA, nodeB) ?? dName;
            }

            if(dName != null) return dName;
            
            // fallback DoorVarcoScript — invariato
            foreach (var d in FindObjectsByType<DoorVarcoScript>(FindObjectsSortMode.None))
            {
                dName = ExplorationUtils.FindDoorBetweenNodesByDoor(d, nodeA, nodeB) ?? dName;
            }

            return dName;
        }

        public VerticalConnector FindConnectorForDoor(string doorName)
        {
            if (buildingGraph == null) return null;
            foreach (var c in buildingGraph.connectors)
            {
                if (c.triggerStartObject?.name == doorName) return c;
                if (c.triggerEndObject?.name == doorName) return c;
            }
            return null;
        }

        public bool IsConnectorDoor(string doorName)
        {
            if (buildingGraph == null) return false;
            foreach (var connector in buildingGraph.connectors)
            {
                if (connector.triggerStartObject?.name == doorName) return true;
                if (connector.triggerEndObject?.name == doorName) return true;
            }

            return false;
        }

        public bool IsCorridorDoorAlreadyRegistered(DoorVarcoScript door) =>
            !door.isCorridorLink
            && (registeredTransitDoors.Contains(door.gameObject.name)
                || allCorridorDoors.Contains(door.gameObject.name));


        public bool IsRoomDoorAlreadyRegistered(string roomId, DoorVarcoScript door) =>
            registeredRoomDoors.TryGetValue(door.gameObject.name, out string from)
            && from == roomId;

    }
}