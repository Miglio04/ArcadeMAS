using System;
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
    protected TopologicalMovement topoMover;

    protected override void Awake()
    {
        base.Awake();
        topoMover = movementModel as TopologicalMovement;
        if (topoMover == null)
            Debug.LogWarning($"[{name}] TopologicalMovement not found: " +
                             "node-to-node navigation will not be available.");
        _ = startServer();
    }

    protected override void OnMessage(object sender, MessageEventArgs e)
    {
        try
        {
            var message = JsonConvert.DeserializeObject<WsMessage>(e.Data);
            if (message != null && message.Type == MessageTypes.Walk)
            {
                var innerType = message.Data?["type"]?.ToString();
                if (innerType == "goto_pos" && topoMover != null)
                {
                    var d = message.Data;
                    float px = (float)d["x"];
                    float py = (float)d["y"];
                    float pz = (float)d["z"];
                    string label = (string)d["target"];
                    UnityMainThreadDispatcher.Instance().Enqueue(() => topoMover.GoToPosition(px, py, pz, label));
                    return;
                }
                var walkData = message.Data.ToObject<WalkData>();
                 Debug.Log($"[VesnaAvatarBase] Walking towards '{walkData?.Target}', " +
                           $"topoMover null: {topoMover == null}, HasNode: {topoMover?.HasNode(walkData?.Target)}");
                if (walkData != null
                    && !string.IsNullOrEmpty(walkData.Target)
                    && walkData.Target != "random"
                    && topoMover != null
                    && topoMover.HasNode(walkData.Target))
                {
                    string node = walkData.Target;
                    UnityMainThreadDispatcher.Instance().Enqueue(() => topoMover.GoToNode(node));
                    return;
                }
            }
        }
        catch (Exception ex)
        {
           Debug.LogWarning($"[VesnaAvatarBase] Parsing exception: {ex.Message}" +
                            $" | topoMover null: {topoMover == null}");
        }

        base.OnMessage(sender, e);
    }

}