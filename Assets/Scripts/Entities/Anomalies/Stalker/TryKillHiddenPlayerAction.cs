using System;
using Game.Entities;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

/// <summary>
/// Attempts a hidden kill for closet/under-bed situations when the player is hiding within kill range.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "TryKillHiddenPlayerAction", story: "Agent tries to kill hidden Player", category: "Action", id: "2c6ef5a8b7284ec3901569b6d71b6159")]
public partial class TryKillHiddenPlayerAction : Action
{
	private static readonly int isKilling = Animator.StringToHash("IsKilling");
	private static readonly int killType = Animator.StringToHash("KillType");
	private const string highLookPointName = "HighLookPoint";
	private const string lowLookPointName = "LowLookPoint";

	/// <summary>
	/// Target player object provided by the behavior graph blackboard.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<GameObject> Player;

	/// <summary>
	/// Maximum range for hidden-kill execution.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> KillDistance = new(1.75f);

	/// <summary>
	/// Duration to lock player controls after triggering a hidden kill.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LockDuration = new(4f);

	/// <summary>
	/// Optional camera lock target used for closet-kill animations.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<GameObject> ClosetLookPoint;

	/// <summary>
	/// Optional camera lock target used for under-bed kill animations.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<GameObject> UnderBedLookPoint;

	private Transform cachedTransform;
	private Transform fallbackHighLookPoint;
	private Transform fallbackLowLookPoint;
	private NavMeshAgent navMeshAgent;
	private Animator animator;
	private Collider actorCollider;
	private Rigidbody actorRigidbody;
	private GameObject cachedPlayerObject;
	private Transform cachedPlayerTransform;
	private PlayerMovement cachedPlayerMovement;

	/// <summary>
	/// Evaluates hidden-kill conditions and triggers the kill sequence when valid.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when a hidden kill is executed; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheAgentReferences() || !tryCachePlayerReferences()) { return Status.Failure; }
		if (!cachedPlayerMovement.CheckIfHiding()) { return Status.Failure; }

		float _killDistance = Mathf.Max(0.1f, KillDistance != null ? KillDistance.Value : 1.75f);
		float _killDistanceSqr = _killDistance * _killDistance;
		Vector3 _offset = cachedPlayerTransform.position - cachedTransform.position;
		if (_offset.sqrMagnitude > _killDistanceSqr) { return Status.Failure; }

		int _resolvedKillType = -1;
		Transform _resolvedLookPoint = cachedTransform;
		if (cachedPlayerMovement.CheckIfInCloset())
		{
			_resolvedKillType = 1;
			_resolvedLookPoint = resolveClosetLookPoint();
		}
		else if (cachedPlayerMovement.CheckIfUnderBed())
		{
			_resolvedKillType = 2;
			_resolvedLookPoint = resolveUnderBedLookPoint();
		}

		if (_resolvedKillType < 0) { return Status.Failure; }

		float _lockDuration = Mathf.Max(0.1f, LockDuration != null ? LockDuration.Value : 4f);
		triggerKill(_resolvedKillType, _resolvedLookPoint, _lockDuration);
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
	/// Caches components required by hidden-kill execution.
	/// </summary>
	/// <returns><c>true</c> when all required references are available; otherwise <c>false</c>.</returns>
	private bool tryCacheAgentReferences()
	{
		if (!GameObject) { return false; }

		cachedTransform ??= GameObject.transform;
		navMeshAgent ??= GameObject.GetComponent<NavMeshAgent>();
		animator ??= GameObject.GetComponent<Animator>();
		actorCollider ??= GameObject.GetComponent<Collider>();
		actorRigidbody ??= GameObject.GetComponent<Rigidbody>();
		fallbackHighLookPoint ??= cachedTransform.Find(highLookPointName);
		fallbackLowLookPoint ??= cachedTransform.Find(lowLookPointName);
		return true;
	}

	/// <summary>
	/// Resolves and caches player references from the <c>Player</c> blackboard variable.
	/// </summary>
	/// <returns>
	/// <c>true</c> when a valid player object with <see cref="PlayerMovement"/> is available; otherwise <c>false</c>.
	/// </returns>
	private bool tryCachePlayerReferences()
	{
		GameObject _playerObject = Player?.Value;
		if (!_playerObject) { _playerObject = GameObject.FindGameObjectWithTag("Player"); }
		if (!_playerObject) { return false; }

		if (!ReferenceEquals(cachedPlayerObject, _playerObject))
		{
			cachedPlayerObject = _playerObject;
			cachedPlayerTransform = _playerObject.transform;
			cachedPlayerMovement = _playerObject.GetComponent<PlayerMovement>();
		}

		return cachedPlayerMovement;
	}

	/// <summary>
	/// Executes hidden kill state by stopping movement, selecting animation variant, and locking player control.
	/// </summary>
	private void triggerKill(int _resolvedKillType, Transform _lookPoint, float _lockDuration)
	{
		if (animator && animator.GetBool(isKilling)) { return; }

		if (actorCollider) { actorCollider.enabled = false; }
		if (actorRigidbody) { actorRigidbody.isKinematic = true; }

		if (navMeshAgent)
		{
			navMeshAgent.velocity = Vector3.zero;
			navMeshAgent.isStopped = true;
			navMeshAgent.ResetPath();
		}

		Vector3 _lookDirection = cachedPlayerTransform.position - cachedTransform.position;
		_lookDirection.y = 0f;
		if (_lookDirection.sqrMagnitude > 0.0001f) { cachedTransform.rotation = Quaternion.LookRotation(_lookDirection, Vector3.up); }

		if (animator)
		{
			animator.SetBool(isKilling, true);
			animator.SetInteger(killType, _resolvedKillType);
		}

		cachedPlayerMovement.lockPlayer(_lookPoint ? _lookPoint : cachedTransform, _lockDuration);
	}

	/// <summary>
	/// Resolves the look point for closet-kill camera lock.
	/// </summary>
	/// <returns>Configured closet look point, fallback high look point, or self transform.</returns>
	private Transform resolveClosetLookPoint()
	{
		GameObject _configuredLookPoint = ClosetLookPoint?.Value;
		if (_configuredLookPoint) { return _configuredLookPoint.transform; }
		if (fallbackHighLookPoint) { return fallbackHighLookPoint; }
		return cachedTransform;
	}

	/// <summary>
	/// Resolves the look point for under-bed kill camera lock.
	/// </summary>
	/// <returns>Configured under-bed look point, fallback low look point, or self transform.</returns>
	private Transform resolveUnderBedLookPoint()
	{
		GameObject _configuredLookPoint = UnderBedLookPoint?.Value;
		if (_configuredLookPoint) { return _configuredLookPoint.transform; }
		if (fallbackLowLookPoint) { return fallbackLowLookPoint; }
		return cachedTransform;
	}
}