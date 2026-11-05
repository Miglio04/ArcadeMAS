using UnityEngine;
using script.core.model;

public class PlayerAvatarScript : AgentAvatarSocial
{
    public PlayerBeliefs playerBeliefs;
    public GameObject[] waypoints;

    protected override void Awake()
    {
        base.Awake();   
        agentType = AgentType == AgentTypeEnum.None ? AgentTypeEnum.Player: AgentType;
        JaCaMoAgentClassPath = "artifact.lib.maselements.AgentMasElement";
        _ = startServer(); // check if we need to move this one class above
        if (movementModel is WaypointMovement waypointMovement && waypoints is { Length: > 0 }) 
            waypointMovement.Waypoints = waypoints;
    }

    public void handleUserInteraction(){
        var jacamoJsonString = UnityJacamoIntegrationUtil
            .CreateAndConvertJacamoMessageIntoJsonString("userInteraction", 
                null, 
                null, 
                "player", 
                "user");
        
        Debug.Log("Sending user interaction message to JaCaMo brain: " + jacamoJsonString);
        
        SendMessageToJaCaMoBrain(jacamoJsonString);
    }

    public override AgentBeliefs AgentBeliefs => playerBeliefs;
}