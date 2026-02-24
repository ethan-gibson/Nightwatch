using System;
using Game.Entities;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

/// <summary>
/// Attempts to kill the player when they are within range by stopping the agent, orienting toward the player,
/// triggering kill animation parameters, and locking player control.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "TryKillPlayerAction", story: "Agent tries to kill [Player]", category: "Action", id: "45842b4b35389a221c16348a7f7f26a2")]
public partial class TryKillPlayerAction : Action
{
	private static readonly int isKilling = Animator.StringToHash("IsKilling");
	private static readonly int killType = Animator.StringToHash("KillType");
	private const string defaultLookPointName = "HighLookPoint";
	private const string playerTag = "Player";

	/// <summary>
	/// Target player object provided by the behavior graph blackboard.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<GameObject> Player;

	/// <summary>
	/// Maximum distance at which this action is allowed to trigger a kill.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> KillDistance = new(1.6f);

	/// <summary>
	/// Duration to lock player control after kill is triggered.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LockDuration = new(4f);

	/// <summary>
	/// Optional look point transform for front-kill camera lock.
	/// When unset, this action falls back to the <c>HighLookPoint</c> child and then to self transform.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<GameObject> LookPoint;

	private Transform cachedTransform;
	private NavMeshAgent navMeshAgent;
	private Animator animator;
	private Transform fallbackLookPoint;
	private GameObject cachedPlayerObject;
	private Transform cachedPlayerTransform;
	private PlayerMovement cachedPlayerMovement;

	/// <summary>
	/// Validates range and executes the kill sequence when possible.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when a kill is triggered; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheAgentReferences() || !tryCachePlayerReferences()) { return Status.Failure; }
		if (animator && animator.GetBool(isKilling)) { return Status.Failure; }
		if (cachedPlayerMovement.CheckIfHiding()) { return Status.Failure; }

		float _killDistance = Mathf.Max(0.1f, KillDistance != null ? KillDistance.Value : 1.6f);
		float _killDistanceSqr = _killDistance * _killDistance;
		Vector3 _offset = cachedPlayerTransform.position - cachedTransform.position;
		if (_offset.sqrMagnitude > _killDistanceSqr) { return Status.Failure; }

		float _lockDuration = Mathf.Max(0.1f, LockDuration != null ? LockDuration.Value : 4f);
		triggerKill(_lockDuration);
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
	/// Caches agent-side references used during kill execution.
	/// </summary>
	/// <returns><c>true</c> when the action has a valid graph-owned <see cref="GameObject"/> context.</returns>
	private bool tryCacheAgentReferences()
	{
		if (!GameObject) { return false; }

		cachedTransform ??= GameObject.transform;
		navMeshAgent ??= GameObject.GetComponent<NavMeshAgent>();
		animator ??= GameObject.GetComponent<Animator>();
		fallbackLookPoint ??= cachedTransform.Find(defaultLookPointName);
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
		if (!_playerObject) { _playerObject = cachedPlayerObject; }
		if (!_playerObject) { _playerObject = GameObject.FindGameObjectWithTag(playerTag); }
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
	/// Performs the kill sequence by stopping movement, rotating toward player, setting animator kill state,
	/// and locking player control.
	/// </summary>
	private void triggerKill(float _lockDuration)
	{
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
			animator.SetInteger(killType, 0);
		}

		cachedPlayerMovement.lockPlayer(resolveLookPoint(), _lockDuration);
	}

	/// <summary>
	/// Resolves the transform used by the kill camera lock.
	/// </summary>
	/// <returns>Assigned look point, fallback look point, or self transform in that order.</returns>
	private Transform resolveLookPoint()
	{
		if (tryGetConfiguredLookPoint(out Transform _configuredLookPointTransform)) { return _configuredLookPointTransform; }

		if (!fallbackLookPoint && cachedTransform) { fallbackLookPoint = cachedTransform.Find(defaultLookPointName); }
		if (fallbackLookPoint) { return fallbackLookPoint; }
		return cachedTransform;
	}

	/// <summary>
	/// Safely resolves a configured look point transform from blackboard input.
	/// </summary>
	/// <param name="_lookPointTransform">Resolved transform when available.</param>
	/// <returns><c>true</c> when a valid configured transform is available; otherwise <c>false</c>.</returns>
	private bool tryGetConfiguredLookPoint(out Transform _lookPointTransform)
	{
		_lookPointTransform = null;
		if (LookPoint == null) { return false; }

		try
		{
			GameObject _configuredLookPoint = LookPoint.Value;
			if (!_configuredLookPoint) { return false; }
			_lookPointTransform = _configuredLookPoint.transform;
			return _lookPointTransform != null;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
