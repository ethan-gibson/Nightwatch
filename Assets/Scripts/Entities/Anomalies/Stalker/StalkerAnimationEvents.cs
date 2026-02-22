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
		cachedNavMeshAgent ??= GetComponent<NavMeshAgent>();
		cachedAnimator ??= GetComponent<Animator>();

		if (cachedNavMeshAgent && cachedNavMeshAgent.enabled && cachedNavMeshAgent.isOnNavMesh)
		{
			cachedNavMeshAgent.isStopped = false;
			if (cachedNavMeshAgent.speed <= 0f) { cachedNavMeshAgent.speed = 1f; }
		}

		if (cachedAnimator) { cachedAnimator.SetBool(leavingHash, false); }
	}
}