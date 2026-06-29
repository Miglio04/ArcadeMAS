using Newtonsoft.Json;
using System;
using WebSocketSharp;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using script.core.model;
using Unity.VisualScripting;

public class PlayerAvatarScript : AgentAvatarSocial
{
    public PlayerBeliefs playerBeliefs;
    private AutonomousWalking autonomousWalking;
    public GameObject[] waypoints;

    protected override void Awake()
    {
        base.Awake();   
        agentFile = string.IsNullOrEmpty(agentFile) ? "player.asl" : agentFile;
        JaCaMoAgentClassPath = "artifact.lib.maselements.AgentMasElement";
        autonomousWalking = (AutonomousWalking) movementModel;
        if (waypoints is { Length: > 0 })
            autonomousWalking.Waypoints = waypoints;
    }

    public void handleUserInteraction(){
        var jacamoJsonString = UnityJacamoIntegrationUtil
            .CreateAndConvertJacamoMessageIntoJsonString("userInteraction", null, null, "player", "user");
        
        Debug.Log("Sending user interaction message to JaCaMo brain: " + jacamoJsonString);
        
        SendMessageToJaCaMoBrain(jacamoJsonString);
    }

    public override AgentBeliefs AgentBeliefs => playerBeliefs;
}