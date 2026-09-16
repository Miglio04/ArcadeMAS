using System;
using System.Collections.Generic;
using UnityEngine;

namespace script.avatar.ExplorationHelpers
{
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(ExplorationManager))]
    public class ExplorationData: MonoBehaviour
    {
    [Header("Agente")] 
    public ExplorationVisionCone explorationVisionCone;

    [Header("Macro-Grafo edificio")] public BuildingGraph buildingGraph;

    [Header("Persistenza")] public string buildingId = "building"; // es. "PalazzoRossi_P1"

    [ContextMenu("Apri cartella snapshot")]
    private void OpenSnapshotFolder()
    {
        System.Diagnostics.Process.Start(GraphPersistence.BaseDirectory);
    }

    [Header("Impostazioni")] 
    public float arrivalThreshold = 0.5f;
    public float enterOffset = 1.5f;
    public float poleArrivalThreshold = 1.2f;
    public float poleTimeoutSeconds = 4f;
    public float rotationSpeed = 120f;
    public float inspectionDistance = 1.2f;
    public float movingToPoleTimer = 0f;
    public float backtrackTimeout = 5f;
    
    public bool inRoomMode = false;
    
    [NonSerialized]
    public string movingToPoleCorridorId;


    // --------------------------------------------------------
    // MACCHINA A STATI
    // --------------------------------------------------------



    // ─────────────────────────────────────────────────────────
    // MULTI-GRAFO: un TopologicalGraph per piano
    // ─────────────────────────────────────────────────────────
    [NonSerialized]
    public Dictionary<int, TopologicalGraph> floorGraphs
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

    [NonSerialized]
    public string currentNodeId;
    public string CurrentNodeId => currentNodeId;
    
    [NonSerialized]
    public string transitCorridorId;
    [NonSerialized]
    public Vector3 poleAPos;
    [NonSerialized]
    public Vector3 poleBPos;

    [NonSerialized]
    public string pendingCorridorId;
    [NonSerialized]
    public string pendingPoleId;
    [NonSerialized]
    public Vector3 pendingPolePos;

    [NonSerialized]
    public GraphEdge currentEdge;

    [NonSerialized]
    public float rotationAccum = 0f;
    
    [NonSerialized]
    public int currentFloor;
    
    [NonSerialized]
    public VerticalConnector pendingConnector;

    // Corridoio verso cui l'agente sta navigando in stato Idle
    // (dopo EnterThroughEdge su un varco corridoio). Blocca segnali
    // di poli di altri corridoi finché non si tocca il polo corretto.
    [NonSerialized]
    public string idleTargetCorridorId;
    [NonSerialized]
    public float enteringRoomTimer   = 0f;
    [NonSerialized]
    public float enteringRoomTimeout = 3f;
    [NonSerialized]
    public Dictionary<string, string> registeredRoomDoors = new Dictionary<string, string>();
    [NonSerialized]
    public string lastCorridorLinkDoorName;

    
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
    }
}