using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class WaypointMovement : MovementModel
{
    private int currentWP = 0;
    
    protected GameObject[] waypoints;

    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();       
    }

    public override event Action<string> OnWalkStarted;
    public override event Action<string> OnWalkingTowardsNonFriendStarted;
    
    public GameObject[] Waypoints
    {
        set { waypoints = value; }
    }

    public override void StartWalking()
    {
        StartCoroutine(WalkRoutine());
    }

    public override void HandleWalkMessage(WsMessage message, AgentAvatar agentAvatar = null) 
    {
        print("Agent needs to reach destination.");
        // Avatar receives the type of artifact to reach
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            WalkData data = message.Data.ToObject<WalkData>();
            OnWalkStarted?.Invoke("New destination: " + data.Target);
            OnWalkingTowardsNonFriendStarted?.Invoke(data.Target);
            ReachDestination( data.Target );
            StartCoroutine(RotateTowardsTarget(data.Target));
        });
    }

    private IEnumerator WalkRoutine()
    {
        if (waypoints is not { Length: > 0 }) yield break;
        while (!isStopped)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance 
                                   && (!agent.hasPath || agent.velocity.sqrMagnitude == 0f))
            {
                Walk();
                currentWP++;
                if (currentWP >= waypoints.Length)
                {
                    currentWP = 0;
                    Array.Reverse(waypoints);
                }
            }

            yield return null; // Check every frame
        }
    }

    public void Walk()
    {
        if (!agent.hasPath || agent.velocity.magnitude < 0.1f)
        {            
            // Check if the random position is on the NavMesh    
            agent.SetDestination(waypoints[currentWP].transform.position);            
        }
    }
}
