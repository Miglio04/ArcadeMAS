using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using script.core.util;
using TMPro;
using UnityEngine;
using WebSocketSharp;

public abstract class AgentAvatarWithEyesAndVoice : AgentAvatar
{
    protected GameObject avatarBody;
    protected GameObject avatarEyes;
    protected TextMeshProUGUI baloonText;

    private Dictionary<string, Action> messageHandlers;
    private Dictionary<string, Action<WsMessage>> messageHandlersWithMessage;
    
    protected override void Awake()
    {
        base.Awake();
        if (Application.IsPlaying(gameObject))
        {
            initializeAvatarWithEyes();
        }
        
        messageHandlers = new Dictionary<string, Action>
        {
            { MessageTypes.WsInitialization, HandleWsInitialization }
        };

        messageHandlersWithMessage = new Dictionary<string, Action<WsMessage>>
        {
            { MessageTypes.Walk, message => movementModel.HandleWalkMessage(message) }
        };

        movementModel.OnWalkStarted += OnWalkStarted;
    }
    
    protected void initializeAvatarWithEyes()
    {
        // Retrieve avatar parts
        avatarBody = transform.Find("Body").gameObject;
        avatarEyes = transform.Find("anchorVisionCone").gameObject;

        // Find the TextMeshPro component in the children of the avatar
        nameTextMeshPro = transform.Find("avatarName").GetComponent<TextMeshPro>();
        nameTextMeshPro.text = name;

        // Initiating the baloon
        baloonText = gameObject.transform.Find("Canvas/BaloonBg/BaloonTxt").GetComponent<TextMeshProUGUI>();
        baloonText.text = "start";
    }


    protected void OnWalkStarted(string message)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() => SetBaloonText(message));
    }
    
    public void SetBaloonText(string message)
    {
        baloonText.text = message;
    }

    public void EnableDisableVisionCone(bool isActive)
    {
        avatarEyes.SetActive(isActive);
    }

    // Unity avatar receives message from jacamo agent
    protected override void OnMessage(object sender, MessageEventArgs e)
    {
        string data = e.Data;
        print("Received message: " + data);
        WsMessage message = null;
        try
        {
            message = JsonConvert.DeserializeObject<WsMessage>(data);
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
        catch (Exception ex)
        {
            print("Error: " + ex.Message);
            print("Message could not be converted.");
        }
    }
    
    
    private void HandleWsInitialization()
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            print("Connection established for " + objInUse.name);
        });
    }
}