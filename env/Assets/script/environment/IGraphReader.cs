using UnityEngine;

namespace script.environment
{
    public interface IGraphReader
    {
        
        string CurrentNodeId { get; }
        
        public Vector3 GetNodePosition(string nodeId);

        public bool IsCorridorFullyTransited(string nodeId);

        public bool IsNodeOfType(string nodeId, NodeType nodeType);

        public bool IsNodePhysicallyVisited(string nodeId);

        public bool IsNodeNull(string nodeId);
        
        public bool HasCorridorAlreadyBeenExplored(string corridorId);

        public bool HasRoomAlreadyBeenExplored(string currentRoomId, string roomId, string doorId);
        
        public bool HasUnexploredEdges(string nodeId);

        public string FindNearestNodeWithDiscoveredEdges(Vector3 position);
        
        public bool HasUnexploredDoors(string nodeId);
    }
}