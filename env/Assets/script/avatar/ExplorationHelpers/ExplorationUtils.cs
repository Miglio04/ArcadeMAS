using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.AI;

namespace script.avatar.ExplorationHelpers
{
    public class ExplorationUtils : MonoBehaviour
    {
        // QUERY UTILS
        
        [CanBeNull]
        public static GameObject FindRoom(string roomId, [CanBeNull] FloorNode fd) => 
            fd?.roomObjects.Find(r => r != null && r.name == roomId);
        
        public static string FindDoorBetweenNodesByConnector(VerticalConnector conn, string nodeA, string nodeB)
        {
            bool aIsStart = (conn.id == nodeA || conn.triggerStartObject.name == nodeA);
            bool aIsEnd = (conn.id == nodeA || conn.triggerEndObject.name == nodeA);

            //verifica anche che nodeB corrisponda al lato opposto
            bool bMatchesEnd = (conn.id == nodeB || conn.triggerEndObject.name == nodeB);
            bool bMatchesStart = (conn.id == nodeB || conn.triggerStartObject.name == nodeB);
        
            if (aIsStart && bMatchesEnd && conn.triggerEndObject != null)
                return conn.triggerEndObject.name;
            if (aIsEnd && bMatchesStart && conn.triggerStartObject != null)
                return conn.triggerStartObject.name;

            return null;
        }

        public static string FindDoorBetweenNodesByDoor(DoorVarcoScript d, string  nodeA, string nodeB)
        {
            string front = d.roomFront?.name;
            string back = d.roomBack?.name;

            if (front != null && back != null &&
                ((front == nodeA && back == nodeB) || (front == nodeB && back == nodeA)))
            {
                return d.gameObject.name;
            }

            return null;
        }
        
        public static CorridorData FindCorridorData(string id)
        {
            foreach (var c in FindObjectsByType<CorridorData>(FindObjectsSortMode.None))
                if (c.corridorId == id) return c;
            return null;
        }
        
        public static CorridorData FindCorridorContainingPosition(Vector3 pos, float radius)
        {
            foreach (var c in FindObjectsByType<CorridorData>(FindObjectsSortMode.None))
            {
                // La posizione è tra i due poli (proiezione sull'asse del corridoio)
                Vector3 axis = c.Pole2Position - c.Pole1Position;
                float len    = axis.magnitude;
                if (len < 0.01f) continue;
                Vector3 dir  = axis / len;
                float proj   = Vector3.Dot(pos - c.Pole1Position, dir);
                // Distanza laterale dall'asse
                Vector3 onAxis  = c.Pole1Position + dir * Mathf.Clamp(proj, 0f, len);
                float lateralDist = Vector3.Distance(pos, onAxis);
                if (lateralDist <= radius && proj >= -radius && proj <= len + radius)
                    return c;
            }
            return null;
        }
        
        [CanBeNull]
        public static DoorVarcoScript FindDoorScript([CanBeNull] string name)
        {
            if (name == null) return null;
            string cleanName = name;
            if (name.StartsWith("fw_"))   cleanName = name.Substring(3);
            if (name.StartsWith("bw_"))   cleanName = name.Substring(3);

            var go = GameObject.Find(cleanName);
            if (go == null) return null;
            return go.GetComponentInChildren<DoorVarcoScript>();
        }
        
        // GEOMETRIC UTILS
        
        public static string ComputeSideRelativeToCorridorAxis(Vector3 doorPos, Vector3 posA, Vector3 posB)
        {
            Vector3 axis   = (posB - posA).normalized; axis.y = 0f;
            Vector3 toDoor = (doorPos - posA).normalized; toDoor.y = 0f;
            float cross = axis.x * toDoor.z - axis.z * toDoor.x;
            if (Mathf.Abs(cross) < 0.05f) return "RIGHT";
            return cross > 0f ? "LEFT" : "RIGHT";
        }

        public static Vector3 GetRoomCenter(GameObject roomObj)
        {
            if (roomObj == null) return Vector3.zero;

            var col = roomObj.GetComponentInChildren<Collider>();
            if (col != null) return col.bounds.center;

            var rend = roomObj.GetComponentInChildren<Renderer>();
            return rend != null ? rend.bounds.center : roomObj.transform.position;
        }
        
        public static float NavMeshDist(Vector3 a, Vector3 b)
        {
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path)) return -1f;
            if (path.status == NavMeshPathStatus.PathInvalid) return -1f;
            float d = 0f;
            for (int i = 1; i < path.corners.Length; i++)
                d += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return d;
        }

        public static bool IsDoorConnectedToRoom(
            DoorVarcoScript door,
            string roomId,
            out string frontRoom, 
            out string backRoom)
        {
            frontRoom = door.roomFront?.name ?? "";
            backRoom  = door.roomBack?.name  ?? "";
            if (frontRoom != roomId && backRoom != roomId) return false;
            return true;
        }

        // NAMING UTILS
        
        public static string NormalizeRoomId(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return nodeId;
            if (nodeId.EndsWith("_A") || nodeId.EndsWith("_B"))
                return nodeId.Substring(0, nodeId.Length - 2);   // corridoio2_B → corridoio2
            return nodeId;
        }
        
        // DATA UTILS
        
        public static void ResetCorridorPoles(string corridorId)
        {
            foreach (var pole in FindObjectsByType<CorridorPoleScript>(FindObjectsSortMode.None))
            {
                if (pole.corridorId == corridorId)
                    pole.ResetTrigger();
            }
        }
    }
}