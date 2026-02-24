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

	private void OnEnable()
	{
		cacheReferences();
		leaveAnimationCompleted = false;

		// Keep agent stationary while spawn window-enter animation plays.
		if (cachedNavMeshAgent && cachedNavMeshAgent.enabled && cachedNavMeshAgent.isOnNavMesh) { cachedNavMeshAgent.isStopped = true; }
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

		if (cachedNavMeshAgent && cachedNavMeshAgent.enabled && cachedNavMeshAgent.isOnNavMesh)
		{
			cachedNavMeshAgent.isStopped = false;
			if (cachedNavMeshAgent.speed <= 0f) { cachedNavMeshAgent.speed = 1f; }
		}
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

	private void cacheReferences()
	{
		cachedNavMeshAgent ??= GetComponent<NavMeshAgent>();
		cachedAnimator ??= GetComponent<Animator>();
	}
}
