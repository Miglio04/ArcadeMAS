using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using script.core.util;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using WebSocketSharp;

public abstract class AgentAvatar : AbstractMasElement
{
    public AgentTypeEnum agentType; 
    public AgentBeliefs agentBeliefs;
    public GameObject[] focusedArtifacts;
    public List<GoalEnum> goals;
    protected TextMeshPro nameTextMeshPro;
    protected NavMeshAgent agent;
    
    private Dictionary<string, Action> messageHandlers;
    private Dictionary<string, Action<WsMessage>> messageHandlersWithMessage;

    protected virtual void Awake()
    {
        initializeWebSocketConnection(OnMessage);
        // Find the TextMeshPro component in the children of the avatar
        nameTextMeshPro = GetComponentInChildren<TextMeshPro>();
        agent = GetComponent<NavMeshAgent>();

        // Check if we found the TextMeshPro component
        if (nameTextMeshPro != null)
        {
            // Set the text of the TextMeshPro to the avatar's name
            nameTextMeshPro.text = name;
        }
        else
        {
            Debug.LogWarning("TextMeshPro component not found in the avatar's children.");
        }

        messageHandlers = new Dictionary<string, Action>
        {
            { MessageTypes.WsInitialization, HandleWsInitialization }
        };

        messageHandlersWithMessage = new Dictionary<string, Action<WsMessage>>
        {
            { MessageTypes.Walk, HandleAgentWalk }
        };
    }
    
    protected void HandleArtifactGrab(GameObject artifact)
    {
        if (artifact != null)
        {
            var artifactSocket = transform.Find("Body/artifactHolder");
            artifact.transform.SetParent(artifactSocket);
            
            artifact.transform.localPosition = Vector3.zero; // Reset position to the socket's position
            artifact.transform.localRotation = Quaternion.identity; // Reset rotation to the socket's rotation
            
            print("Holding artifact: " + artifact.name);
        }
        else
        {
            print("No artifact to hold.");
        }
    }
    
    protected void HandleArtifactRelease(GameObject artifact, GameObject snapPoint)
    {
        if (artifact != null && snapPoint != null)
        {
            artifact.transform.SetParent(null); // Detach from avatar
            
            var surfaceCollider = snapPoint.GetComponent<Collider>();
            var movingRenderer = artifact.GetComponent<Renderer>();
            
            var newPos = snapPoint.transform.position; // XZ position of snap point
            var surfaceTopY = surfaceCollider.bounds.max.y;
            var movingBottomY = movingRenderer.bounds.min.y;
            var yOffset = surfaceTopY - movingBottomY;

            newPos.y += yOffset;
            artifact.transform.position = newPos;
            artifact.transform.rotation = snapPoint.transform.rotation;
            
            print("Released artifact: " + artifact.name + " to snap point: " + snapPoint.name);
        }
        else
        {
            print("No artifact to release or no snap point provided.");
        }
    }

    public GameObject[] FocusedArtifacts
    {
        get => focusedArtifacts;
        set => focusedArtifacts = value;
    }

    public virtual AgentBeliefs AgentBeliefs => agentBeliefs;

    public AgentTypeEnum AgentType => agentType;

    public List<GoalEnum> Goals => goals;

    public string ArtifactToReach { get; set; }


    public string JaCaMoAgentClassPath { get; set; }

    protected void ReachDestination(string dest)
    {
        agent.isStopped = false;


        var destObject = GameObject.Find(dest);
        if (destObject == null)
        {
            Debug.LogError($"Destination is null.");
            return;
        }
        var child = destObject.transform.Find("AnchorPoint");
        if (child == null)
        {
            Debug.LogError($"AnchorPoint not found in {destObject.name}");
            return;
        }

        agent.SetDestination(child.transform.position);
    }

    public void SendMessageToJaCaMoBrain( string message )
    {
        wsChannel.sendMessage(message);
    }


    // Unity avatar receives message from jacamo agent
    protected virtual void OnMessage(object sender, MessageEventArgs e)
    {
        var data = e.Data;
        print("Received message: " + data);
        try
        {
            var message = JsonConvert.DeserializeObject<WsMessage>(data);
            
            if (messageHandlers.TryGetValue(message.Type, out var handler))
            {
                handler.Invoke();
                return;
            }
            
            if (messageHandlersWithMessage.TryGetValue(message.Type, out var handlerWithMessage))
            {
                handlerWithMessage.Invoke(message);
                return;
            }
            
            Debug.LogError("Unknown message type for " + objInUse.name);
        }
        catch (Exception)
        {
            print(data);
            Debug.LogError("Message could not be converted.");
        }
    }
    
    private void HandleWsInitialization()
    {
        print("Connection established for " + objInUse.name);
    }

    private void HandleAgentWalk(WsMessage message)
    {
        print("Agent needs to reach destination.");
        // Avatar receives the type of artifact to reach
        var walkData = message.Data.ToObject<WalkData>();
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            ReachDestination( walkData.Target );
        });
    }
}