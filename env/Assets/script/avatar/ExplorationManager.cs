using System.Collections.Generic;
using script.avatar.ExplorationHelpers;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace script.avatar
{
// ─────────────────────────────────────────────────────────────
// DATA STRUCTURE: link cross-floor between two graph floors
// ─────────────────────────────────────────────────────────────
    [System.Serializable]
    public struct FloorConnectorLink
    {
        public string  connectorId;
        public int     floorFrom;
        public string  nodeIdFrom; 
        public Vector3 posFrom;
        public int     floorTo;
        public string  nodeIdTo;
        public Vector3 posTo;
        public bool    bidirectional;
    }


// ─────────────────────────────────────────────────────────────

    [RequireComponent(typeof(ExplorationData))]
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(GraphConstruction))]
    [RequireComponent(typeof(ExplorationNavigation))]
    [RequireComponent(typeof(FloorManager))]
    [RequireComponent(typeof(CorridorManager))]
    [RequireComponent(typeof(DoorHandler))]
    [RequireComponent(typeof(RoomHelper))]
    public class ExplorationManager : MonoBehaviour
    {
        private bool _explorationStarted = false;
        
        private ExplorationData _expData;

        private StateMachine _stateMachine;

        private GraphConstruction _graphConstruction;

        private ExplorationNavigation _expNav;

        private FloorManager _floorManager;
        private CorridorManager _corridorManager;
        private RoomHelper _roomHelper;
        private DoorHandler _doorHandler;
        
        // --------------------------------------------------------
        // AWAKE
        // --------------------------------------------------------
        void Awake()
        {
            _expData = GetComponent<ExplorationData>();
            _stateMachine = GetComponent<StateMachine>();
            _graphConstruction = GetComponent<GraphConstruction>();
            _expNav = GetComponent<ExplorationNavigation>();
            _floorManager = GetComponent<FloorManager>();
            _corridorManager = GetComponent<CorridorManager>();
            _doorHandler = GetComponent<DoorHandler>();
            _roomHelper = GetComponent<RoomHelper>();
            
            // FloorManager
            FloorManager.OnAllFloorsCompleted += OnAllFloorsCompleted;
            FloorManager.OnNewFloorRoomOrExploredCorridor += StartRotating360;
            FloorManager.OnNextActionNeeded += DecideNextAction;
            
            // CorridorManager
            CorridorManager.OnRotationRequested += StartRotating360;
            CorridorManager.OnNextActionNeeded += DecideNextAction;

            
            // GraphConstruction
            GraphConstruction.OnBacktrackNodeNull += _floorManager.CheckFloorCompletion;
            GraphConstruction.OnCurrentNodeNull += DoBacktrack;
            
            // ExplorationNavigation
            ExplorationNavigation.OnStairsTraversed += _floorManager.ExecuteFloorChange;
            ExplorationNavigation.OnMovingToPole += _corridorManager.SetPendingCorridorAndPole;
            
            // StateMachine
            StateMachine.OnCurrentNodeNullAfterBacktracking += _floorManager.CheckFloorCompletion;
            StateMachine.OnNavigationToCorridorEnded += _corridorManager.ProcessPoleTouched;
            StateMachine.OnPoleTouchedWhileTransiting += _corridorManager.PoleTouchedWhileTransiting;
            StateMachine.OnPoleTouchedWhileIdle += _corridorManager.PoleTouchedWhileIdle;
            StateMachine.OnNextActionNeeded += DecideNextAction;
            StateMachine.OnRotationRequested += StartRotating360;
            StateMachine.OnRotationCompleted += EndRotating360;
            StateMachine.OnDoorTraversable += _floorManager.EnterThroughEdge;
            
            // CorridorPoleScript
            CorridorPoleScript.OnPoleTouched += _stateMachine.HandlePoleTouched;
            
            // ExplorationVisionCone
            ExplorationVisionCone.OnDoorDiscovered += _doorHandler.HandleDoorDiscovered;
            ExplorationVisionCone.OnCorridorPoleVisible += _corridorManager.HandleCorridorPoleVisible;
            ExplorationVisionCone.OnDoorVisibleInRoom += _doorHandler.HandleDoorVisibleInRoom;
            ExplorationVisionCone.OnDiscoveredObject += _floorManager.RegisterDiscoveredObject;

            
            //VerticalConnectorScript
            VerticalConnectorScript.OnConnectorReached  += _floorManager.HandleConnectorReached;
        
        }

        void OnDestroy()
        {
            // FloorManager
            FloorManager.OnAllFloorsCompleted -= OnAllFloorsCompleted;
            FloorManager.OnNewFloorRoomOrExploredCorridor -= StartRotating360;
            FloorManager.OnNextActionNeeded -= DecideNextAction;
            
            // CorridorManager
            CorridorManager.OnRotationRequested -= StartRotating360;
            CorridorManager.OnNextActionNeeded -= DecideNextAction;

            
            // GraphConstruction
            GraphConstruction.OnBacktrackNodeNull -= _floorManager.CheckFloorCompletion;
            GraphConstruction.OnCurrentNodeNull -= DoBacktrack;
            
            // ExplorationNavigation
            ExplorationNavigation.OnStairsTraversed -= _floorManager.ExecuteFloorChange;
            ExplorationNavigation.OnMovingToPole -= _corridorManager.SetPendingCorridorAndPole;
            
            // StateMachine
            StateMachine.OnCurrentNodeNullAfterBacktracking -= _floorManager.CheckFloorCompletion;
            StateMachine.OnNavigationToCorridorEnded -= _corridorManager.ProcessPoleTouched;
            StateMachine.OnPoleTouchedWhileTransiting -= _corridorManager.PoleTouchedWhileTransiting;
            StateMachine.OnPoleTouchedWhileIdle -= _corridorManager.PoleTouchedWhileIdle;
            StateMachine.OnNextActionNeeded -= DecideNextAction;
            StateMachine.OnRotationRequested -= StartRotating360;
            StateMachine.OnRotationCompleted -= EndRotating360;
            StateMachine.OnDoorTraversable -= _floorManager.EnterThroughEdge;
            
            // CorridorPoleScript
            CorridorPoleScript.OnPoleTouched -= _stateMachine.HandlePoleTouched;
            
            // ExplorationVisionCone
            ExplorationVisionCone.OnDoorDiscovered -= _doorHandler.HandleDoorDiscovered;
            ExplorationVisionCone.OnCorridorPoleVisible -= _corridorManager.HandleCorridorPoleVisible;
            ExplorationVisionCone.OnDoorVisibleInRoom -= _doorHandler.HandleDoorVisibleInRoom;
            ExplorationVisionCone.OnDiscoveredObject -= _floorManager.RegisterDiscoveredObject;
            
            //VerticalConnectorScript
            VerticalConnectorScript.OnConnectorReached -= _floorManager.HandleConnectorReached;
        
        }

        // --------------------------------------------------------
        // START
        // --------------------------------------------------------
        void Start()
        {
            if (_expData.buildingGraph == null)
            {
                Debug.LogWarning("[ExplMgr] BuildingGraph is null!");
                return;
            }
    
            // Check if snapshot already exists 
            if (GraphPersistence.Exists(_expData.buildingId))
            {
                BuildingSnapshot snapshot = GraphPersistence.Load(_expData.buildingId);
    
                if (snapshot != null)
                {
                    Debug.Log($"[ExplMgr] Snapshot found for '{_expData.buildingId}': " +
                              $"exploration skipped, transmitting to JaCaMo.");
    
                    var bt = GetComponent<BeliefTransmitter>();
                    bt?.TransmitSnapshot(snapshot);
                    
                    _stateMachine.SetState(State.Completed);
                    return;
                }

                // Start exploration in case of corrupted or unreadable file
                Debug.LogWarning($"[ExplMgr] Snapshot corrupted or unreadable '{_expData.buildingId}':" +
                                 $" restarting exploration.");
            }
    
        
            // No snapshot: start exploration
            FloorNode floorData = _expNav.PrepareExploration();
            if (floorData == null) return;
    
            _expData.currentFloor = floorData.floorIndex;
            StartExploration(floorData.spawnObject.name, floorData.spawnPosition);
        }
        
        // UPDATE
        void Update()
        {
            _stateMachine.Tick();
        }
        
        // ──────────────────────────────────────────────────────────────
        // START EXPLORATION
        // ──────────────────────────────────────────────────────────────
        private void StartExploration(string startNodeId, Vector3 startPos)
        {
            if (_explorationStarted) return;
            _explorationStarted = true;
     

            Debug.Log($"[ExplMgr] Starting floor: {_expData.currentFloor}");
            
            _expData.floorGraphs[_expData.currentFloor] = new TopologicalGraph();
     
            _expData.explorationVisionCone?.SetExplorationMode(true);
            _expData.currentNodeId = startNodeId;
            CorridorPoleScript.OnPoleTouched -= _stateMachine.HandlePoleTouched;
            CorridorPoleScript.OnPoleTouched += _stateMachine.HandlePoleTouched;

            var spawnCorridor = ExplorationUtils.FindCorridorData(startNodeId);
            if (spawnCorridor != null)
            {
                // If the spawn is inside a corridor move to the nearest pole.
                _expNav.BeginMovingToPole(startPos, startNodeId, spawnCorridor);
                _stateMachine.SetState(State.MovingToPole);
            }
            else
            {
                _graphConstruction.AddRoomAndEnterNode(startNodeId, startPos);
                StartRotating360(isRoom: true);
            }

            Debug.Log($"[ExplMgr] Got here from '{startNodeId}' floor {_expData.currentFloor}");
        }

        private void StartRotating360(bool isRoom)
        {
            _expNav.StartRotation(isRoom);
            _stateMachine.SetState(State.Rotating360);

            if (isRoom)
                _expData.registeredRoomDoors.Clear();
            
            _graphConstruction.SetRoomNodeAsPhysicallyVisited(isRoom);
        }
        
        private void EndRotating360()
        {
            _expData.rotationAccum = 0f;
            Debug.Log($"[ExplMgr] 360° completed in '{_expData.currentNodeId}'.");

            if (_expData.currentNodeId != null && _expData.currentNodeId.EndsWith("_B"))
            {
                string corrId = _expData.currentNodeId.Replace("_B", "");
                _graphConstruction.ComputeCorridorOrders(corrId);
            }

            if (_expData.inRoomMode && !string.IsNullOrEmpty(_expData.currentNodeId))
            {
                bool hasUnexploredDoors = !_graphConstruction.IsNodeNull(_expData.currentNodeId, out var currentNode) &&
                                          currentNode.edges.Exists(e => e != null &&
                                                                        e.state != EdgeState.Explored &&
                                                                        e.edgeType == EdgeType.Door);
                if (!hasUnexploredDoors)
                    _roomHelper.ScanRoomDoorsFromScript(_expData.currentNodeId, _floorManager);
            }
        }
        
        private void DecideNextAction()
        {
            if (string.IsNullOrEmpty(_expData.currentNodeId)) return;

            GraphEdge nextEdge = _graphConstruction.RetrieveNextEdgeToInspect();

            if (nextEdge != null)
            {
                _expNav.MoveToNextEdge(nextEdge);
                _stateMachine.SetState(State.MovingToDoor);
                Debug.Log($"[ExplMgr] Next door: {nextEdge.id} navDist={nextEdge.navMeshDist:F1}");
            }
            else
            {
                DoBacktrack();
            }
        }
        
        private void DoBacktrack()
        {
            (string targetNode, GraphNode targetData) = _graphConstruction.BacktrackingLoop();
        
            _stateMachine.DoBacktrack(targetNode, targetData);
        }
        
        // ====================================================
        // END EXPLORATION
        // ====================================================

        private void OnAllFloorsCompleted()
        {
            _expData.explorationVisionCone?.SetExplorationMode(false);
            _stateMachine.SetState(State.Completed);

            // 1. Measure the distance between the rooms
            var ddm = GetComponent<RoomDistanceMap>();
            if (ddm != null)
                ddm.Compute(_expData.floorGraphs);
            else
                Debug.LogWarning("[ExplMgr] GameObject's RoomDistanceMap not found!");

            List<RoomDistancePair> roomPairs = ddm?.pairs ?? new List<RoomDistancePair>();

            // 2. Build Snapshot
            BuildingSnapshot snapshot = GraphSnapshotBuilder.Build(
                _expData.floorGraphs, _expData.ConnectorLinks, roomPairs, _expData.roomArtifacts, _expData.buildingId);

            snapshot.discoveredObjects.AddRange(_expData.artifactData.Values);
            // 3. Save on disk
            bool saved = GraphPersistence.Save(snapshot);
            if (!saved)
                Debug.LogWarning("[ExplMgr] Failed to save snapshot: " +
                                 "transmitting current version to JaCaMo.");
        
            // 4. Save artifacts in a separate JSON file
            ArtifactsPersistence.Save(_expData.buildingId, _expData.roomArtifacts, _expData.artifactData);

            // 6. Send to JaCaMo
            var bt = GetComponent<BeliefTransmitter>();
            bt?.TransmitSnapshot(snapshot);

            // 7. Set CurrentRoom to last Node explored
            var loc = GetComponent<LocalizationTest>();  
            loc?.ForceCurrentRoomAndSend(ExplorationUtils.NormalizeRoomId(_expData.currentNodeId));
            Debug.Log($"[ExplMgr] Building '{_expData.buildingId}' explored. " +
                      $"Floors: {_expData.floorGraphs.Count}  Links: {_expData.ConnectorLinks.Count}  " +
                      $"Distances: {roomPairs.Count}  Snapshot saved: {saved}");
        }
    }
}