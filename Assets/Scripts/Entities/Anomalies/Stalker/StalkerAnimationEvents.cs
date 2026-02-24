using Game.Entities;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Minimal animation event receiver for stalker clips.
/// Keeps animation callbacks decoupled from graph AI action logic.
/// </summary>
[DisallowMultipleComponent]
public sealed class StalkerAnimationEvents : MonoBehaviour
{
	private static readonly int leavingHash = Animator.StringToHash("IsLeaving");

	private PlayerMovement cachedPlayerMovement;
	private NavMeshAgent cachedNavMeshAgent;
	private Animator cachedAnimator;
	private bool leaveAnimationCompleted;
	private bool blockMovementUntilEntryAnimationCompletes;
	private bool externalMovementLock;

	private void OnEnable()
	{
		cacheReferences();
		leaveAnimationCompleted = false;
		blockMovementUntilEntryAnimationCompletes = true;
		externalMovementLock = false;

		// Keep agent stationary while spawn window-enter animation plays.
		applyMovementLockIfNeeded();
	}

	private void LateUpdate()
	{
		// Re-apply the lock after graph actions so navigation writes cannot bypass spawn/leave gating.
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
		cachedNavMeshAgent ??= GetComponent<NavMeshAgent>();
		cachedAnimator ??= GetComponent<Animator>();
	}

	private void applyMovementLockIfNeeded()
	{
		cacheReferences();
		if (!cachedNavMeshAgent || !cachedNavMeshAgent.enabled || !cachedNavMeshAgent.isOnNavMesh) { return; }

		if (IsMovementLocked)
		{
			cachedNavMeshAgent.isStopped = true;
			cachedNavMeshAgent.velocity = Vector3.zero;
			return;
		}

		cachedNavMeshAgent.isStopped = false;
		if (cachedNavMeshAgent.speed <= 0f) { cachedNavMeshAgent.speed = 1f; }
	}
}
