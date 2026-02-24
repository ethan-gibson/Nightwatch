using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

/// <summary>
/// Tracks stalker active lifetime and handles leave-map behavior after lifetime expiration.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "TickStalkerLifetimeAction", story: "Ticks stalker lifetime/leave logic", category: "Action", id: "dbfe085fba244e9b810398f02e62a772")]
public partial class TickStalkerLifetimeAction : Action
{
	private static readonly int leavingHash = Animator.StringToHash("IsLeaving");
	private static readonly int velocityAnimatorSpeedParameterHash = Animator.StringToHash("Velocity");
	private static readonly int speedMagnitudeAnimatorSpeedParameterHash = Animator.StringToHash("SpeedMagnitude");
	private const string defaultExitTag = "StalkerEnterExit";

	/// <summary>
	/// Optional custom delta time. When zero or negative, <see cref="Time.deltaTime"/> is used.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> DeltaTime = new(-1f);

	/// <summary>
	/// Total active duration before the stalker transitions into leave mode.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LifeTime = new(20f);

	/// <summary>
	/// Tag used to discover stalker exit points in the scene.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<string> ExitTag = new("StalkerEnterExit");

	/// <summary>
	/// Vertical offset applied when snapping to the final exit position before despawn.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LeaveSnapHeightOffset = new(1.3f);

	/// <summary>
	/// Distance threshold used to consider the agent arrived at the exit before playing leave animation.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LeaveArrivalDistance = new(0.35f);

	/// <summary>
	/// Interval used to retry exit path requests while leaving.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LeaveRepathInterval = new(0.75f);

	/// <summary>
	/// Maximum time allowed in leave mode before force-despawn fallback.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> MaximumLeaveDuration = new(8f);

	/// <summary>
	/// Optional blackboard flag updated with current leave state.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<bool> IsLeaving;

	private Transform cachedTransform;
	private NavMeshAgent navMeshAgent;
	private Animator animator;
	private StalkerAnimationEvents animationEvents;
	private Collider actorCollider;
	private GameObject[] exitPoints;
	private string cachedExitTag;
	private float activeTime;
	private bool isLeaving;
	private float leaveTime;
	private float nextLeaveRepathTime;
	private float lastTickTimestamp;
	private bool hasLastTickTimestamp;
	private Vector3 exitTargetPosition;
	private bool hasExitTargetPosition;
	private bool leaveAnimationStarted;

	/// <summary>
	/// Caches references used by lifecycle ticking and performs the first tick.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when references are valid; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheReferences()) { return Status.Failure; }
		refreshExitPointsIfNeeded();

		float _deltaTime = resolveDeltaTime();
		return tickLifetimeStep(_deltaTime);
	}

	/// <summary>
	/// Advances stalker lifetime and processes leave-map movement/despawn after expiry.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> during normal operation; <see cref="Node.Status.Running"/> while leaving;
	/// otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnUpdate()
	{
		float _deltaTime = resolveDeltaTime();
		return tickLifetimeStep(_deltaTime);
	}

	/// <summary>
	/// No cleanup is required for this action.
	/// </summary>
	protected override void OnEnd() { }

	/// <summary>
	/// Caches stalker-side components needed for lifecycle and leave logic.
	/// </summary>
	/// <returns><c>true</c> when the action has a valid graph-owned <see cref="GameObject"/> context.</returns>
	private bool tryCacheReferences()
	{
		if (!GameObject) { return false; }

		cachedTransform ??= GameObject.transform;
		navMeshAgent ??= GameObject.GetComponent<NavMeshAgent>();
		animator ??= GameObject.GetComponent<Animator>();
		animationEvents ??= GameObject.GetComponent<StalkerAnimationEvents>();
		actorCollider ??= GameObject.GetComponent<Collider>();
		return true;
	}

	/// <summary>
	/// Resolves tick delta time for this update.
	/// </summary>
	/// <returns>
	/// Configured delta time when positive; otherwise elapsed realtime between consecutive ticks.
	/// </returns>
	private float resolveDeltaTime()
	{
		if (DeltaTime != null && DeltaTime.Value > 0f) { return DeltaTime.Value; }

		float _currentTime = Time.time;
		if (!hasLastTickTimestamp)
		{
			hasLastTickTimestamp = true;
			lastTickTimestamp = _currentTime;
			return 0f;
		}

		float _deltaTime = Mathf.Max(0f, _currentTime - lastTickTimestamp);
		lastTickTimestamp = _currentTime;
		return _deltaTime;
	}

	/// <summary>
	/// Runs a single lifecycle update step.
	/// </summary>
	/// <param name="_deltaTime">Time delta used for life progression.</param>
	/// <returns>Current node status after applying lifecycle logic.</returns>
	private Status tickLifetimeStep(float _deltaTime)
	{
		if (isLeaving)
		{
			leaveTime += Mathf.Max(0f, _deltaTime);
			updateLeavingState();
			updateLeaveLocomotionOutputs();
			syncLeaveFlag();
			return Status.Running;
		}

		float _lifeTime = Mathf.Max(0f, LifeTime != null ? LifeTime.Value : 20f);
		if (_lifeTime <= 0f)
		{
			syncLeaveFlag();
			return Status.Success;
		}

		activeTime += Mathf.Max(0f, _deltaTime);
		if (activeTime < _lifeTime)
		{
			syncLeaveFlag();
			return Status.Success;
		}

		beginLeaving();
		updateLeavingState();
		updateLeaveLocomotionOutputs();
		syncLeaveFlag();
		return Status.Running;
	}

	/// <summary>
	/// Starts leave mode and requests navigation to the closest valid exit.
	/// </summary>
	private void beginLeaving()
	{
		if (isLeaving) { return; }
		isLeaving = true;
		leaveTime = 0f;
		nextLeaveRepathTime = 0f;
		leaveAnimationStarted = false;
		hasExitTargetPosition = false;
		animationEvents?.ConsumeLeaveAnimationCompleted();
		animationEvents?.SetExternalMovementLock(false);

		if (animator) { animator.SetBool(leavingHash, false); }
		moveToClosestExit();
	}

	/// <summary>
	/// Refreshes cached exit points when the configured exit tag changes.
	/// </summary>
	private void refreshExitPointsIfNeeded()
	{
		string _exitTag = ExitTag != null ? ExitTag.Value : defaultExitTag;
		if (string.IsNullOrWhiteSpace(_exitTag))
		{
			_exitTag = defaultExitTag;
		}

		if (string.Equals(cachedExitTag, _exitTag, StringComparison.Ordinal) && exitPoints != null) { return; }

		cachedExitTag = _exitTag;
		exitPoints = GameObject.FindGameObjectsWithTag(_exitTag);
	}

	/// <summary>
	/// Sends the agent to the closest available exit point.
	/// </summary>
	private void moveToClosestExit()
	{
		refreshExitPointsIfNeeded();
		if (exitPoints == null || exitPoints.Length == 0) { return; }
		if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh) { return; }

		Transform _closestExit = null;
		float _closestDistanceSqr = float.PositiveInfinity;
		Vector3 _position = cachedTransform.position;

		for (int _i = 0; _i < exitPoints.Length; _i++)
		{
			GameObject _exitPoint = exitPoints[_i];
			if (!_exitPoint) { continue; }

			Vector3 _delta = _exitPoint.transform.position - _position;
			float _distanceSqr = _delta.sqrMagnitude;
			if (_distanceSqr >= _closestDistanceSqr) { continue; }

			_closestDistanceSqr = _distanceSqr;
			_closestExit = _exitPoint.transform;
		}

		if (!_closestExit) { return; }

		exitTargetPosition = _closestExit.position;
		hasExitTargetPosition = true;
		navMeshAgent.isStopped = false;
		if (navMeshAgent.speed <= 0f) { navMeshAgent.speed = 1f; }
		float _leaveArrivalDistance = Mathf.Max(0.05f, LeaveArrivalDistance != null ? LeaveArrivalDistance.Value : 0.35f);
		if (navMeshAgent.stoppingDistance > _leaveArrivalDistance) { navMeshAgent.stoppingDistance = _leaveArrivalDistance; }
		if (NavMesh.SamplePosition(_closestExit.position, out NavMeshHit _exitHit, 1.25f, NavMesh.AllAreas)) { navMeshAgent.SetDestination(_exitHit.position); }
		else { navMeshAgent.SetDestination(_closestExit.position); }
	}

	/// <summary>
	/// Updates leave progress and despawns the stalker once it reaches its exit.
	/// </summary>
	private void updateLeavingState()
	{
		if (!GameObject) { return; }

		if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
		{
			UnityEngine.Object.Destroy(GameObject);
			return;
		}

		float _maximumLeaveDuration = Mathf.Max(1f, MaximumLeaveDuration != null ? MaximumLeaveDuration.Value : 8f);
		if (leaveTime >= _maximumLeaveDuration)
		{
			UnityEngine.Object.Destroy(GameObject);
			return;
		}

		float _leaveRepathInterval = Mathf.Max(0.1f, LeaveRepathInterval != null ? LeaveRepathInterval.Value : 0.75f);
		if (Time.time >= nextLeaveRepathTime)
		{
			nextLeaveRepathTime = Time.time + _leaveRepathInterval;
			if (!leaveAnimationStarted && (!navMeshAgent.hasPath || navMeshAgent.pathStatus != NavMeshPathStatus.PathComplete))
			{
				if (hasExitTargetPosition)
				{
					navMeshAgent.isStopped = false;
					if (navMeshAgent.speed <= 0f) { navMeshAgent.speed = 1f; }
					if (NavMesh.SamplePosition(exitTargetPosition, out NavMeshHit _exitHit, 1.25f, NavMesh.AllAreas)) { navMeshAgent.SetDestination(_exitHit.position); }
					else { navMeshAgent.SetDestination(exitTargetPosition); }
				}
				else { moveToClosestExit(); }
			}
		}

		if (leaveAnimationStarted)
		{
			updateLeaveLocomotionOutputs(_forceIdle: true);
			if (animationEvents != null)
			{
				if (!animationEvents.ConsumeLeaveAnimationCompleted()) { return; }
			}

			snapAndDestroy();
			return;
		}

		if (navMeshAgent.pathPending) { return; }
		if (!navMeshAgent.hasPath) { return; }
		float _leaveArrivalDistance = Mathf.Max(0.05f, LeaveArrivalDistance != null ? LeaveArrivalDistance.Value : 0.35f);
		if (navMeshAgent.remainingDistance > _leaveArrivalDistance) { return; }

		animationEvents?.SetExternalMovementLock(true);
		navMeshAgent.velocity = Vector3.zero;
		navMeshAgent.isStopped = true;
		leaveAnimationStarted = true;
		updateLeaveLocomotionOutputs(_forceIdle: true);

		if (animator)
		{
			animator.SetBool(leavingHash, true);
			if (animationEvents != null) { return; }
		}

		snapAndDestroy();
	}

	/// <summary>
	/// Keeps leave locomotion animation speed aligned with NavMesh movement while leaving.
	/// </summary>
	/// <param name="_forceIdle">When true, forces zero locomotion output.</param>
	private void updateLeaveLocomotionOutputs(bool _forceIdle = false)
	{
		if (!animator || !navMeshAgent) { return; }

		float _movementSpeed = 0f;
		if (!_forceIdle && !leaveAnimationStarted && !navMeshAgent.isStopped)
		{
			float _velocitySqrMagnitude = navMeshAgent.velocity.sqrMagnitude;
			if (_velocitySqrMagnitude <= 0.0001f && navMeshAgent.hasPath && !navMeshAgent.pathPending)
			{
				_velocitySqrMagnitude = navMeshAgent.desiredVelocity.sqrMagnitude;
			}

			if (_velocitySqrMagnitude > 0f)
			{
				_movementSpeed = Mathf.Sqrt(_velocitySqrMagnitude);
			}
		}

		animator.SetFloat(velocityAnimatorSpeedParameterHash, _movementSpeed);
		animator.SetFloat(speedMagnitudeAnimatorSpeedParameterHash, _movementSpeed);
	}

	private void snapAndDestroy()
	{
		if (actorCollider) { actorCollider.enabled = false; }
		float _leaveOffset = LeaveSnapHeightOffset != null ? LeaveSnapHeightOffset.Value : 1.3f;
		cachedTransform.position = navMeshAgent.destination + new Vector3(0f, _leaveOffset, 0f);
		UnityEngine.Object.Destroy(GameObject);
	}

	/// <summary>
	/// Writes the current leave state to the optional blackboard output variable.
	/// </summary>
	private void syncLeaveFlag()
	{
		if (IsLeaving != null) { IsLeaving.Value = isLeaving; }
	}
}
