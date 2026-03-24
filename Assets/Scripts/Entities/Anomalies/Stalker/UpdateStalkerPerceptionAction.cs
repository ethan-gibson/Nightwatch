using System;
using Game.Entities;
using Game.Entities.Octree;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Logger = Arti.Utilities.Logger;

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
	private const float visibilityPersistenceDuration = 0.2f;
	private const float debugLineDuration = 0.1f;
	private const int maxVisionHits = 16;
	private const string defaultVisionOriginChild = "HighLookPoint";
	private static readonly Vector3 defaultVisionOriginOffset = new(0f, 1.6f, 0f);
	private static readonly Color blockedLineColor = new(1f, 0f, 0f, 1f);
	private static readonly Color visibleLineColor = new(0f, 1f, 0f, 1f);
	private static readonly Color outOfRangeLineColor = new(0.4f, 0.4f, 0.4f, 1f);
	private static readonly Color outOfFovLineColor = new(0f, 0.5f, 1f, 1f);
	private static readonly Color closeRangeLineColor = new(1f, 0.8f, 0f, 1f);

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

	/// <summary>
	/// If greater than zero, the stalker can detect the player in close range even outside the normal FOV gate.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> CloseRangeVisionDistance = new(2.2f);

	/// <summary>
	/// When true, visibility raycasts treat the configured mask as occluders, not as required target layers.
	/// If no occluder is hit before the player point, visibility is granted.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<bool> TreatRaycastMaskAsOccludersOnly = new(true);

	private Transform cachedTransform;
	private Transform cachedVisionOriginTransform;
	private BehaviorGraphAgent graphAgent;
	private BehaviorGraph cachedGraph;
	private PathFindingAgent pathfindingAgent;
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
	private Collider[] cachedSelfColliders;
	private readonly Vector3[] playerVisibilityPoints = new Vector3[3];
	private readonly RaycastHit[] visionHits = new RaycastHit[maxVisionHits];
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
		bool _wasPlayerVisible = playerVisibleVariable != null && playerVisibleVariable.Value;
		if (!_playerVisible && _wasPlayerVisible && _timeSinceSeen <= visibilityPersistenceDuration)
		{
			_playerVisible = true;
		}

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
		if (pathfindingAgent != null) { pathfindingAgent.Target = _playerVisible ? cachedPlayerTransform : null; }
		if (playerVariable != null) { playerVariable.Value = cachedPlayerObject; }
		graphAgent.SetVariableValue(playerVariableName, cachedPlayerObject);

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
		cachedVisionOriginTransform ??= cachedTransform.Find(defaultVisionOriginChild);
		pathfindingAgent ??= GameObject.GetComponent<PathFindingAgent>();
		cachedSelfColliders ??= GameObject.GetComponentsInChildren<Collider>();

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
		Vector3 _origin = resolveVisionOriginPosition();
		int _raycastMask = VisionRaycastMask != null ? VisionRaycastMask.Value : ~0;
		QueryTriggerInteraction _queryTriggerInteraction = IgnoreTriggerColliders != null && IgnoreTriggerColliders.Value
			? QueryTriggerInteraction.Ignore
			: QueryTriggerInteraction.Collide;
		float _closeRangeDistance = Mathf.Max(0f, CloseRangeVisionDistance != null ? CloseRangeVisionDistance.Value : 2.2f);
		float _closeRangeDistanceSqr = _closeRangeDistance * _closeRangeDistance;
		bool _maskAsOccludersOnly = TreatRaycastMaskAsOccludersOnly == null || TreatRaycastMaskAsOccludersOnly.Value;

		for (int _i = 0; _i < playerVisibilityPoints.Length; _i++)
		{
			Vector3 _targetPoint = playerVisibilityPoints[_i];
			Vector3 _directionToPoint = _targetPoint - _origin;
			float _distanceSqr = _directionToPoint.sqrMagnitude;
			if (_distanceSqr <= 0.0001f)
			{
				Debug.DrawLine(_origin, _targetPoint, visibleLineColor, debugLineDuration, false);
				return true;
			}

			if (_distanceSqr > _rangeSqr)
			{
				Debug.DrawLine(_origin, _targetPoint, outOfRangeLineColor, debugLineDuration, false);
				continue;
			}

			float _distance = Mathf.Sqrt(_distanceSqr);
			Vector3 _directionNormalized = _directionToPoint / _distance;
			Vector3 _forwardPlanar = new Vector3(cachedTransform.forward.x, 0f, cachedTransform.forward.z);
			Vector3 _directionPlanar = new Vector3(_directionNormalized.x, 0f, _directionNormalized.z);
			bool _passesFov = true;
			if (_forwardPlanar.sqrMagnitude > 0.0001f && _directionPlanar.sqrMagnitude > 0.0001f)
			{
				_forwardPlanar.Normalize();
				_directionPlanar.Normalize();
				_passesFov = Vector3.Dot(_forwardPlanar, _directionPlanar) >= _cosHalfFov;
			}
			bool _passesCloseRange = _closeRangeDistanceSqr > 0f && _distanceSqr <= _closeRangeDistanceSqr;
			if (!_passesFov && !_passesCloseRange)
			{
				Debug.DrawLine(_origin, _targetPoint, outOfFovLineColor, debugLineDuration, false);
				continue;
			}

			int _hitCount = Physics.RaycastNonAlloc(_origin, _directionNormalized, visionHits, _distance, _raycastMask, _queryTriggerInteraction);
			if (_hitCount <= 0)
			{
				Color _clearColor = _passesCloseRange && !_passesFov ? closeRangeLineColor : visibleLineColor;
				Debug.DrawLine(_origin, _targetPoint, _clearColor, debugLineDuration, false);
				if (_maskAsOccludersOnly) { return true; }
				continue;
			}

			float _closestHitDistance = float.PositiveInfinity;
			RaycastHit _closestHit = default;
			bool _hasClosestHit = false;
			for (int _hitIndex = 0; _hitIndex < _hitCount; _hitIndex++)
			{
				RaycastHit _hit = visionHits[_hitIndex];
				if (isSelfCollider(_hit.collider)) { continue; }
				if (_hit.distance >= _closestHitDistance) { continue; }

				_closestHitDistance = _hit.distance;
				_closestHit = _hit;
				_hasClosestHit = true;
			}

			if (!_hasClosestHit)
			{
				Color _clearColor = _passesCloseRange && !_passesFov ? closeRangeLineColor : visibleLineColor;
				Debug.DrawLine(_origin, _targetPoint, _clearColor, debugLineDuration, false);
				if (_maskAsOccludersOnly) { return true; }
				continue;
			}

			if (_closestHit.transform != cachedPlayerTransform && !_closestHit.transform.IsChildOf(cachedPlayerTransform))
			{
				Debug.DrawLine(_origin, _targetPoint, blockedLineColor, debugLineDuration, false);
				continue;
			}

			Color _visibleColor = _passesCloseRange && !_passesFov ? closeRangeLineColor : visibleLineColor;
			Debug.DrawLine(_origin, _targetPoint, _visibleColor, debugLineDuration, false);
			return true;
		}

		return false;
	}

	private Vector3 resolveVisionOriginPosition()
	{
		if (cachedVisionOriginTransform != null) { return cachedVisionOriginTransform.position; }
		return cachedTransform.position + (VisionOriginOffset != null ? VisionOriginOffset.Value : defaultVisionOriginOffset);
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

	private bool isSelfCollider(Collider _collider)
	{
		if (_collider == null || cachedSelfColliders == null) { return false; }

		for (int i = 0; i < cachedSelfColliders.Length; i++)
		{
			if (ReferenceEquals(cachedSelfColliders[i], _collider)) { return true; }
		}

		return false;
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
