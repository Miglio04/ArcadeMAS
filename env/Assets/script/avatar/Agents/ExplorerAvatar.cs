using UnityEngine;

// ============================================================
// ExplorerAvatar.cs  ->  : AgentAvatarSocial
// ============================================================
// Explorer agent: by navigating the different rooms, discovers
// the graph.
// ============================================================

public class ExplorerAvatar : AgentAvatarSocial
{
    protected override void Awake()
    {
        agentType = AgentType == AgentTypeEnum.None ? AgentTypeEnum.Explorer: AgentType;
        JaCaMoAgentClassPath = "artifact.lib.maselements.AgentMasElement";

        base.Awake();
        if (movementModel == null)
            Debug.LogWarning($"[{name}] MovementModel not found.");
        _ = startServer();
    }
    
    protected override void EventSetup() { }

    protected override void EventDestroy() { }
}
