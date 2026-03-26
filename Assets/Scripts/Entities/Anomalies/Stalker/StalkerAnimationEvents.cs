using Game.Entities;
using Game.Entities.Octree;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Minimal animation event receiver for stalker clips.
/// Keeps animation callbacks decoupled from graph AI action logic.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class StalkerAnimationEvents : MonoBehaviour
{
	private static readonly int leavingHash = Animator.StringToHash("IsLeaving");
	private static readonly int windowEnterStateHash = Animator.StringToHash("WindowEnter");
	private static readonly int windowEnterFullPathHash = Animator.StringToHash("Base Layer.WindowEnter");
	private const float entryStateResolveGraceDuration = 0.25f;
	private const float entryLockFallbackDuration = 12f;

	private PlayerMovement cachedPlayerMovement;
	private NavMeshAgent cachedNavMeshAgent;
	private Animator cachedAnimator;
	private PathFindingAgent cachedPathFindingAgent;
	private Transform cachedTransform;
	private bool leaveAnimationCompleted;
	private bool blockMovementUntilEntryAnimationCompletes;
	private bool externalMovementLock;
	private bool hasObservedEntryAnimation;
	private float entryLockStartTime;

	private void OnEnable()
	{
		cacheReferences();
		leaveAnimationCompleted = false;
		blockMovementUntilEntryAnimationCompletes = true;
		externalMovementLock = false;
		hasObservedEntryAnimation = false;
		entryLockStartTime = Time.time;

		applyMovementLockIfNeeded();
	}

	private void LateUpdate()
	{
		updateEntryAnimationLock();
		applyMovementLockIfNeeded();
	}

	/// <summary>
	/// Called by kill animations to trigger player death after lock/cinematic.
	/// </summary>
	public void CallMenuOnAnimEnd()
	{
		if (!cachedPlayerMovement)
		{
			GameObject _player = GameObject.FindGameObjectWithTag("Player");
			cachedPlayerMovement = _player ? _player.GetComponent<PlayerMovement>() : null;
		}

		facePlayerIfAvailable();
		cachedPlayerMovement?.InvokePlayerDeath();
	}

	/// <summary>
	/// Called by enter/leave animations to resume navigation if needed.
	/// </summary>
	public void OnAnimationCompleted()
	{
		cacheReferences();

		if (cachedAnimator && cachedAnimator.GetBool(leavingHash))
		{
			leaveAnimationCompleted = true;
			return;
		}

		blockMovementUntilEntryAnimationCompletes = false;
		applyMovementLockIfNeeded();
	}

	/// <summary>
	/// Returns and clears leave-animation completion status.
	/// </summary>
	/// <returns><c>true</c> once when the leave animation completion event has fired.</returns>
	public bool ConsumeLeaveAnimationCompleted()
	{
		if (!leaveAnimationCompleted) { return false; }
		leaveAnimationCompleted = false;
		return true;
	}

	/// <summary>
	/// True when movement should remain blocked by spawn/leave animation flow.
	/// </summary>
	public bool IsMovementLocked => blockMovementUntilEntryAnimationCompletes || externalMovementLock;

	/// <summary>
	/// Applies or clears an external movement lock used by graph actions.
	/// </summary>
	/// <param name="_locked">Whether to force movement lock.</param>
	public void SetExternalMovementLock(bool _locked)
	{
		cacheReferences();
		externalMovementLock = _locked;
		applyMovementLockIfNeeded();
	}

	private void cacheReferences()
	{
		cachedTransform ??= transform;
		cachedNavMeshAgent ??= GetComponent<NavMeshAgent>();
		cachedAnimator ??= GetComponent<Animator>();
		cachedPathFindingAgent ??= GetComponent<PathFindingAgent>();
	}

	private void facePlayerIfAvailable()
	{
		if (cachedTransform == null || cachedPlayerMovement == null) { return; }

		Transform _playerTransform = cachedPlayerMovement.transform;
		if (_playerTransform == null) { return; }

		Vector3 _lookDirection = _playerTransform.position - cachedTransform.position;
		_lookDirection.y = 0f;
		if (_lookDirection.sqrMagnitude <= 0.0001f) { return; }

		cachedTransform.rotation = Quaternion.LookRotation(_lookDirection, Vector3.up);
	}

	private void applyMovementLockIfNeeded()
	{
		cacheReferences();
		updateMovementAuthority();
		updateRootMotionState();

		if (!cachedNavMeshAgent || !cachedPathFindingAgent || cachedPathFindingAgent.MovementAuthority != StalkerMovementAuthority.NavMeshLeave)
		{
			return;
		}

		if (!cachedNavMeshAgent.enabled || !cachedNavMeshAgent.isOnNavMesh) { return; }

		if (externalMovementLock)
		{
			cachedNavMeshAgent.isStopped = true;
			cachedNavMeshAgent.velocity = Vector3.zero;
			return;
		}

		cachedNavMeshAgent.isStopped = false;
		if (cachedNavMeshAgent.speed <= 0f) { cachedNavMeshAgent.speed = 1f; }
	}

	private void updateMovementAuthority()
	{
		if (!cachedPathFindingAgent || cachedPathFindingAgent.MovementAuthority == StalkerMovementAuthority.NavMeshLeave) { return; }

		if (IsMovementLocked)
		{
			cachedPathFindingAgent.SetMovementAuthority(StalkerMovementAuthority.AnimationLocked);
			return;
		}

		if (cachedPathFindingAgent.MovementAuthority == StalkerMovementAuthority.AnimationLocked)
		{
			cachedPathFindingAgent.SetMovementAuthority(StalkerMovementAuthority.Octree);
		}
	}

	private void updateRootMotionState()
	{
		if (!cachedAnimator) { return; }

		bool _shouldUseRootMotion = shouldUseScriptedRootMotion();
		if (cachedAnimator.applyRootMotion == _shouldUseRootMotion) { return; }

		cachedAnimator.applyRootMotion = _shouldUseRootMotion;
	}

	private void updateEntryAnimationLock()
	{
		if (!blockMovementUntilEntryAnimationCompletes) { return; }

		cacheReferences();
		if (!cachedAnimator)
		{
			blockMovementUntilEntryAnimationCompletes = false;
			return;
		}

		if (cachedAnimator.IsInTransition(0))
		{
			AnimatorStateInfo _nextState = cachedAnimator.GetNextAnimatorStateInfo(0);
			if (isEntryAnimationState(_nextState))
			{
				hasObservedEntryAnimation = true;
			}
			else if (!hasObservedEntryAnimation && Time.time - entryLockStartTime >= entryStateResolveGraceDuration)
			{
				blockMovementUntilEntryAnimationCompletes = false;
			}
			else if (hasObservedEntryAnimation && Time.time - entryLockStartTime >= entryLockFallbackDuration)
			{
				blockMovementUntilEntryAnimationCompletes = false;
			}
			return;
		}

		AnimatorStateInfo _state = cachedAnimator.GetCurrentAnimatorStateInfo(0);
		if (isEntryAnimationState(_state))
		{
			hasObservedEntryAnimation = true;
			if (_state.normalizedTime >= 0.99f)
			{
				blockMovementUntilEntryAnimationCompletes = false;
			}
			return;
		}

		if (!hasObservedEntryAnimation)
		{
			if (Time.time - entryLockStartTime < entryStateResolveGraceDuration) { return; }
			blockMovementUntilEntryAnimationCompletes = false;
			return;
		}

		if (Time.time - entryLockStartTime >= entryLockFallbackDuration)
		{
			blockMovementUntilEntryAnimationCompletes = false;
			return;
		}

		blockMovementUntilEntryAnimationCompletes = false;
	}

	private static bool isEntryAnimationState(AnimatorStateInfo _state)
	{
		return _state.shortNameHash == windowEnterStateHash || _state.fullPathHash == windowEnterFullPathHash;
	}

	private bool shouldUseScriptedRootMotion()
	{
		if (!cachedAnimator) { return false; }

		if (cachedAnimator.IsInTransition(0))
		{
			AnimatorStateInfo _nextState = cachedAnimator.GetNextAnimatorStateInfo(0);
			if (isEntryAnimationState(_nextState)) { return true; }
		}

		AnimatorStateInfo _state = cachedAnimator.GetCurrentAnimatorStateInfo(0);
		if (isEntryAnimationState(_state)) { return true; }

		return cachedAnimator.GetBool(leavingHash) && externalMovementLock;
	}
}
