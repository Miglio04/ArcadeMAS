using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using script.core.model.io;
using script.core.util;
using UnityEngine;
using WebSocketSharp;

public class AgentAvatarSocial : AgentAvatarWithEyesAndVoice
{
    protected MovementModel movementModel;
    protected AvatarAnimationController animationController;
    private Dictionary<string, Action> messageHandlers;
    private Dictionary<string, Action<WsMessage>> messageHandlersWithMessage;

    protected override void Awake()
    {
        base.Awake();
        // Set waypoints to follow
        movementModel = GetComponent<MovementModel>();
        animationController = GetComponentInChildren<AvatarAnimationController>();

        messageHandlers = new Dictionary<string, Action>
        {
            {MessageTypes.WsInitialization,  HandleConnectionMessage},
            {MessageTypes.Stop,  HandleAgentStop},
        };

        messageHandlersWithMessage = new Dictionary<string, Action<WsMessage>>
        {
            {MessageTypes.Walk,  HandleAgentWalk},
            {MessageTypes.Rotate, HandleAgentRotate},
            {MessageTypes.Say, HandleAgentSay},
            {MessageTypes.Grab, HandleAgentGrab},
            {MessageTypes.Release, HandleAgentRelease}
        };

    }


    protected void resetStoppingDistance()
    {
        agent.stoppingDistance = 1.0f;
    }
    
    protected IEnumerator WaitUntilReachedTarget(string target, bool isFriend = false)
    {
        
        // Wait until the agent has a path and is not moving
        yield return new WaitUntil(() =>
            !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance &&
            (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
        );
        
        // Searching for the target object with .Find is not efficient, it would be better to either
        // pass the GameObject directly, cache it when setting the destination, or use tags.
        // However, this should work for now.
        var targetObj = GameObject.Find(target);
        if (targetObj)
            transform.LookAt(targetObj.transform);
        
        Debug.Log($"Agent has reached his {(isFriend ? "friend" : "target")}.");
        var messageType = isFriend ? "reached_friend" : "reached_destination";
        var jacamoJsonString = UnityJacamoIntegrationUtil
            .CreateAndConvertJacamoMessageIntoJsonString("destinationReached", null,
                messageType, null, target);
        SendMessageToJaCaMoBrain(jacamoJsonString);
    }

    protected IEnumerator ActivateVisionCone()
    {
        yield return new WaitForSeconds(4.0f);
        EnableDisableVisionCone(true);
    }

    // Unity avatar receives message from jacamo agent
    protected override void OnMessage(object sender, MessageEventArgs e)
    {
        var data = e.Data;
        print("Received message: " + data);
        try
        {
            var message = JsonConvert.DeserializeObject<WsMessage>(data);
            print("Received Message Type: " + message.Type);

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
            
            print($"Unknown message type for {objInUse.name}");
        }
        catch (Exception ex)
        {
            print($"Error: {ex.Message}" );
            print("Message could not be converted.");
        }
    }

    private void HandleAgentWalk(WsMessage message)
    {
        print("Agent needs to reach destination.");
        // Avatar receives the type of artifact to reach
        WalkData walkData = message.Data.ToObject<WalkData>();

        if (walkData.Target == null) return;
        
        if (walkData.Target == "random")
        {
            UnityMainThreadDispatcher.Instance().Enqueue(HandleRandomWalk);
            return;
        }
                    
        UnityMainThreadDispatcher.Instance().Enqueue(() => { HandleTargetedWalk(walkData); });
    }
    
    private void HandleAgentRotate(WsMessage message)
    {
        var rotateData = message.Data.ToObject<RotateData>();
        HandleRotateMessage(rotateData);
    }

    private void HandleAgentSay(WsMessage message)
    {
        var saysData = message.Data.ToObject<SaysData>();
        UnityMainThreadDispatcher.Instance().Enqueue(() => { HandleSpeechBalloon(saysData); });
    }

    private void HandleAgentGrab(WsMessage message)
    {
        print("Agent needs to grab an artifact.");
        var grabData = message.Data.ToObject<GrabData>();
        UnityMainThreadDispatcher.Instance().Enqueue( () => { HandleArtifactGrab(grabData); } );
    }
    
    private void HandleAgentRelease(WsMessage message)
    {
        print("Agent needs to release an artifact.");
        var releaseData = message.Data.ToObject<ReleaseData>();
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            var artifact = GameObject.Find(releaseData.ArtifactName.Trim('"'));
            var snapPoint = GameObject.Find(releaseData.SnapPointName.Trim('"'));
            HandleArtifactRelease(artifact, snapPoint);
        });
    }
    
    private void HandleTargetedWalk(WalkData walkData)
    {
        SetBaloonText("New destination: " + walkData.Target);
        movementModel.IsStopped = true;
        agent.ResetPath();
        EnableDisableVisionCone(false);
        ReachDestination(walkData.Target);
        
        // If the target is a friend, updates stoppoingDistance and uses the correct check
        if (AgentBeliefs != null && AgentBeliefs.Friends.Contains(walkData.Target))
        {
            agent.stoppingDistance = 8.0f;
            ReachDestination(walkData.Target);
            
                            
            StartCoroutine(WaitUntilReachedTarget(walkData.Target, true));
            if (Mathf.Approximately(agent.stoppingDistance, 8.0f)) //TODO: find correct distance or better solution
            {
                animationController.SetAnimationState("stop");
            }
        }
        else
        {
            resetStoppingDistance(); // stoppingDistance = 1.0f
            ReachDestination(walkData.Target);
            StartCoroutine(WaitUntilReachedTarget(walkData.Target));
        }
    }

    private void HandleRandomWalk()
    {
        resetStoppingDistance();
        movementModel.IsStopped = false;
        agent.ResetPath();
        SetBaloonText("Walking");
        movementModel.StartWalking();
        StartCoroutine(ActivateVisionCone());
                            
        if (animationController != null)
        {
            animationController.SetAnimationState("walk"); // or "stop"
        }
    }

    #region Handlers
    
    private void HandleConnectionMessage()
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            print("Connection established for " + objInUse.name);
        });
    }
    
    private void HandleAgentStop()
    {
        print("Stopping the agent.");
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            SetBaloonText("I'm stopped");
            movementModel.IsStopped = true;
            agent.isStopped = true;
            if (animationController != null)
            {
                animationController.SetAnimationState("stop");
            }
        });
    }
    
    private void HandleRotateMessage(RotateData rotateData)
    {
        if (rotateData.Type == "lookat")
        {
            transform.LookAt(GameObject.Find(rotateData.Target).transform);
            EnableDisableVisionCone(false);
            return;
        }

        print("This rotate is not implemented");
    }
    
    private void HandleSpeechBalloon(SaysData saysData)
    {
        SetBaloonText(saysData.Msg);
        if (animationController != null)
        {
            animationController.SetAnimationState("say");
        }
    }
    
    private void HandleArtifactGrab(GrabData grabData)
    {
        var artifact = GameObject.Find(grabData.Name.Trim('"'));
        HandleArtifactGrab(artifact);
    }
    
    
    
    #endregion
}