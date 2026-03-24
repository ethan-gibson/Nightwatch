using System;
using Game.Entities.Octree;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[BlackboardEnum]
public enum NavigateToTargetPositionMode
{
	ClosestPointOnAnyCollider,
	ClosestPointOnTargetCollider,
	ExactTargetPosition
}

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Navigate To Target", story: "[Agent] navigates to [Target]", category: "Action", id: "aaf47a5a8f2148178e4c8c90e6cc7c91")]
public partial class NavigateToOctreeTargetAction : Action
{
	private const float defaultDistanceThreshold = 0.2f;
	private const float targetRepathDistance = 0.25f;
	private const float targetRepathInterval = 0.15f;

	[SerializeReference]
	public BlackboardVariable<GameObject> Agent;
	[SerializeReference]
	public BlackboardVariable<GameObject> Target;
	[SerializeReference]
	public BlackboardVariable<float> Speed = new(1f);
	[SerializeReference]
	public BlackboardVariable<float> DistanceThreshold = new(defaultDistanceThreshold);
	[SerializeReference]
	public BlackboardVariable<string> AnimatorSpeedParam = new("SpeedMagnitude");
	[SerializeReference]
	public BlackboardVariable<float> SlowDownDistance = new(1f);
	[SerializeReference]
	public BlackboardVariable<NavigateToTargetPositionMode> m_TargetPositionMode = new(NavigateToTargetPositionMode.ClosestPointOnAnyCollider);

	private GameObject cachedAgentObject;
	private PathFindingAgent pathfindingAgent;
	private Transform agentTransform;
	private float colliderOffset;
	private Vector3 lastRequestedTargetPosition;
	private bool hasLastRequestedTargetPosition;
	private float nextRepathTime;

	protected override Status OnStart()
	{
		return updateNavigation(true);
	}

	protected override Status OnUpdate()
	{
		return updateNavigation(false);
	}

	protected override void OnEnd()
	{
		if (pathfindingAgent != null)
		{
			pathfindingAgent.StopMoving();
		}
	}

	private Status updateNavigation(bool _forceRepath)
	{
		if (!tryCacheReferences()) { return Status.Failure; }

		if (Speed != null)
		{
			pathfindingAgent.MoveSpeed = Speed.Value;
		}

		Vector3 _targetPosition = getTargetPosition();
		float _distanceThreshold = Mathf.Max(0.01f, DistanceThreshold != null ? DistanceThreshold.Value : defaultDistanceThreshold);
		if (getDistanceXZ(_targetPosition) <= _distanceThreshold + colliderOffset)
		{
			pathfindingAgent.StopMoving();
			return Status.Success;
		}

		if (pathfindingAgent.IsMovementLocked) { return Status.Running; }

		bool _needsRepath = _forceRepath || !pathfindingAgent.IsMoving || !pathfindingAgent.LastRequestSucceeded;
		if (!_needsRepath && hasLastRequestedTargetPosition)
		{
			Vector2 _currentTargetPosition = new Vector2(_targetPosition.x, _targetPosition.z);
			Vector2 _lastTargetPosition = new Vector2(lastRequestedTargetPosition.x, lastRequestedTargetPosition.z);
			float _movedTargetDistanceSqr = (_currentTargetPosition - _lastTargetPosition).sqrMagnitude;
			if (_movedTargetDistanceSqr >= targetRepathDistance * targetRepathDistance && Time.time >= nextRepathTime)
			{
				_needsRepath = true;
			}
		}

		if (_needsRepath)
		{
			bool _requestAccepted = pathfindingAgent.SetDestination(_targetPosition);
			lastRequestedTargetPosition = _targetPosition;
			hasLastRequestedTargetPosition = true;
			nextRepathTime = Time.time + targetRepathInterval;
			if (!_requestAccepted) { return Status.Failure; }
		}

		return Status.Running;
	}

	private bool tryCacheReferences()
	{
		GameObject _agentObject = Agent != null && Agent.Value ? Agent.Value : GameObject;
		if (!_agentObject || Target == null || !Target.Value) { return false; }

		if (!ReferenceEquals(cachedAgentObject, _agentObject))
		{
			cachedAgentObject = _agentObject;
			pathfindingAgent = _agentObject.GetComponent<PathFindingAgent>();
			agentTransform = _agentObject.transform;
			colliderOffset = resolveColliderOffset(_agentObject);
			hasLastRequestedTargetPosition = false;
			nextRepathTime = 0f;
		}

		return pathfindingAgent != null && agentTransform != null;
	}

	private float resolveColliderOffset(GameObject _agentObject)
	{
		Collider _agentCollider = _agentObject.GetComponentInChildren<Collider>();
		if (!_agentCollider) { return 0f; }

		Vector3 _extents = _agentCollider.bounds.extents;
		return Mathf.Max(_extents.x, _extents.z);
	}

	private Vector3 getTargetPosition()
	{
		GameObject _targetObject = Target.Value;
		if (!_targetObject) { return Vector3.zero; }

		NavigateToTargetPositionMode _targetPositionMode = m_TargetPositionMode != null
			? m_TargetPositionMode.Value
			: NavigateToTargetPositionMode.ClosestPointOnAnyCollider;

		switch (_targetPositionMode)
		{
			case NavigateToTargetPositionMode.ClosestPointOnAnyCollider:
			{
				Collider _targetCollider = _targetObject.GetComponentInChildren<Collider>(false);
				if (_targetCollider != null && _targetCollider.enabled)
				{
					return _targetCollider.ClosestPoint(agentTransform.position);
				}
				break;
			}

			case NavigateToTargetPositionMode.ClosestPointOnTargetCollider:
			{
				Collider _targetCollider = _targetObject.GetComponent<Collider>();
				if (_targetCollider != null && _targetCollider.enabled)
				{
					return _targetCollider.ClosestPoint(agentTransform.position);
				}
				break;
			}
		}

		return _targetObject.transform.position;
	}

	private float getDistanceXZ(Vector3 _targetPosition)
	{
		Vector3 _agentPosition = agentTransform.position;
		Vector2 _agentPositionXZ = new Vector2(_agentPosition.x, _agentPosition.z);
		Vector2 _targetPositionXZ = new Vector2(_targetPosition.x, _targetPosition.z);
		return Vector2.Distance(_agentPositionXZ, _targetPositionXZ);
	}
}
