using UnityEngine;

// ============================================================
// NavigatorAvatar.cs  ->  : VesnaAvatarBase
// ============================================================
// Navigator agent: uses the envManager artifact to move around
// ============================================================

public class NavigatorAvatar : VesnaAvatarBase
{
    protected override void Awake()
    {
        agentType = AgentType == AgentTypeEnum.None ? AgentTypeEnum.Navigator: AgentType;
        JaCaMoAgentClassPath = "artifact.lib.maselements.AgentMasElement";

        base.Awake();
    }
}
