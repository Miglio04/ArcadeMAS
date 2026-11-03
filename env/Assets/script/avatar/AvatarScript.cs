using UnityEngine;

public class AvatarScript : AgentAvatarSocial
{ 
    public GameObject[] waypoints;

    protected override void Awake()
    {
        base.Awake();
        JaCaMoAgentClassPath = "artifact.lib.maselements.AgentMasElement";
        
        if (movementModel is WaypointMovement waypointMovement && waypoints is { Length: > 0 }) 
            waypointMovement.Waypoints = waypoints;
    }
}
