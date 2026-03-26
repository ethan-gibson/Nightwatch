using System;
using Game.Entities.Octree;
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
	private static readonly int windowEnterStateHash = Animator.StringToHash("WindowEnter");
	private static readonly int windowEnterFullPathHash = Animator.StringToHash("Base Layer.WindowEnter");
	private const string defaultExitTag = "StalkerEnterExit";
	private const float minimumLeaveDuration = 20f;
	private const float exitSampleDistance = 1.25f;
	private const float navMeshAttachDistance = 4f;

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
	public BlackboardVariable<float> MaximumLeaveDuration = new(20f);

	/// <summary>
	/// Maximum time allowed for the leave animation to complete before fallback despawn.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LeaveAnimationTimeout = new(12f);

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
	private PathFindingAgent pathfindingAgent;
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
		pathfindingAgent ??= GameObject.GetComponent<PathFindingAgent>();
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

		if (pathfindingAgent != null)
		{
			pathfindingAgent.Target = null;
			pathfindingAgent.StopMoving();
			pathfindingAgent.SetMovementAuthority(StalkerMovementAuthority.NavMeshLeave);
		}

		if (!prepareNavMeshForLeave()) { return; }

		moveToBestReachableExit();
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

	private bool prepareNavMeshForLeave()
	{
		if (!navMeshAgent) { return false; }
		if (!navMeshAgent.enabled) { navMeshAgent.enabled = true; }

		Vector3 _currentPosition = cachedTransform.position;
		if (!NavMesh.SamplePosition(_currentPosition, out NavMeshHit _navMeshHit, navMeshAttachDistance, NavMesh.AllAreas))
		{
			return false;
		}

		if (!navMeshAgent.Warp(_navMeshHit.position))
		{
			return false;
		}

		navMeshAgent.isStopped = false;
		navMeshAgent.ResetPath();
		navMeshAgent.velocity = Vector3.zero;
		if (navMeshAgent.speed <= 0f) { navMeshAgent.speed = 1f; }

		float _leaveArrivalDistance = Mathf.Max(0.05f, LeaveArrivalDistance != null ? LeaveArrivalDistance.Value : 0.35f);
		if (navMeshAgent.stoppingDistance > _leaveArrivalDistance) { navMeshAgent.stoppingDistance = _leaveArrivalDistance; }
		return navMeshAgent.isOnNavMesh;
	}

	private void moveToBestReachableExit()
	{
		refreshExitPointsIfNeeded();
		if (exitPoints == null || exitPoints.Length == 0 || !navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
		{
			hasExitTargetPosition = false;
			return;
		}

		Vector3 _bestExitPosition = Vector3.zero;
		float _bestPathLength = float.PositiveInfinity;
		bool _foundReachableExit = false;

		for (int _i = 0; _i < exitPoints.Length; _i++)
		{
			GameObject _exitPoint = exitPoints[_i];
			if (!_exitPoint) { continue; }

			if (!NavMesh.SamplePosition(_exitPoint.transform.position, out NavMeshHit _exitHit, exitSampleDistance, NavMesh.AllAreas))
			{
				continue;
			}

			NavMeshPath _candidatePath = new NavMeshPath();
			if (!navMeshAgent.CalculatePath(_exitHit.position, _candidatePath) || _candidatePath.status != NavMeshPathStatus.PathComplete)
			{
				continue;
			}

			float _pathLength = getPathLength(_candidatePath);
			if (_pathLength >= _bestPathLength) { continue; }

			_bestPathLength = _pathLength;
			_bestExitPosition = _exitHit.position;
			_foundReachableExit = true;
		}

		if (!_foundReachableExit)
		{
			hasExitTargetPosition = false;
			navMeshAgent.ResetPath();
			navMeshAgent.isStopped = true;
			return;
		}

		exitTargetPosition = _bestExitPosition;
		hasExitTargetPosition = true;
		navMeshAgent.isStopped = false;
		navMeshAgent.SetDestination(exitTargetPosition);
	}

	private static float getPathLength(NavMeshPath _path)
	{
		if (_path == null || _path.corners == null || _path.corners.Length < 2) { return 0f; }

		float _pathLength = 0f;
		for (int i = 1; i < _path.corners.Length; i++)
		{
			_pathLength += Vector3.Distance(_path.corners[i - 1], _path.corners[i]);
		}

		return _pathLength;
	}

	private void updateLeavingState()
	{
		if (!GameObject) { return; }

		float _maximumLeaveDuration = Mathf.Max(minimumLeaveDuration, MaximumLeaveDuration != null ? MaximumLeaveDuration.Value : minimumLeaveDuration);
		if (!leaveAnimationStarted && leaveTime >= _maximumLeaveDuration)
		{
			destroyInPlace();
			return;
		}

		if (leaveAnimationStarted)
		{
			updateLeaveLocomotionOutputs(_forceIdle: true);

			float _leaveAnimationTimeout = Mathf.Max(1f, LeaveAnimationTimeout != null ? LeaveAnimationTimeout.Value : 12f);
			if (leaveTime >= _leaveAnimationTimeout)
			{
				snapAndDestroy();
				return;
			}

			if (animationEvents != null && animationEvents.ConsumeLeaveAnimationCompleted())
			{
				snapAndDestroy();
				return;
			}

			if (animator != null && isWindowEnterAnimationCompleted())
			{
				snapAndDestroy();
			}

			return;
		}

		bool _readyForLeaveNavigation = navMeshAgent && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;
		if (!_readyForLeaveNavigation && Time.time >= nextLeaveRepathTime)
		{
			nextLeaveRepathTime = Time.time + Mathf.Max(0.1f, LeaveRepathInterval != null ? LeaveRepathInterval.Value : 0.75f);
			if (!prepareNavMeshForLeave()) { return; }
			moveToBestReachableExit();
		}

		if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh) { return; }

		float _leaveRepathInterval = Mathf.Max(0.1f, LeaveRepathInterval != null ? LeaveRepathInterval.Value : 0.75f);
		if (Time.time >= nextLeaveRepathTime)
		{
			nextLeaveRepathTime = Time.time + _leaveRepathInterval;
			if (!hasValidLeavePath())
			{
				moveToBestReachableExit();
			}
		}

		if (!hasReachedExit()) { return; }

		startLeaveAnimation();
	}

	private bool hasValidLeavePath()
	{
		return hasExitTargetPosition
			&& navMeshAgent
			&& navMeshAgent.enabled
			&& navMeshAgent.isOnNavMesh
			&& !navMeshAgent.pathPending
			&& navMeshAgent.hasPath
			&& navMeshAgent.pathStatus == NavMeshPathStatus.PathComplete;
	}

	private bool hasReachedExit()
	{
		if (!hasValidLeavePath()) { return false; }

		float _leaveArrivalDistance = Mathf.Max(0.05f, LeaveArrivalDistance != null ? LeaveArrivalDistance.Value : 0.35f);
		if (navMeshAgent.remainingDistance > _leaveArrivalDistance) { return false; }

		Vector3 _planarPosition = cachedTransform.position;
		_planarPosition.y = 0f;
		Vector3 _planarExitPosition = exitTargetPosition;
		_planarExitPosition.y = 0f;
		return Vector3.Distance(_planarPosition, _planarExitPosition) <= _leaveArrivalDistance;
	}

	private void startLeaveAnimation()
	{
		animationEvents?.SetExternalMovementLock(true);
		navMeshAgent.velocity = Vector3.zero;
		navMeshAgent.isStopped = true;
		leaveAnimationStarted = true;
		leaveTime = 0f;
		updateLeaveLocomotionOutputs(_forceIdle: true);

		if (animator)
		{
			animator.SetBool(leavingHash, true);
			animator.CrossFadeInFixedTime(windowEnterFullPathHash, 0.05f, 0, 0f);
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
		if (!animator || !navMeshAgent || !navMeshAgent.enabled) { return; }

		float _movementSpeed = 0f;
		if (!_forceIdle && !leaveAnimationStarted && !navMeshAgent.isStopped && navMeshAgent.isOnNavMesh)
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

	/// <summary>
	/// Returns true when the animator has reached the end of the window animation state.
	/// </summary>
	private bool isWindowEnterAnimationCompleted()
	{
		if (!animator || animator.IsInTransition(0)) { return false; }

		AnimatorStateInfo _state = animator.GetCurrentAnimatorStateInfo(0);
		bool _isWindowEnter = _state.shortNameHash == windowEnterStateHash || _state.fullPathHash == windowEnterFullPathHash;
		if (!_isWindowEnter) { return false; }

		return _state.normalizedTime >= 0.99f;
	}

	private void snapAndDestroy()
	{
		if (actorCollider) { actorCollider.enabled = false; }

		if (hasExitTargetPosition)
		{
			float _leaveOffset = LeaveSnapHeightOffset != null ? LeaveSnapHeightOffset.Value : 1.3f;
			cachedTransform.position = exitTargetPosition + new Vector3(0f, _leaveOffset, 0f);
		}

		UnityEngine.Object.Destroy(GameObject);
	}

	private void destroyInPlace()
	{
		if (actorCollider) { actorCollider.enabled = false; }
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
