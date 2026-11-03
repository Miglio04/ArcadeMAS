using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.AI;

namespace script.avatar.MovementModels
{
    public class SocialMovement : WaypointMovement
    {
        public float stoppingDistance = 1.0f;
        public float stoppingDistanceWithFriends = 8.0f;
        
        public override event Action<string> OnWalkStarted;
        public override event Action<string> OnWalkingTowardsFriendStarted;
        public override event Action<string> OnWalkingTowardsNonFriendStarted;
        public override event Action OnDestinationReached;
        public override event Action OnRandomWalkStarted;
        public override event Action OnTargetedWalkStarted;
        
        protected void ResetStoppingDistance()
        {
            agent.stoppingDistance = stoppingDistance;
        }
        
        public override void HandleWalkMessage(WsMessage message, AgentAvatar agentAvatar = null)
        {
            print("[SocialMovement] Message received: " + message.Data);
            // Avatar receives the type of artifact to reach
            WalkData walkData = message.Data.ToObject<WalkData>();

            if (walkData.Target == null) return;
            
            if (walkData.Target == "random")
            {
                UnityMainThreadDispatcher.Instance().Enqueue(HandleRandomWalk);
                return;
            }
                        
            UnityMainThreadDispatcher.Instance().Enqueue(() => { HandleTargetedWalk(walkData, agentAvatar); });
        }
        
        
        private void HandleTargetedWalk(WalkData walkData, [CanBeNull] AgentAvatar agentAvatar)
        {
            OnWalkStarted?.Invoke(walkData.Target);
            IsStopped = true;
            agent.ResetPath();
            OnTargetedWalkStarted?.Invoke();
            
            // If the target is a friend, updates stoppingDistance and uses the correct check
            if (agentAvatar?.IsAgentFriend(walkData.Target) == true)
            {
                agent.stoppingDistance = stoppingDistanceWithFriends;
                ReachDestination(walkData.Target);
                
                OnWalkingTowardsFriendStarted?.Invoke(walkData.Target);
                StartCoroutine(RotateTowardsTarget(walkData.Target));
                if (Mathf.Approximately(agent.stoppingDistance, stoppingDistanceWithFriends))
                {
                    OnDestinationReached?.Invoke();
                }
            }
            else
            {
                ResetStoppingDistance();
                ReachDestination(walkData.Target);
                StartCoroutine(RotateTowardsTarget(walkData.Target));
                OnWalkingTowardsNonFriendStarted?.Invoke(walkData.Target);
            }
        }

        private void HandleRandomWalk()
        {
            ResetStoppingDistance();
            IsStopped = false;
            agent.ResetPath();
            OnWalkStarted?.Invoke("Walking");
            StartWalking();
            
            OnRandomWalkStarted?.Invoke();
        }
    }
}