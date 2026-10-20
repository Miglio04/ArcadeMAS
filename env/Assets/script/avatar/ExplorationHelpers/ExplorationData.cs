using System;
using System.Collections.Generic;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(ExplorationManager))]
    public class ExplorationData : MonoBehaviour
    {
        [Header("Agent")] 
        public ExplorationVisionCone explorationVisionCone;

        [Header("Macro-Graph of the building")] 
        public BuildingGraph buildingGraph;
        
        [Header("Settings")] public float arrivalThreshold = 0.5f;
        public float enterOffset = 1.5f;
        public float poleArrivalThreshold = 1.2f;
        public float poleTimeoutSeconds = 4f;
        public float rotationSpeed = 120f;
        public float inspectionDistance = 1.2f;
        public float movingToPoleTimer = 0f;
        public float backtrackTimeout = 5f;

        [Header("Persistence")] public string buildingId = "building"; // ex. "RossiPalace_P1"

        [ContextMenu("Open the snapshot folder")]
        private void OpenSnapshotFolder()
        {
            System.Diagnostics.Process.Start(GraphPersistence.BaseDirectory);
        }
        
        
        [NonSerialized] public string movingToPoleCorridorId;
        
        
        [NonSerialized] public Dictionary<int, TopologicalGraph> floorGraphs
            = new Dictionary<int, TopologicalGraph>();

        /// <summary>All floors, property needed by BeliefTransmitter.</summary>
        public Dictionary<int, TopologicalGraph> AllFloorGraphs => floorGraphs;

        /// <summary>
        /// Link cross-floor: describes how the node conn_X in the graph floorA connects
        /// to the node conn_X in the graph floorB. Used by BeliefTransmitter to construct
        /// the complete graph of the building.
        /// </summary>
        public List<FloorConnectorLink> ConnectorLinks { get; }
            = new List<FloorConnectorLink>();

        [NonSerialized] public string currentNodeId;
        [NonSerialized] public bool inRoomMode = false;
        
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

        // Corridor towards which the agent is moving in Idle state.
        // Every other pole's signal is ignored.
        [NonSerialized] public string idleTargetCorridorId;
        [NonSerialized] public float enteringRoomTimer = 0f;
        [NonSerialized] public float enteringRoomTimeout = 3f;
        [NonSerialized] public Dictionary<string, string> registeredRoomDoors = new Dictionary<string, string>();
        [NonSerialized] public string lastCorridorLinkDoorName;
        [NonSerialized] public DoorVarcoScript pendingArrivalDoor = null;
        [NonSerialized] public HashSet<string> registeredTransitDoors = new HashSet<string>();
        [NonSerialized] public HashSet<string> allCorridorDoors = new HashSet<string>();
        [NonSerialized] public HashSet<string> traversedConnectors = new HashSet<string>();
        
        [NonSerialized] public Dictionary<string, HashSet<string>> roomArtifacts
            = new Dictionary<string, HashSet<string>>();

        [NonSerialized] public Dictionary<string, RoomObjectSnapshot> artifactData
            = new Dictionary<string, RoomObjectSnapshot>();
        
        void Awake()
        {
            if (buildingGraph != null)
                foreach (var floor in buildingGraph.floors)
                    floor.explored = false;
        }
    }
}