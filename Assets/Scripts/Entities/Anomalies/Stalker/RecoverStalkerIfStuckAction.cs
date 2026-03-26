using System;
using Game.Entities.Octree;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;
using Action = Unity.Behavior.Action;

/// <summary>
/// Detects when the stalker is stuck on navigation and forces a repath recovery.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "RecoverStalkerIfStuckAction", story: "Recovers stalker if navigation is stuck", category: "Action", id: "0f8a8f16d9fb4bb48cd67f26a86e1ef4")]
public partial class RecoverStalkerIfStuckAction : Action
{
	/// <summary>
	/// Optional custom delta time. When zero or negative, <see cref="Time.deltaTime"/> is used.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> DeltaTime = new(-1f);

	/// <summary>
	/// Enables or disables stuck recovery checks for this action.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<bool> Enabled = new(true);

	/// <summary>
	/// Velocity magnitude below this threshold counts as stationary.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> StuckVelocityThreshold = new(0.15f);

	/// <summary>
	/// Time the agent must remain stationary before recovery is attempted.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> StuckDuration = new(1.25f);

	/// <summary>
	/// Minimum remaining distance to destination required before stuck recovery can trigger.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> MinRemainingDistanceToConsiderStuck = new(0.8f);

	/// <summary>
	/// Radius used to sample recovery destinations around current or alternate targets.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> RepathRadius = new(1.5f);

	private Transform cachedTransform;
	private NavMeshAgent navMeshAgent;
	private PathFindingAgent pathfindingAgent;
	private float stuckTimer;

	/// <summary>
	/// Runs one stuck-recovery evaluation tick.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when references are valid; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheReferences()) { return Status.Failure; }
		updateRecovery();
		return Status.Success;
	}

	/// <summary>
	/// Completes immediately because this action performs its work during <see cref="OnStart"/>.
	/// </summary>
	/// <returns>Always <see cref="Node.Status.Success"/>.</returns>
	protected override Status OnUpdate()
	{
		return Status.Success;
	}

	/// <summary>
	/// No cleanup is required for this action.
	/// </summary>
	protected override void OnEnd() { }

	/// <summary>
	/// Caches references used by stuck recovery logic.
	/// </summary>
	/// <returns><c>true</c> when the action has a valid graph-owned <see cref="GameObject"/> context.</returns>
	private bool tryCacheReferences()
	{
		if (!GameObject) { return false; }

		cachedTransform ??= GameObject.transform;
		navMeshAgent ??= GameObject.GetComponent<NavMeshAgent>();
		pathfindingAgent ??= GameObject.GetComponent<PathFindingAgent>();
		return navMeshAgent;
	}

	/// <summary>
	/// Applies one recovery step and repaths when the agent has been stuck long enough.
	/// </summary>
	private void updateRecovery()
	{
		if (Enabled != null && !Enabled.Value)
		{
			stuckTimer = 0f;
			return;
		}

		if (pathfindingAgent != null && !pathfindingAgent.IsUsingNavMeshMovement)
		{
			stuckTimer = 0f;
			return;
		}

		if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh || navMeshAgent.isStopped || !navMeshAgent.hasPath || navMeshAgent.pathPending)
		{
			stuckTimer = 0f;
			return;
		}

		float _remainingDistanceThreshold = MinRemainingDistanceToConsiderStuck != null ? MinRemainingDistanceToConsiderStuck.Value : 0.8f;
		_remainingDistanceThreshold = Mathf.Max(_remainingDistanceThreshold, navMeshAgent.stoppingDistance);

		float _velocityThreshold = StuckVelocityThreshold != null ? StuckVelocityThreshold.Value : 0.15f;
		float _velocityThresholdSqr = _velocityThreshold * _velocityThreshold;
		float _desiredVelocitySqrMagnitude = navMeshAgent.desiredVelocity.sqrMagnitude;

		if (navMeshAgent.remainingDistance <= _remainingDistanceThreshold || _desiredVelocitySqrMagnitude <= _velocityThresholdSqr || navMeshAgent.velocity.sqrMagnitude > _velocityThresholdSqr)
		{
			stuckTimer = 0f;
			return;
		}

		float _deltaTime = DeltaTime != null && DeltaTime.Value > 0f ? DeltaTime.Value : Time.deltaTime;
		stuckTimer += Mathf.Max(0f, _deltaTime);

		float _stuckDuration = Mathf.Max(0.1f, StuckDuration != null ? StuckDuration.Value : 1.25f);
		if (stuckTimer < _stuckDuration) { return; }
		stuckTimer = 0f;

		float _repathRadius = Mathf.Max(0.1f, RepathRadius != null ? RepathRadius.Value : 1.5f);
		if (pathfindingAgent != null && pathfindingAgent.TryRecoverMovement(_repathRadius))
		{
			return;
		}

		Vector3 _currentDestination = navMeshAgent.destination;
		navMeshAgent.ResetPath();

		if (NavMesh.SamplePosition(_currentDestination, out NavMeshHit _destinationHit, _repathRadius, NavMesh.AllAreas))
		{
			if (pathfindingAgent != null) { pathfindingAgent.SetDestination(_destinationHit.position); }
			else { navMeshAgent.SetDestination(_destinationHit.position); }
			return;
		}

		Vector2 _randomCircle = Random.insideUnitCircle * _repathRadius;
		Vector3 _alternateDirection = cachedTransform.position + new Vector3(_randomCircle.x, 0f, _randomCircle.y);
		if (NavMesh.SamplePosition(_alternateDirection, out NavMeshHit _alternateHit, _repathRadius, NavMesh.AllAreas))
		{
			if (pathfindingAgent != null) { pathfindingAgent.SetDestination(_alternateHit.position); }
			else { navMeshAgent.SetDestination(_alternateHit.position); }
		}
	}
}
