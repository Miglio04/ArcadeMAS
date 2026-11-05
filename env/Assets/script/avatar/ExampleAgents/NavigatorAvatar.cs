using UnityEngine;

// ============================================================
// NavigatorAvatar.cs  ->  : AgentAvatarSocial
// ============================================================
// Navigator agent: uses the envManager artifact to move around
// ============================================================

public class NavigatorAvatar : AgentAvatarSocial
{
    protected override void Awake()
    {
        agentType = AgentType == AgentTypeEnum.None ? AgentTypeEnum.Navigator: AgentType;
        JaCaMoAgentClassPath = "artifact.lib.maselements.AgentMasElement";

        base.Awake();
        if (movementModel == null)
            Debug.LogWarning($"[{name}] MovementModel not found.");
        _ = startServer();
    }
    
    protected override void EventSetup() { }

    protected override void EventDestroy() { }
}
