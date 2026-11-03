using System;
using System.Collections;
using System.Collections.Generic;
using script.environment;
using UnityEngine;
using UnityEngine.AI;

public abstract class MovementModel : MonoBehaviour
{
    [Tooltip("How closely the agent must face the target, from 0 to 1. " +
             "Higher values require the agent to turn further toward the target.")]
    public float lookAngle = 0.8f;
    
    protected Dictionary<string, Vector3> pointsOfInterestPositions = new Dictionary<string, Vector3>();

    protected NavMeshAgent agent;

    protected PlacesPositionFinder _placesPositionFinder;
    
    public virtual event Action<string> OnWalkStarted;
    public virtual event Action<string> OnWalkingTowardsFriendStarted;
    public virtual event Action<string> OnWalkingTowardsNonFriendStarted;
    public virtual event Action OnDestinationReached;
    public virtual event Action OnTargetedWalkStarted;
    public virtual event Action OnRandomWalkStarted;

    public void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        GameObject envManager = GameObject.Find("envManager");
        _placesPositionFinder = envManager.GetComponent<EnvironmentManagerArtifact>().placesPositionFinder;
    }

    protected bool isStopped = false;
    public bool IsStopped
    {
        set { isStopped = value; }
    }

    public abstract void StartWalking();
    
    public abstract void HandleWalkMessage(WsMessage message, AgentAvatar agentAvatar = null);
    
    
    protected void ReachDestination(string dest)
    {
        agent.isStopped = false;
        
        var destObject = GameObject.Find(dest);
        if (destObject == null)
        {
            Debug.LogWarning($"No object found with name {dest}.");
            LookForPlaceWithPositionFinder(dest);
            return;
        }
        var child = destObject.transform.Find("AnchorPoint");
        if (child == null)
        {
            Debug.LogWarning($"AnchorPoint not found in {destObject.name}");
            LookForPlaceWithPositionFinder(dest);
            return;
        }

        Debug.Log($"Found position for {dest}, navigating to anchor point {child.position}.");
        agent.SetDestination(child.transform.position);
    }

    private void LookForPlaceWithPositionFinder(string dest)
    {
        if (_placesPositionFinder.TryGetPlacePosition(dest, out Vector3 position))
        {
            Debug.Log($"Found position for {dest}.");
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                position = hit.position;
            agent.SetDestination(position);
                
        }
    }
    
    protected IEnumerator RotateTowardsTarget(string target)
    {
        yield return new WaitUntil(() =>
            !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance &&
            (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
        );
        
        GameObject targetObj = GameObject.Find(target);
        if (!targetObj)
        {
            Debug.Log($"Target named '{target}' not found.");
            yield break;
        }
        Vector3 dir = (targetObj.transform.position - transform.position).normalized;
        float dotProd = Vector3.Dot(transform.forward, dir);
        while (dotProd < lookAngle)
        {
            Quaternion lookRotation = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 3.0f);

            dir = (targetObj.transform.position - transform.position).normalized;
            dotProd = Vector3.Dot(transform.forward, dir);
            yield return null;
        }
    }

}
