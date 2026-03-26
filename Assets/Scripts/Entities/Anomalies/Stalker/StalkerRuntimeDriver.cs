using System;
using Game.Entities;
using Unity.Behavior;
using UnityEngine;

namespace Game.Entities.Octree
{
	[DisallowMultipleComponent]
	public sealed class StalkerRuntimeDriver : MonoBehaviour
	{
		private const string playerTag = "Player";
		private const string playerVariableName = "Player";
		private const string playerVisibleVariableName = "PlayerVisible";
		private const string lastKnownPositionVariableName = "LastKnownPosition";
		private const string timeSinceSeenVariableName = "TimeSinceSeen";
		private const string lostSightRecentlyVariableName = "LostSightRecently";
		private const string lostSightMemoryVariableName = "LostSightMemory";
		private const string stateVariableName = "State";
		private const string chaseSpeedVariableName = "ChaseSpeed";
		private const string searchSpeedVariableName = "SearchSpeed";
		private const string patrolSpeedVariableName = "PatrolSpeed";
		private const float defaultLostSightMemory = 4f;
		private const float defaultSightRange = 10f;
		private const float defaultVisionWidth = 45f;
		private const float visibilityPersistenceDuration = 0.2f;
		private const float defaultCloseRangeVisionDistance = 2.2f;
		private const float defaultWalkingAudioThreshold = 0.1f;
		private const float minimumAgentSpeed = 0.1f;
		private const float debugLineDuration = 0.1f;
		private const float movementSignalThreshold = 0.05f;
		private const float movementSignalHoldDuration = 0.15f;
		private const float movementBlendRate = 12f;
		private const int maxVisionHits = 16;
		private const string defaultVisionOriginChild = "HighLookPoint";
		private const string defaultWalkingAudioSourceChild = "LowLookPoint";
		private static readonly int velocityAnimatorSpeedParameterHash = Animator.StringToHash("Velocity");
		private static readonly int speedMagnitudeAnimatorSpeedParameterHash = Animator.StringToHash("SpeedMagnitude");
		private static readonly int leavingHash = Animator.StringToHash("IsLeaving");
		private static readonly Vector3 defaultVisionOriginOffset = new(0f, 1.6f, 0f);
		private static readonly Color blockedLineColor = new(1f, 0f, 0f, 1f);
		private static readonly Color visibleLineColor = new(0f, 1f, 0f, 1f);
		private static readonly Color outOfRangeLineColor = new(0.4f, 0.4f, 0.4f, 1f);
		private static readonly Color outOfFovLineColor = new(0f, 0.5f, 1f, 1f);
		private static readonly Color closeRangeLineColor = new(1f, 0.8f, 0f, 1f);
		private static readonly Color hidingLineColor = new(0.8f, 0f, 1f, 1f);

		private BehaviorGraphAgent graphAgent;
		private BehaviorGraph cachedGraph;
		private PathFindingAgent pathfindingAgent;
		private Animator animator;
		private global::StalkerAnimationEvents animationEvents;
		private AudioSource walkingAudioSource;
		private Transform cachedTransform;
		private Transform visionOriginTransform;
		private Collider[] cachedSelfColliders;
		private GameObject cachedPlayerObject;
		private Transform cachedPlayerTransform;
		private PlayerMovement cachedPlayerMovement;
		private Collider cachedPlayerCollider;
		private BlackboardVariable<GameObject> playerVariable;
		private BlackboardVariable<bool> playerVisibleVariable;
		private BlackboardVariable<Vector3> lastKnownPositionVariable;
		private BlackboardVariable<float> timeSinceSeenVariable;
		private BlackboardVariable<bool> lostSightRecentlyVariable;
		private BlackboardVariable<float> lostSightMemoryVariable;
		private readonly Vector3[] playerVisibilityPoints = new Vector3[3];
		private readonly RaycastHit[] visionHits = new RaycastHit[maxVisionHits];
		private Vector3 lastFramePosition;
		private bool hasLastFramePosition;
		private float smoothedMovementSpeed;
		private float lastMovementSignalTime;
		private float cachedVisionWidth = float.NaN;
		private float cachedCosHalfFov;

		private void Awake()
		{
			cacheReferences();
			lastFramePosition = transform.position;
			hasLastFramePosition = true;
		}

		private void OnEnable()
		{
			cacheReferences();
			lastFramePosition = transform.position;
			hasLastFramePosition = true;
			smoothedMovementSpeed = 0f;
			lastMovementSignalTime = Time.time;
		}

		private void Update()
		{
			if (!cacheReferences()) { return; }

			updateMovementSpeedTargets();
			updatePerception();
		}

		private void LateUpdate()
		{
			if (!cacheReferences()) { return; }

			updateLocomotion();
		}

		private bool cacheReferences()
		{
			cachedTransform ??= transform;
			graphAgent ??= GetComponent<BehaviorGraphAgent>();
			pathfindingAgent ??= GetComponent<PathFindingAgent>();
			animator ??= GetComponent<Animator>();
			animationEvents ??= GetComponent<global::StalkerAnimationEvents>();
			cachedSelfColliders ??= GetComponentsInChildren<Collider>();

			if (!walkingAudioSource)
			{
				Transform _walkingAudioTransform = cachedTransform.Find(defaultWalkingAudioSourceChild);
				walkingAudioSource = _walkingAudioTransform ? _walkingAudioTransform.GetComponent<AudioSource>() : null;
			}

			visionOriginTransform ??= cachedTransform.Find(defaultVisionOriginChild);

			if (graphAgent != null && graphAgent.Graph != null && !ReferenceEquals(cachedGraph, graphAgent.Graph))
			{
				cachedGraph = graphAgent.Graph;
				playerVariable = null;
				playerVisibleVariable = null;
				lastKnownPositionVariable = null;
				timeSinceSeenVariable = null;
				lostSightRecentlyVariable = null;
				lostSightMemoryVariable = null;
			}

			return cachedTransform != null;
		}

		private void updateMovementSpeedTargets()
		{
			if (!graphAgent || !graphAgent.Graph) { return; }
			if (pathfindingAgent != null && pathfindingAgent.MovementAuthority != StalkerMovementAuthority.Octree) { return; }

			if (!graphAgent.GetVariable(stateVariableName, out BlackboardVariable<global::State> _stateVariable)) { return; }
			if (!graphAgent.GetVariable(chaseSpeedVariableName, out BlackboardVariable<float> _chaseSpeedVariable)) { return; }
			if (!graphAgent.GetVariable(searchSpeedVariableName, out BlackboardVariable<float> _searchSpeedVariable)) { return; }
			if (!graphAgent.GetVariable(patrolSpeedVariableName, out BlackboardVariable<float> _patrolSpeedVariable)) { return; }

			float _targetSpeed = resolveSpeedForState(_stateVariable.Value, _chaseSpeedVariable.Value, _searchSpeedVariable.Value, _patrolSpeedVariable.Value);
			_targetSpeed = Mathf.Max(minimumAgentSpeed, _targetSpeed);

			if (pathfindingAgent != null && !Mathf.Approximately(pathfindingAgent.MoveSpeed, _targetSpeed))
			{
				pathfindingAgent.MoveSpeed = _targetSpeed;
			}
		}

		private static float resolveSpeedForState(global::State _state, float _chaseSpeed, float _searchSpeed, float _patrolSpeed)
		{
			switch (_state)
			{
				case global::State.Chasing:
					return _chaseSpeed;
				case global::State.Searching:
					return _searchSpeed;
				default:
					return _patrolSpeed;
			}
		}

		private void updatePerception()
		{
			if (!graphAgent || !graphAgent.Graph) { return; }
			cachePerceptionVariables();
			if (!tryResolvePlayer())
			{
				if (playerVisibleVariable != null) { playerVisibleVariable.Value = false; }
				if (lostSightRecentlyVariable != null) { lostSightRecentlyVariable.Value = false; }
				graphAgent.SetVariableValue(playerVisibleVariableName, false);
				graphAgent.SetVariableValue(lostSightRecentlyVariableName, false);
				if (pathfindingAgent != null) { pathfindingAgent.Target = null; }
				return;
			}

			bool _playerVisible = evaluatePlayerVisible();
			float _timeSinceSeen = timeSinceSeenVariable != null ? Mathf.Max(0f, timeSinceSeenVariable.Value) : 0f;
			bool _wasPlayerVisible = playerVisibleVariable != null && playerVisibleVariable.Value;
			if (!_playerVisible && _wasPlayerVisible && _timeSinceSeen <= visibilityPersistenceDuration)
			{
				_playerVisible = true;
			}

			if (_playerVisible)
			{
				if (lastKnownPositionVariable != null) { lastKnownPositionVariable.Value = cachedPlayerTransform.position; }
				graphAgent.SetVariableValue(lastKnownPositionVariableName, cachedPlayerTransform.position);
				_timeSinceSeen = 0f;
			}
			else
			{
				_timeSinceSeen += Time.deltaTime;
			}

			float _lostSightMemory = defaultLostSightMemory;
			if (lostSightMemoryVariable != null) { _lostSightMemory = Mathf.Max(0.1f, lostSightMemoryVariable.Value); }

			if (playerVariable != null) { playerVariable.Value = cachedPlayerObject; }
			if (playerVisibleVariable != null) { playerVisibleVariable.Value = _playerVisible; }
			if (timeSinceSeenVariable != null) { timeSinceSeenVariable.Value = _timeSinceSeen; }
			if (lostSightRecentlyVariable != null) { lostSightRecentlyVariable.Value = !_playerVisible && _timeSinceSeen <= _lostSightMemory; }
			graphAgent.SetVariableValue(playerVariableName, cachedPlayerObject);
			graphAgent.SetVariableValue(playerVisibleVariableName, _playerVisible);
			graphAgent.SetVariableValue(timeSinceSeenVariableName, _timeSinceSeen);
			graphAgent.SetVariableValue(lostSightRecentlyVariableName, !_playerVisible && _timeSinceSeen <= _lostSightMemory);

			if (pathfindingAgent != null)
			{
				pathfindingAgent.Target = pathfindingAgent.MovementAuthority == StalkerMovementAuthority.Octree && _playerVisible ? cachedPlayerTransform : null;
			}
		}

		private void cachePerceptionVariables()
		{
			if (!graphAgent || !graphAgent.Graph) { return; }

			if (playerVariable == null) { graphAgent.GetVariable(playerVariableName, out playerVariable); }
			if (playerVisibleVariable == null) { graphAgent.GetVariable(playerVisibleVariableName, out playerVisibleVariable); }
			if (lastKnownPositionVariable == null) { graphAgent.GetVariable(lastKnownPositionVariableName, out lastKnownPositionVariable); }
			if (timeSinceSeenVariable == null) { graphAgent.GetVariable(timeSinceSeenVariableName, out timeSinceSeenVariable); }
			if (lostSightRecentlyVariable == null) { graphAgent.GetVariable(lostSightRecentlyVariableName, out lostSightRecentlyVariable); }
			if (lostSightMemoryVariable == null) { graphAgent.GetVariable(lostSightMemoryVariableName, out lostSightMemoryVariable); }
		}

		private bool tryResolvePlayer()
		{
			GameObject _playerObject = cachedPlayerObject;
			if (!_playerObject && graphAgent && graphAgent.Graph && graphAgent.GetVariable(playerVariableName, out BlackboardVariable<GameObject> _playerVariable))
			{
				_playerObject = _playerVariable.Value;
			}

			if (!_playerObject)
			{
				_playerObject = GameObject.FindGameObjectWithTag(playerTag);
			}

			if (!_playerObject)
			{
				cachedPlayerObject = null;
				cachedPlayerTransform = null;
				cachedPlayerMovement = null;
				cachedPlayerCollider = null;
				return false;
			}

			if (!ReferenceEquals(cachedPlayerObject, _playerObject))
			{
				cachedPlayerObject = _playerObject;
				cachedPlayerTransform = _playerObject.transform;
				cachedPlayerMovement = _playerObject.GetComponent<PlayerMovement>();
				cachedPlayerCollider = _playerObject.GetComponent<Collider>() ?? _playerObject.GetComponentInChildren<Collider>();
			}

			return cachedPlayerTransform != null;
		}

		private bool evaluatePlayerVisible()
		{
			if (!cachedPlayerTransform) { return false; }

			Vector3 _origin = resolveVisionOriginPosition();
			fillPlayerVisibilityPoints();

			if (cachedPlayerMovement && cachedPlayerMovement.CheckIfHiding())
			{
				drawDebugLines(_origin, hidingLineColor);
				return false;
			}

			float _range = Mathf.Max(0.1f, defaultSightRange);
			float _rangeSqr = _range * _range;
			float _cosHalfFov = resolveCosHalfFov();
			float _closeRangeDistanceSqr = defaultCloseRangeVisionDistance * defaultCloseRangeVisionDistance;
			QueryTriggerInteraction _queryTriggerInteraction = QueryTriggerInteraction.Ignore;

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
				bool _passesCloseRange = _distanceSqr <= _closeRangeDistanceSqr;
				if (!_passesFov && !_passesCloseRange)
				{
					Debug.DrawLine(_origin, _targetPoint, outOfFovLineColor, debugLineDuration, false);
					continue;
				}

				int _hitCount = Physics.RaycastNonAlloc(_origin, _directionNormalized, visionHits, _distance, ~0, _queryTriggerInteraction);
				if (_hitCount <= 0)
				{
					Color _visibleColor = _passesCloseRange && !_passesFov ? closeRangeLineColor : visibleLineColor;
					Debug.DrawLine(_origin, _targetPoint, _visibleColor, debugLineDuration, false);
					return true;
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
					Color _visibleColor = _passesCloseRange && !_passesFov ? closeRangeLineColor : visibleLineColor;
					Debug.DrawLine(_origin, _targetPoint, _visibleColor, debugLineDuration, false);
					return true;
				}

				bool _hitPlayer = _closestHit.transform == cachedPlayerTransform
					|| (_closestHit.transform != null && _closestHit.transform.IsChildOf(cachedPlayerTransform));
				Color _lineColor = _hitPlayer
					? (_passesCloseRange && !_passesFov ? closeRangeLineColor : visibleLineColor)
					: blockedLineColor;
				Debug.DrawLine(_origin, _targetPoint, _lineColor, debugLineDuration, false);
				if (_hitPlayer) { return true; }
			}

			return false;
		}

		private Vector3 resolveVisionOriginPosition()
		{
			if (visionOriginTransform != null) { return visionOriginTransform.position; }
			return cachedTransform.position + defaultVisionOriginOffset;
		}

		private float resolveCosHalfFov()
		{
			if (Mathf.Approximately(defaultVisionWidth, cachedVisionWidth)) { return cachedCosHalfFov; }

			cachedVisionWidth = defaultVisionWidth;
			cachedCosHalfFov = Mathf.Cos((defaultVisionWidth * 0.5f) * Mathf.Deg2Rad);
			return cachedCosHalfFov;
		}

		private void fillPlayerVisibilityPoints()
		{
			if (cachedPlayerCollider && cachedPlayerCollider.enabled)
			{
				Bounds _playerBounds = cachedPlayerCollider.bounds;
				playerVisibilityPoints[0] = new Vector3(_playerBounds.center.x, _playerBounds.max.y, _playerBounds.center.z);
				playerVisibilityPoints[1] = _playerBounds.center;
				playerVisibilityPoints[2] = new Vector3(_playerBounds.center.x, _playerBounds.min.y + 0.1f, _playerBounds.center.z);
				return;
			}

			Vector3 _playerPosition = cachedPlayerTransform.position;
			playerVisibilityPoints[0] = _playerPosition + Vector3.up * 1.6f;
			playerVisibilityPoints[1] = _playerPosition + Vector3.up * 1.0f;
			playerVisibilityPoints[2] = _playerPosition + Vector3.up * 0.35f;
		}

		private void drawDebugLines(Vector3 _origin, Color _lineColor)
		{
			for (int _i = 0; _i < playerVisibilityPoints.Length; _i++)
			{
				Debug.DrawLine(_origin, playerVisibilityPoints[_i], _lineColor, debugLineDuration, false);
			}
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

		private void updateLocomotion()
		{
			Vector3 _currentPosition = cachedTransform.position;
			if (pathfindingAgent != null && pathfindingAgent.MovementAuthority != StalkerMovementAuthority.Octree)
			{
				smoothedMovementSpeed = 0f;
				lastMovementSignalTime = Time.time;
				if (pathfindingAgent.MovementAuthority == StalkerMovementAuthority.AnimationLocked && animator && !animator.GetBool(leavingHash))
				{
					animator.SetFloat(velocityAnimatorSpeedParameterHash, 0f);
					animator.SetFloat(speedMagnitudeAnimatorSpeedParameterHash, 0f);
				}

				if (walkingAudioSource && walkingAudioSource.isPlaying) { walkingAudioSource.Stop(); }
				lastFramePosition = _currentPosition;
				hasLastFramePosition = true;
				return;
			}

			float _movementSpeed = resolveMovementSpeedMagnitude(_currentPosition);
			bool _expectsMovement = expectsMovementSignal();
			if (_movementSpeed > movementSignalThreshold)
			{
				lastMovementSignalTime = Time.time;
			}
			else if (_expectsMovement && Time.time - lastMovementSignalTime <= movementSignalHoldDuration)
			{
				_movementSpeed = smoothedMovementSpeed;
			}

			float _blendDelta = movementBlendRate * Time.deltaTime;
			smoothedMovementSpeed = Mathf.MoveTowards(smoothedMovementSpeed, _movementSpeed, _blendDelta);
			if (!_expectsMovement && smoothedMovementSpeed <= movementSignalThreshold)
			{
				smoothedMovementSpeed = 0f;
			}

			if (animator && !animator.GetBool(leavingHash))
			{
				animator.SetFloat(velocityAnimatorSpeedParameterHash, smoothedMovementSpeed);
				animator.SetFloat(speedMagnitudeAnimatorSpeedParameterHash, smoothedMovementSpeed);
			}

			if (walkingAudioSource)
			{
				float _threshold = Mathf.Max(0f, defaultWalkingAudioThreshold);
				if (smoothedMovementSpeed > _threshold)
				{
					if (!walkingAudioSource.isPlaying) { walkingAudioSource.Play(); }
				}
				else
				{
					if (walkingAudioSource.isPlaying) { walkingAudioSource.Stop(); }
				}
			}

			lastFramePosition = _currentPosition;
			hasLastFramePosition = true;
		}

		private bool expectsMovementSignal()
		{
			if (pathfindingAgent != null && pathfindingAgent.MovementAuthority == StalkerMovementAuthority.Octree && (pathfindingAgent.IsMoving || pathfindingAgent.HasPath || pathfindingAgent.Target != null))
			{
				return true;
			}

			return false;
		}

		private float resolveMovementSpeedMagnitude(Vector3 _currentPosition)
		{
			if (animationEvents != null && animationEvents.IsMovementLocked) { return 0f; }
			if (pathfindingAgent != null && pathfindingAgent.MovementAuthority == StalkerMovementAuthority.Octree)
			{
				if (pathfindingAgent.CurrentSpeed > 0f) { return pathfindingAgent.CurrentSpeed; }
				if (pathfindingAgent.IsMoving || pathfindingAgent.HasPath) { return pathfindingAgent.MoveSpeed; }
			}

			float _deltaSpeed = 0f;
			if (hasLastFramePosition)
			{
				Vector3 _delta = _currentPosition - lastFramePosition;
				_delta.y = 0f;
				float _deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
				_deltaSpeed = _delta.magnitude / _deltaTime;
			}

			if (_deltaSpeed > 0.01f) { return _deltaSpeed; }
			return 0f;
		}
	}
}
