using System;
using Game.Entities;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

/// <summary>
/// Updates stalker perception blackboard values using triple-ray visibility checks against player points.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "UpdateStalkerPerceptionAction", story: "Updates stalker perception blackboard", category: "Action", id: "d4300f07e6f54cde96af69153fd45af9")]
public partial class UpdateStalkerPerceptionAction : Action
{
	private const string playerTag = "Player";
	private const string playerVariableName = "Player";
	private const string playerVisibleVariableName = "PlayerVisible";
	private const string lastKnownPositionVariableName = "LastKnownPosition";
	private const string timeSinceSeenVariableName = "TimeSinceSeen";
	private const string lostSightRecentlyVariableName = "LostSightRecently";
	private const string lostSightMemoryVariableName = "LostSightMemory";
	private const float defaultLostSightMemory = 4f;
	private const float defaultSightRange = 10f;
	private const float defaultVisionWidth = 45f;
	private static readonly Vector3 defaultVisionOriginOffset = new(0f, 1.6f, 0f);

	/// <summary>
	/// Optional custom delta time. When zero or negative, <see cref="Time.deltaTime"/> is used.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> DeltaTime = new(-1f);

	/// <summary>
	/// Maximum distance used for visibility checks.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> SightRange = new(10f);

	/// <summary>
	/// Horizontal field-of-view angle used for visibility checks.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> VisionWidth = new(45f);

	/// <summary>
	/// World-space offset from stalker origin used as the eye position.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<Vector3> VisionOriginOffset = new(new Vector3(0f, 1.6f, 0f));

	/// <summary>
	/// Fallback head offset used when player collider bounds are unavailable.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> PlayerHeadOffset = new(1.6f);

	/// <summary>
	/// Fallback torso offset used when player collider bounds are unavailable.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> PlayerTorsoOffset = new(1.0f);

	/// <summary>
	/// Fallback leg offset used when player collider bounds are unavailable.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> PlayerLegOffset = new(0.35f);

	/// <summary>
	/// Layer mask used by visibility raycasts.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<int> VisionRaycastMask = new(~0);

	/// <summary>
	/// If true, trigger colliders are ignored during visibility raycasts.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<bool> IgnoreTriggerColliders = new(true);

	/// <summary>
	/// If true, writes null into the <c>Player</c> blackboard variable when not visible.
	/// This lets chase navigation fail immediately and self-abort out of chase.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<bool> SetPlayerVariableToNullWhenNotVisible = new(true);

	/// <summary>
	/// Fallback memory duration for <c>LostSightRecently</c> when no graph variable exists.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> LostSightMemoryFallback = new(4f);

	private Transform cachedTransform;
	private BehaviorGraphAgent graphAgent;
	private BehaviorGraph cachedGraph;
	private BlackboardVariable<GameObject> playerVariable;
	private BlackboardVariable<bool> playerVisibleVariable;
	private BlackboardVariable<Vector3> lastKnownPositionVariable;
	private BlackboardVariable<float> timeSinceSeenVariable;
	private BlackboardVariable<bool> lostSightRecentlyVariable;
	private BlackboardVariable<float> lostSightMemoryVariable;
	private GameObject cachedPlayerObject;
	private Transform cachedPlayerTransform;
	private PlayerMovement cachedPlayerMovement;
	private Collider cachedPlayerCollider;
	private readonly Vector3[] playerVisibilityPoints = new Vector3[3];
	private float cachedVisionWidth = float.NaN;
	private float cachedCosHalfFov;

	/// <summary>
	/// Caches references and applies one perception tick.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when blackboard values are updated; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheReferences() || !tryResolvePlayer()) { return Status.Failure; }

		float _deltaTime = resolveDeltaTime();
		bool _playerVisible = evaluatePlayerVisible();
		float _timeSinceSeen = Mathf.Max(0f, timeSinceSeenVariable.Value);

		if (_playerVisible)
		{
			lastKnownPositionVariable.Value = cachedPlayerTransform.position;
			_timeSinceSeen = 0f;
		}
		else { _timeSinceSeen += _deltaTime; }

		timeSinceSeenVariable.Value = _timeSinceSeen;

		float _lostSightMemory = LostSightMemoryFallback != null ? LostSightMemoryFallback.Value : defaultLostSightMemory;
		if (lostSightMemoryVariable != null) { _lostSightMemory = lostSightMemoryVariable.Value; }
		_lostSightMemory = Mathf.Max(0.1f, _lostSightMemory);

		playerVisibleVariable.Value = _playerVisible;
		lostSightRecentlyVariable.Value = !_playerVisible && _timeSinceSeen <= _lostSightMemory;

		if (SetPlayerVariableToNullWhenNotVisible != null && SetPlayerVariableToNullWhenNotVisible.Value) { playerVariable.Value = _playerVisible ? cachedPlayerObject : null; }
		else { playerVariable.Value = cachedPlayerObject; }

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
	/// Caches graph, component, and required blackboard variable references.
	/// </summary>
	/// <returns><c>true</c> when all required references are valid; otherwise <c>false</c>.</returns>
	private bool tryCacheReferences()
	{
		if (!GameObject) { return false; }

		cachedTransform ??= GameObject.transform;

		if (!graphAgent)
		{
			graphAgent = GameObject.GetComponent<BehaviorGraphAgent>();
			if (!graphAgent) { return false; }
		}

		if (!graphAgent.Graph) { return false; }

		if (!ReferenceEquals(cachedGraph, graphAgent.Graph))
		{
			cachedGraph = graphAgent.Graph;
			playerVariable = null;
			playerVisibleVariable = null;
			lastKnownPositionVariable = null;
			timeSinceSeenVariable = null;
			lostSightRecentlyVariable = null;
			lostSightMemoryVariable = null;
		}

		if (playerVariable == null && !graphAgent.GetVariable(playerVariableName, out playerVariable)) { return false; }
		if (playerVisibleVariable == null && !graphAgent.GetVariable(playerVisibleVariableName, out playerVisibleVariable)) { return false; }
		if (lastKnownPositionVariable == null && !graphAgent.GetVariable(lastKnownPositionVariableName, out lastKnownPositionVariable)) { return false; }
		if (timeSinceSeenVariable == null && !graphAgent.GetVariable(timeSinceSeenVariableName, out timeSinceSeenVariable)) { return false; }
		if (lostSightRecentlyVariable == null && !graphAgent.GetVariable(lostSightRecentlyVariableName, out lostSightRecentlyVariable)) { return false; }
		if (lostSightMemoryVariable == null) { graphAgent.GetVariable(lostSightMemoryVariableName, out lostSightMemoryVariable); }

		return true;
	}

	/// <summary>
	/// Resolves and caches player references.
	/// </summary>
	/// <returns><c>true</c> when a valid player transform is available; otherwise <c>false</c>.</returns>
	private bool tryResolvePlayer()
	{
		GameObject _playerObject = playerVariable != null ? playerVariable.Value : null;
		if (!_playerObject) { _playerObject = cachedPlayerObject; }
		if (!_playerObject) { _playerObject = GameObject.FindGameObjectWithTag(playerTag); }
		if (!_playerObject) { return false; }

		if (!ReferenceEquals(cachedPlayerObject, _playerObject))
		{
			cachedPlayerObject = _playerObject;
			cachedPlayerTransform = _playerObject.transform;
			cachedPlayerMovement = _playerObject.GetComponent<PlayerMovement>();
			cachedPlayerCollider = _playerObject.GetComponent<Collider>() ?? _playerObject.GetComponentInChildren<Collider>();
		}

		return cachedPlayerTransform;
	}

	/// <summary>
	/// Evaluates whether the player is currently visible from the stalker's vision origin.
	/// </summary>
	/// <returns><c>true</c> when the player is visible; otherwise <c>false</c>.</returns>
	private bool evaluatePlayerVisible()
	{
		if (!cachedPlayerTransform) { return false; }
		if (cachedPlayerMovement && cachedPlayerMovement.CheckIfHiding()) { return false; }

		fillPlayerVisibilityPoints();

		float _range = Mathf.Max(0.1f, SightRange != null ? SightRange.Value : defaultSightRange);
		float _rangeSqr = _range * _range;
		float _cosHalfFov = resolveCosHalfFov();
		Vector3 _origin = cachedTransform.position + (VisionOriginOffset != null ? VisionOriginOffset.Value : defaultVisionOriginOffset);
		int _raycastMask = VisionRaycastMask != null ? VisionRaycastMask.Value : ~0;
		QueryTriggerInteraction _queryTriggerInteraction = IgnoreTriggerColliders != null && IgnoreTriggerColliders.Value
			? QueryTriggerInteraction.Ignore
			: QueryTriggerInteraction.Collide;

		for (int _i = 0; _i < playerVisibilityPoints.Length; _i++)
		{
			Vector3 _directionToPoint = playerVisibilityPoints[_i] - _origin;
			float _distanceSqr = _directionToPoint.sqrMagnitude;
			if (_distanceSqr <= 0.0001f || _distanceSqr > _rangeSqr) { continue; }

			float _distance = Mathf.Sqrt(_distanceSqr);
			Vector3 _directionNormalized = _directionToPoint / _distance;
			if (Vector3.Dot(cachedTransform.forward, _directionNormalized) < _cosHalfFov) { continue; }

			if (!Physics.Raycast(_origin, _directionNormalized, out RaycastHit _hit, _distance, _raycastMask, _queryTriggerInteraction)) { continue; }
			if (_hit.transform != cachedPlayerTransform && !_hit.transform.IsChildOf(cachedPlayerTransform)) { continue; }
			return true;
		}

		return false;
	}

	/// <summary>
	/// Resolves and caches cosine half-FOV value used by visibility checks.
	/// </summary>
	/// <returns>Cosine of the clamped half field-of-view angle.</returns>
	private float resolveCosHalfFov()
	{
		float _visionWidth = Mathf.Max(1f, VisionWidth != null ? VisionWidth.Value : defaultVisionWidth);
		if (Mathf.Approximately(_visionWidth, cachedVisionWidth)) { return cachedCosHalfFov; }

		cachedVisionWidth = _visionWidth;
		cachedCosHalfFov = Mathf.Cos((_visionWidth * 0.5f) * Mathf.Deg2Rad);
		return cachedCosHalfFov;
	}

	/// <summary>
	/// Fills visibility test points using collider bounds when available; otherwise uses fixed vertical offsets.
	/// </summary>
	private void fillPlayerVisibilityPoints()
	{
		if (cachedPlayerCollider)
		{
			Bounds _playerBounds = cachedPlayerCollider.bounds;
			playerVisibilityPoints[0] = new Vector3(_playerBounds.center.x, _playerBounds.max.y, _playerBounds.center.z);
			playerVisibilityPoints[1] = _playerBounds.center;
			playerVisibilityPoints[2] = new Vector3(_playerBounds.center.x, _playerBounds.min.y + 0.1f, _playerBounds.center.z);
			return;
		}

		Vector3 _playerPosition = cachedPlayerTransform.position;
		float _headOffset = PlayerHeadOffset != null ? PlayerHeadOffset.Value : 1.6f;
		float _torsoOffset = PlayerTorsoOffset != null ? PlayerTorsoOffset.Value : 1.0f;
		float _legOffset = PlayerLegOffset != null ? PlayerLegOffset.Value : 0.35f;
		playerVisibilityPoints[0] = _playerPosition + Vector3.up * _headOffset;
		playerVisibilityPoints[1] = _playerPosition + Vector3.up * _torsoOffset;
		playerVisibilityPoints[2] = _playerPosition + Vector3.up * _legOffset;
	}

	/// <summary>
	/// Resolves tick delta time for this update.
	/// </summary>
	/// <returns>Configured delta time when positive; otherwise <see cref="Time.deltaTime"/>.</returns>
	private float resolveDeltaTime()
	{
		if (DeltaTime != null && DeltaTime.Value > 0f) { return DeltaTime.Value; }
		return Time.deltaTime;
	}
}