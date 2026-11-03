using System;
using UnityEngine;
using UnityEngine.AI;
using script.environment;


public class TopologicalMovement : MovementModel
{
    [Header("Config")]
    [Tooltip("Range in which an INTERMEDIATE node is considered 'reached': whenever " +
             "its crossed, notify the arrival and start pointing towards the next. " +
             "Increase it to pass wider, decrease it to pass closer to the node.")]
    public float arrivalThreshold = 1.5f;

    [Tooltip("Range in which the agent is considered near the FINAL OBJECT " +
             "This value should be lower then arrivalThreshold, as we want the agent to stop near the final object.")]
    public float objectArrivalThreshold = 0.6f;

    private string currentTargetNode = null;
    private string finalApproachLabel = null;
    private Vector3 currentTargetPos;
    private bool isMoving = false;

    private NodesPositionFinder _nodesPositionFinder;
    
    public override event Action<string> OnWalkStarted;
    public override event Action OnTargetedWalkStarted;
    public override event Action<string> OnWalkingTowardsNonFriendStarted;
    public override event Action OnDestinationReached;


    void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        _nodesPositionFinder =  GameObject.Find("envManager").GetComponent<NodesPositionFinder>();
        
    }

    public override void HandleWalkMessage(WsMessage message, AgentAvatar agentAvatar = null)
    {
        OnWalkStarted?.Invoke("Started walking");
        var innerType = message.Data?["type"]?.ToString();
        if (innerType == "goto_pos")
        {
            OnTargetedWalkStarted?.Invoke();
            var d = message.Data;
            OnWalkingTowardsNonFriendStarted?.Invoke((string)d["target"]);
            float px = (float)d["x"];
            float py = (float)d["y"];
            float pz = (float)d["z"];
            string label = (string)d["target"];
            UnityMainThreadDispatcher.Instance().Enqueue(() => GoToPosition(px, py, pz, label));
            return;
        }
        var walkData = message.Data.ToObject<WalkData>();
        Debug.Log($"[VesnaAvatarBase] Walking towards '{walkData?.Target}', " +
                  $" HasNode: {_nodesPositionFinder.HasNode(walkData?.Target)}");
        if (walkData != null
            && !string.IsNullOrEmpty(walkData.Target)
            && walkData.Target != "random")
        {
            OnTargetedWalkStarted?.Invoke();
            OnWalkingTowardsNonFriendStarted?.Invoke(walkData.Target);
            if (_nodesPositionFinder.HasNode(walkData.Target))
            {
                string node = walkData.Target;
                UnityMainThreadDispatcher.Instance().Enqueue(() => GoToNode(node));
            } else
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    if (!TryResolveArtifactPosition(walkData.Target, out Vector3 artifactPos)) return;
                    MoveToTarget(walkData.Target, artifactPos, true);
                    StartCoroutine(RotateTowardsTarget(walkData.Target));
                });
            }
        }
    }

    // Agents using this MovementModel can't roam, for now, so the random/waypoint movement is disabled
    public override void StartWalking()
    {
        isStopped = false;
        if (agent != null) agent.isStopped = false;
    }
    
    // --------------------------------------------------------
    // Method called when the brain of the avatar sends a "go_to" message
    // --------------------------------------------------------
    private void GoToNode(string nodeId)
    {
        if (!_nodesPositionFinder.TryGetNodePosition(nodeId, out var targetPos))
        {
            Debug.LogWarning($"[TopologicalMovement] Couldn't find '{nodeId}' within the loaded positions.");
            return;
        }
        
        // Re-enable movement in case a targeted walk toward an artifact
        // had disabled the MovementModel.
        enabled = true;
        isStopped = false;
        if (agent != null) agent.isStopped = false;

        currentTargetNode = nodeId;
        currentTargetPos = targetPos;
        isMoving = true;
        agent.SetDestination(targetPos);
        Debug.Log($"[TopologicalMovement] Moving towards {nodeId} @ {targetPos}");
    }
    // Final approach to a free POSITION (object center), not to a node.
    // 'label' is what will be reported as reached(place, label) on arrival.
    private void GoToPosition(float x, float y, float z, string label)
    {
        Vector3 targetPos = new Vector3(x, y, z);

        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            targetPos = hit.position;
        else
            Debug.LogWarning($"[TopologicalMovement] Couldn't project the position of '{label}' " +
                             $"onto the NavMesh Area. Trying to use the raw position.");

        enabled = true;
        isStopped = false;
        if (agent != null) agent.isStopped = false;

        currentTargetNode  = label;
        currentTargetPos   = targetPos;
        finalApproachLabel = label;
        isMoving = true;
        agent.SetDestination(targetPos);
        Debug.Log($"[TopologicalMovement] Nearing '{label}' @ {targetPos}");
    }
    private void MoveToTarget(string label, Vector3 targetPos, bool isArtifact)
    {
        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            targetPos = hit.position;

        enabled = true;
        isStopped = false;
        if (agent != null) agent.isStopped = false;

        currentTargetNode = label;
        currentTargetPos = targetPos;
        finalApproachLabel = isArtifact ? label : null;
        isMoving = true;
        
        agent.SetDestination(targetPos);

        Debug.Log(isArtifact
            ? $"[TopologicalMovement] Nearing artifact '{label}'"
            : $"[TopologicalMovement] Moving towards node '{label}'");
    }
    private bool TryResolveArtifactPosition(string artifactId, out Vector3 pos)
    {
        GameObject obj = GameObject.Find(artifactId);

        if (obj != null && obj.layer != LayerMask.NameToLayer("RoomNode"))
        {
            if (obj.transform.Find("AnchorPoint") != null)
                obj = obj.transform.Find("AnchorPoint").gameObject;
            pos = obj.transform.position;
            return true;
        }

        pos = Vector3.zero;
        return false;
    }
    void Update()
    {
        if (!isMoving || currentTargetNode == null) return;
        if (agent.pathPending) return;

        float dist = Vector3.Distance(transform.position, currentTargetPos);
        
        float threshold = (finalApproachLabel != null) ? objectArrivalThreshold : arrivalThreshold;

        bool passedThrough = dist <= threshold;
        bool fullyStopped  = agent.remainingDistance <= agent.stoppingDistance
                             && agent.velocity.sqrMagnitude < 0.01f;

        if (passedThrough || fullyStopped)
        {
            string reached = currentTargetNode;
            bool wasFinalApproach = finalApproachLabel != null;

            isMoving = false;
            currentTargetNode = null;
            finalApproachLabel = null;

            if (wasFinalApproach && agent != null)
            {
                OnDestinationReached?.Invoke();
                agent.isStopped = true;
            }

            var bridge = GetComponent<AgentJacamoBridge>();
            if (bridge != null)
            {
                if (wasFinalApproach)
                    bridge.SendObjectReached(reached);     // reached(place, Artifact) only, NO current_room
                else
                    bridge.SendMovementCompleted(reached); // reached + current_room
            }
            else
                Debug.LogWarning("[TopologicalMovement] No AgentJacamoBridge found: can't report arrival.");}
    }


}