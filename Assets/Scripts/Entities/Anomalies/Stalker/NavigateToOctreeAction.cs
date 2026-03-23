using System;
using Game.Entities.Octree;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Navigate to Octree Point", story: "Navigate to [TargetPoint] using octree pathfinding", category: "Action")]
public partial class NavigateToOctreePointAction : Action
{
    [SerializeReference]
    public BlackboardVariable<Vector3> TargetPoint;  // Should be bound to SearchPoint

    public float ArrivalDistance = 0.5f;
    public float MaxStuckTime = 2f;

    private PathFindingAgent pathfindingAgent;
    private float stuckTimer;
    private Vector3 lastPosition;

    protected override Status OnStart()
    {
        if (!GameObject.TryGetComponent(out pathfindingAgent))
        {
            Debug.LogError("NavigateToOctreePointAction requires a PathfindingAgent on the same GameObject.");
            return Status.Failure;
        }

        if (TargetPoint == null)
        {
            Debug.LogWarning("TargetPoint is not set in the behavior tree.");
            return Status.Failure;
        }

        pathfindingAgent.SetDestination(TargetPoint.Value);
        stuckTimer = 0f;
        lastPosition = GameObject.transform.position;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (!pathfindingAgent.IsMoving)
        {
            return pathfindingAgent.HasPath ? Status.Success : Status.Failure;
        }

        Vector3 _currentPos = GameObject.transform.position;
        if (Vector3.Distance(_currentPos, lastPosition) < 0.01f)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= MaxStuckTime)
            {
                Debug.LogWarning("Agent appears stuck. Aborting movement.");
                pathfindingAgent.StopMoving();
                return Status.Failure;
            }
        }
        else
        {
            stuckTimer = 0f;
            lastPosition = _currentPos;
        }

        if (!(Vector3.Distance(_currentPos, pathfindingAgent.Destination) < ArrivalDistance)) { return Status.Running; }
        pathfindingAgent.StopMoving();
        return Status.Success;

    }

    protected override void OnEnd()
    {
        if (pathfindingAgent != null)
            pathfindingAgent.StopMoving();
    }
}