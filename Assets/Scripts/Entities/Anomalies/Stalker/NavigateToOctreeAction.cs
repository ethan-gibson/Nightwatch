using System;
using Game.Entities.Octree;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Logger = Arti.Utilities.Logger;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Navigate to Octree Point", story: "Navigate to [TargetPoint] using octree pathfinding", category: "Action")]
public partial class NavigateToOctreePointAction : Action
{
    private const string playerTag = "Player";
    private const float minMovementDistance = 0.0025f;
    private const float recoverySampleRadius = 1.5f;
    private const int maxRecoveryAttempts = 3;

    [SerializeReference]
    public BlackboardVariable<Vector3> TargetPoint;  // Should be bound to SearchPoint

    public float ArrivalDistance = 0.5f;
    public float MaxStuckTime = 2f;

    private PathFindingAgent pathfindingAgent;
    private GameObject cachedPlayerObject;
    private float stuckTimer;
    private Vector3 lastPosition;
    private int recoveryAttempts;

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

        if (tryTriggerImmediatePlayerKill()) { return Status.Success; }

        if (!pathfindingAgent.SetDestination(TargetPoint.Value))
        {
	        Logger.LogWarning( $"PathfindingAgent failed to set destination to {TargetPoint.Value}. Check if the point is valid and reachable.");
            return Status.Failure;
        }

        resetStuckState(GameObject.transform.position, _resetRecoveryAttempts: true);

        return pathfindingAgent.IsMoving ? Status.Running : Status.Success;
    }

    protected override Status OnUpdate()
    {
        if (tryTriggerImmediatePlayerKill()) { return Status.Success; }

        if (pathfindingAgent.IsMovementLocked)
        {
            resetStuckState(GameObject.transform.position, _resetRecoveryAttempts: false);
            return Status.Running;
        }

        if (pathfindingAgent.IsPathPending)
        {
            resetStuckState(GameObject.transform.position, _resetRecoveryAttempts: false);
            return Status.Running;
        }

        if (!pathfindingAgent.IsMoving)
        {
            return pathfindingAgent.LastRequestSucceeded ? Status.Success : Status.Failure;
        }

        Vector3 _currentPos = GameObject.transform.position;
        float _movementThreshold = Mathf.Max(minMovementDistance, pathfindingAgent.CurrentSpeed * Time.deltaTime * 0.25f);
        if (getPlanarDistanceSqr(_currentPos, lastPosition) < _movementThreshold * _movementThreshold)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= MaxStuckTime)
            {
                if (tryRecoverMovement(_currentPos)) { return Status.Running; }

                Debug.LogWarning("Agent appears stuck. Aborting movement.");
                pathfindingAgent.StopMoving();
                return Status.Failure;
            }
        }
        else
        {
            resetStuckState(_currentPos, _resetRecoveryAttempts: true);
        }

        if (!pathfindingAgent.IsAtDestination(pathfindingAgent.Destination, ArrivalDistance)) { return Status.Running; }
        pathfindingAgent.StopMoving();
        return Status.Success;

    }

    protected override void OnEnd()
    {
        if (pathfindingAgent != null)
            pathfindingAgent.StopMoving();
    }

    private bool tryTriggerImmediatePlayerKill()
    {
        if (pathfindingAgent == null || pathfindingAgent.Target == null) { return false; }

        GameObject _playerObject = resolvePlayerObject();
        if (!_playerObject) { return false; }

        Transform _playerTransform = _playerObject.transform;
        Transform _targetTransform = pathfindingAgent.Target;
        if (_playerTransform == null || _targetTransform == null) { return false; }
        if (_targetTransform != _playerTransform && !_targetTransform.IsChildOf(_playerTransform)) { return false; }

        return StalkerKillUtility.TryTriggerVisibleKill(
            GameObject,
            _playerObject,
            StalkerKillUtility.DefaultVisibleKillDistance,
            StalkerKillUtility.DefaultKillLockDuration);
    }

    private GameObject resolvePlayerObject()
    {
        if (cachedPlayerObject) { return cachedPlayerObject; }

        cachedPlayerObject = GameObject.FindGameObjectWithTag(playerTag);
        return cachedPlayerObject;
    }

    private bool tryRecoverMovement(Vector3 _currentPos)
    {
        if (recoveryAttempts >= maxRecoveryAttempts) { return false; }
        if (!pathfindingAgent.TryRecoverMovement(recoverySampleRadius)) { return false; }

        recoveryAttempts++;
        resetStuckState(_currentPos, _resetRecoveryAttempts: false);
        Debug.LogWarning("Agent appears stuck. Repathing movement.");
        return true;
    }

    private void resetStuckState(Vector3 _currentPos, bool _resetRecoveryAttempts)
    {
        stuckTimer = 0f;
        lastPosition = _currentPos;
        if (_resetRecoveryAttempts)
        {
            recoveryAttempts = 0;
        }
    }

    private static float getPlanarDistanceSqr(Vector3 _a, Vector3 _b)
    {
        float _deltaX = _a.x - _b.x;
        float _deltaZ = _a.z - _b.z;
        return (_deltaX * _deltaX) + (_deltaZ * _deltaZ);
    }
}
