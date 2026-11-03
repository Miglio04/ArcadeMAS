using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace script.environment
{
    public class NodesPositionFinder :  PlacesPositionFinder
    {
        [Tooltip("Directory containing the *.json files of the graph (graph_snapshots)")]
        public string graphSnapshotsFolder =
            @"%USERPROFILE%\AppData\LocalLow\DefaultCompany\JaCaMoIntegration\graph_snapshots";
    
        private readonly Dictionary<string, Vector3> nodePositions = new Dictionary<string, Vector3>();
        
        public override bool HasPlace(string placeName) => HasNode(placeName);

        public override bool TryGetPlacePosition(string placeName, out Vector3 position) => TryGetNodePosition(placeName, out position);

        void Awake()
        {
            StartCoroutine(LoadNodePositionsWithRetry());
        }
        
        public bool TryGetNodePosition(string nodeId, out Vector3 targetPos) =>
            nodePositions.TryGetValue(nodeId, out targetPos);
    
        public bool HasNode(string nodeId) => nodePositions.ContainsKey(nodeId);
        
        // ========================================================
        // LOADING NODE POSITIONS
        // ========================================================
        private IEnumerator LoadNodePositionsWithRetry()
        {
            while (nodePositions.Count == 0)
            {
                LoadNodePositions();
                if (nodePositions.Count > 0) break;
                Debug.Log("[TopologicalMovement] building.json not yet available, retrying in 3s...");
                yield return new WaitForSeconds(3f);
            }
        }

        private void LoadNodePositions()
        {
            nodePositions.Clear();

            string folder = Environment.ExpandEnvironmentVariables(graphSnapshotsFolder);
            if (!Directory.Exists(folder))
            {
                Debug.Log($"[TopologicalMovement] Graph folder not yet available: {folder}");
                return;
            }

            string buildingFile = null;
            DateTime newest = DateTime.MinValue;

            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                string name = Path.GetFileName(file);
                if (name.Equals("target.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("pathfinder_state.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith("_artifacts.json", StringComparison.OrdinalIgnoreCase)) continue;

                var mtime = File.GetLastWriteTimeUtc(file);
                if (mtime > newest)
                {
                    newest = mtime;
                    buildingFile = file;
                }
            }

            if (buildingFile == null)
            {
                Debug.Log($"[TopologicalMovement] No building*.json available in folder {folder}");
                return;
            }

            Debug.Log($"[TopologicalMovement] Loading node positions from: {buildingFile}");

            string json = File.ReadAllText(buildingFile);
            JObject root = JObject.Parse(json);

            foreach (var floor in root["floors"])
            {
                foreach (var node in floor["nodes"])
                {
                    string nodeType = node["nodeType"]?.ToString();
                    float x = node["x"].Value<float>();
                    float y = node["y"].Value<float>();
                    float z = node["z"].Value<float>();
                    Vector3 pos = new Vector3(x, y, z);

                    if (nodeType == "CorridorPoleA" || nodeType == "CorridorPoleB")
                    {
                        string nodeId = node["nodeId"]?.ToString();
                        string corridorId = node["corridorId"]?.ToString();

                        if (!string.IsNullOrEmpty(nodeId))
                            nodePositions[nodeId] = pos;

                        if (!string.IsNullOrEmpty(corridorId) && !nodePositions.ContainsKey(corridorId))
                            nodePositions[corridorId] = pos;
                    }
                    else
                    {
                        string nodeId = node["nodeId"]?.ToString();
                        if (!string.IsNullOrEmpty(nodeId))
                            nodePositions[nodeId] = pos;
                    }
                }
            }

            Debug.Log($"[TopologicalMovement] Node positions loaded: {nodePositions.Count}");

            // Load missing nodes from pathfinder_state.json
            LoadFromPathfinderState();
        }

        private void LoadFromPathfinderState()
        {
            string path = Path.Combine(
                Environment.ExpandEnvironmentVariables(graphSnapshotsFolder),
                "pathfinder_state.json");
            if (!File.Exists(path)) return;

            var root = JObject.Parse(File.ReadAllText(path));
            foreach (var node in root["nodes"])
            {
                string nodeId = node[0]?.ToString();
                float x = node[3].Value<float>();
                float y = node[4].Value<float>();
                float z = node[5].Value<float>();
                if (!string.IsNullOrEmpty(nodeId) && !nodePositions.ContainsKey(nodeId))
                    nodePositions[nodeId] = new Vector3(x, y, z);
            }
        }
    }
}