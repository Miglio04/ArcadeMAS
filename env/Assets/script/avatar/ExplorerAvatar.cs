using UnityEngine;

// ============================================================
// ExplorerAvatar.cs  ->  : VesnaAvatarBase
// ============================================================
// Explorer agent: by navigating the different rooms, discovers
// the graph.
// ============================================================

public class ExplorerAvatar : VesnaAvatarBase
{
    protected override void Awake()
    {
        agentType = AgentType == AgentTypeEnum.None ? AgentTypeEnum.Explorer: AgentType;
        JaCaMoAgentClassPath = "artifact.lib.maselements.AgentMasElement";

        base.Awake();
    }
}
