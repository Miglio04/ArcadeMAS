using System;
using System.Collections;
using Newtonsoft.Json;
using script.core.util;
using UnityEngine;
using WebSocketSharp;

// ============================================================
// VesnaAvatarBase.cs  ->  : AgentAvatarSocial
// ============================================================
// Base class for Navigator and Explorer agents. Contains only
// logic to handle messages regarding movement.
// ============================================================

public abstract class VesnaAvatarBase : AgentAvatarSocial
{
    protected override void Awake()
    {
        base.Awake();
        if (movementModel == null)
            Debug.LogWarning($"[{name}] MovementModel not found.");
        _ = startServer();
    }

    protected override void EventSetup() { }

    protected override void EventDestroy() { }
    
    protected override void OnMessage(object sender, MessageEventArgs e)
    {
        try
        {
            var message = JsonConvert.DeserializeObject<WsMessage>(e.Data);
            if (message != null && message.Type == MessageTypes.Walk)
            {
                movementModel.HandleWalkMessage(message);
            }
        }
        catch (Exception ex)
        {
           Debug.LogWarning($"[VesnaAvatarBase] Parsing exception: {ex.Message}" +
                            $" | movementModel null: {movementModel == null}" + 
                            $" | StackTrace: {ex.StackTrace}");
        }

        base.OnMessage(sender, e);
    }

}