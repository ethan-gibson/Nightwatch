using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Entities;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;
using Logger = Arti.Utilities.Logger;

public enum EnemyState
{
	Patrolling,
	Chasing,
	Searching,
}

public class EnemyAI : MonoBehaviour
{
	private static readonly int velocity = Animator.StringToHash("Velocity");
	private static readonly int leaving = Animator.StringToHash("IsLeaving");
	private static readonly int isKilling = Animator.StringToHash("IsKilling");
	private static readonly int killType = Animator.StringToHash("KillType");
	[Header("Perception / Movement")]
	[SerializeField]
	private float sightRange = 10f;

	[SerializeField]
	private float patrolSpeed = 2f;
	[SerializeField]
	private float chaseSpeed = 5f;
	[SerializeField]
	private float searchSpeed = 3f;
	[SerializeField]
	private float searchDuration = 5f;
	[SerializeField]
	private float visionWidth = 45f;
	[SerializeField]
	private float patrolRadius = 10f;
	[SerializeField]
	private float searchRadius = 5f;
	[SerializeField]
	private float searchDelay = 2f;
	[SerializeField]
	private float lifeTime = 20f;
	[SerializeField]
	private Transform highLookPoint;
	[SerializeField]
	private Transform lowLookPoint;
	[SerializeField]
	private AudioClip alertedNoise;
	[SerializeField]
	private AudioClip searchStarted;
	[SerializeField]
	private AudioClip searchEnded;
	[SerializeField]
	private bool disableLegacyHuntingAnomaly = true;

	[Header("Behavior Graph Integration")]
	[SerializeField]
	private bool useBehaviorGraphIfAvailable = true;

	[SerializeField]
	private float lostSightMemory = 4f;
	[SerializeField]
	private bool patchRuntimeObservers = true;

	[Header("Perception Tuning")]
	[SerializeField]
	private Vector3 visionOriginOffset = new(0f, 1.6f, 0f);

	[SerializeField]
	private float playerHeadOffset = 1.6f;
	[SerializeField]
	private float playerTorsoOffset = 1.0f;
	[SerializeField]
	private float playerLegOffset = 0.35f;

	[Header("Navigation Recovery")]
	[SerializeField]
	private float stuckVelocityThreshold = 0.15f;

	[SerializeField]
	private float stuckDuration = 1.25f;
	[SerializeField]
	private float minRemainingDistanceToConsiderStuck = 0.8f;
	[SerializeField]
	private float repathRadius = 1.5f;
	[SerializeField]
	private float hiddenKillDistance = 1.75f;

	private NavMeshAgent agent;
	private Transform player;
	private PlayerMovement playerMovement;
	private BehaviorGraphAgent behaviorGraphAgent;
	private Vector3 lastKnownPlayerPosition;
	private EnemyState currentState;
	private float searchTimer;
	private float timeSinceSeen;
	private float delayTimer;
	private bool playerWasSeenHiding; //in case the stalker happens to get close to the player without seeing him
	private float timeActive;
	private bool isLeaving;
	private GameObject[] exitPoints;
	private Animator animator;
	private float currentFOV;
	private AudioSource audioSource;
	private AudioSource walkingAudioSource;
	private bool usingBehaviorGraph;
	private bool runtimeObserverPatched;
	private bool runtimeNavigationPatched;
	private bool graphStateInitialized;
	private BehaviorGraph patchedGraph;
	private State lastGraphState;
	private float stuckTimer;
	private readonly Vector3[] playerVisibilityPoints = new Vector3[3];

	private const BindingFlags reflectionFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private void Awake()
	{
		if (disableLegacyHuntingAnomaly && TryGetComponent(out HuntingAnomaly _legacyHuntingAnomaly) && _legacyHuntingAnomaly.enabled)
		{
			_legacyHuntingAnomaly.enabled = false;
			Logger.LogWarning("Disabled legacy HuntingAnomaly on Stalker to avoid AI controller conflicts.");
		}

		agent = GetComponent<NavMeshAgent>();
		currentState = EnemyState.Patrolling;
		player = GameObject.FindGameObjectWithTag("Player")?.transform;
		playerMovement = player ? player.GetComponent<PlayerMovement>() : null;
		exitPoints = GameObject.FindGameObjectsWithTag("StalkerEnterExit");
		animator = GetComponent<Animator>();
		audioSource = GetComponent<AudioSource>();

		highLookPoint ??= transform.Find("HighLookPoint");
		lowLookPoint ??= transform.Find("LowLookPoint");
		walkingAudioSource = lowLookPoint ? lowLookPoint.GetComponent<AudioSource>() : null;
		behaviorGraphAgent = GetComponent<BehaviorGraphAgent>();
		if (useBehaviorGraphIfAvailable && behaviorGraphAgent && !behaviorGraphAgent.Graph) { Logger.LogWarning("BehaviorGraphAgent has no graph assigned on Stalker. Falling back to legacy FSM."); }
		usingBehaviorGraph = useBehaviorGraphIfAvailable && behaviorGraphAgent && behaviorGraphAgent.Graph;
		currentFOV = visionWidth;

		if (usingBehaviorGraph) { applyBlackboardDefaults(); }
	}

	private void Update()
	{
		if (!usingBehaviorGraph && useBehaviorGraphIfAvailable)
		{
			behaviorGraphAgent ??= GetComponent<BehaviorGraphAgent>();
			if (behaviorGraphAgent && behaviorGraphAgent.Graph)
			{
				usingBehaviorGraph = true;
				applyBlackboardDefaults();
			}
		}

		animator.SetFloat(velocity, agent.velocity.magnitude);
		if (isLeaving && agent.remainingDistance <= agent.stoppingDistance)
		{
			agent.speed = 0;
			animator.SetBool(leaving, true);
			gameObject.GetComponent<Collider>().enabled = false;
			transform.position = agent.destination + new Vector3(0, 1.3f, 0);
		}

		if (isLeaving) { return; }

		timeActive += Time.deltaTime;
		if (timeActive >= lifeTime)
		{
			isLeaving = true;
			leaveMap();
			return;
		}

		updateStuckRecovery();

		if (usingBehaviorGraph)
		{
			syncBehaviorGraphBlackboard();
			return;
		}

		switch (currentState)
		{
			case EnemyState.Patrolling:
				patrol();
				checkForPlayer();
				break;

			case EnemyState.Chasing:
				chasePlayer();
				if (!canSeePlayer()) { transitionToState(EnemyState.Searching); }
				break;

			case EnemyState.Searching:
				searchForPlayer();
				break;
		}
	}

	private void LateUpdate()
	{
		if (walkingAudioSource == null) { return; }

		if (agent.velocity.magnitude > 0.1f)
		{
			if (!walkingAudioSource.isPlaying) { walkingAudioSource.Play(); }
		}
		else
		{
			if (walkingAudioSource.isPlaying) { walkingAudioSource.Stop(); }
		}
	}

	private void applyBlackboardDefaults()
	{
		if (behaviorGraphAgent == null || player == null) { return; }

		lastKnownPlayerPosition = player.position;
		timeSinceSeen = 0f;
		graphStateInitialized = false;

		behaviorGraphAgent.SetVariableValue("Player", player.gameObject);
		behaviorGraphAgent.SetVariableValue("State", State.Patrol);
		behaviorGraphAgent.SetVariableValue("PlayerVisible", false);
		behaviorGraphAgent.SetVariableValue("LastKnownPosition", player.position);
		behaviorGraphAgent.SetVariableValue("TimeSinceSeen", 0f);
		behaviorGraphAgent.SetVariableValue("LostSightRecently", false);
		behaviorGraphAgent.SetVariableValue("ChaseSpeed", chaseSpeed);
		behaviorGraphAgent.SetVariableValue("SearchSpeed", searchSpeed);
		behaviorGraphAgent.SetVariableValue("PatrolSpeed", patrolSpeed);
		behaviorGraphAgent.SetVariableValue("SearchPoint", transform.position);
	}

	private void syncBehaviorGraphBlackboard()
	{
		if (!behaviorGraphAgent || !behaviorGraphAgent.Graph || !player || !playerMovement) { return; }

		if (!ReferenceEquals(patchedGraph, behaviorGraphAgent.Graph))
		{
			patchedGraph = behaviorGraphAgent.Graph;
			runtimeObserverPatched = false;
			runtimeNavigationPatched = false;
			graphStateInitialized = false;
		}

		if (patchRuntimeObservers && !runtimeObserverPatched) { runtimeObserverPatched = tryPatchChaseObserver(); }
		if (!runtimeNavigationPatched) { runtimeNavigationPatched = tryPatchNavigationSpeeds(); }
		if (!playerMovement.CheckIfHiding()) { playerWasSeenHiding = false; }

		bool _playerVisible = canSeePlayer();
		if (_playerVisible)
		{
			lastKnownPlayerPosition = player.position;
			timeSinceSeen = 0f;
		}
		else { timeSinceSeen += Time.deltaTime; }

		bool _lostSightRecently = !_playerVisible && timeSinceSeen <= Mathf.Max(0.1f, lostSightMemory);

		behaviorGraphAgent.SetVariableValue("PlayerVisible", _playerVisible);
		behaviorGraphAgent.SetVariableValue("LastKnownPosition", lastKnownPlayerPosition);
		behaviorGraphAgent.SetVariableValue("TimeSinceSeen", timeSinceSeen);
		behaviorGraphAgent.SetVariableValue("LostSightRecently", _lostSightRecently);
		behaviorGraphAgent.SetVariableValue("ChaseSpeed", chaseSpeed);
		behaviorGraphAgent.SetVariableValue("SearchSpeed", searchSpeed);
		behaviorGraphAgent.SetVariableValue("PatrolSpeed", patrolSpeed);

		// Fallback for graphs where observer registration cannot be patched.
		if (!runtimeObserverPatched && _playerVisible) { behaviorGraphAgent.SetVariableValue("SearchPoint", transform.position); }

		// Forces NavigateToTarget to fail when sight is lost so chase can exit immediately.
		behaviorGraphAgent.SetVariableValue("Player", _playerVisible ? player.gameObject : null);

		syncGraphStateEffects();
		tryKillHiddenPlayerInRange();
	}

	private bool tryPatchChaseObserver()
	{
		if (!tryGetRuntimeRoot(out Composite _rootNode)) { return false; }

		foreach (Node _child in _rootNode.Children)
		{
			if (!isConditionalGuardForVariable(_child, "PlayerVisible")) { continue; }
			return configureGuardObserver(_rootNode, _child, "Both");
		}

		return false;
	}

	private static bool isConditionalGuardForVariable(Node _node, string _variableName)
	{
		Type _nodeType = _node.GetType();
		if (_nodeType.FullName != "Unity.Behavior.ConditionalGuardModifier") { return false; }

		var _conditionsField = _nodeType.GetField("m_Conditions", reflectionFlags);
		if (_conditionsField?.GetValue(_node) is not IList _conditions) { return false; }

		foreach (var _condition in _conditions)
		{
			var _variableField = _condition?.GetType().GetField("Variable", reflectionFlags);
			if (_variableField?.GetValue(_condition) is BlackboardVariable _variable && string.Equals(_variable.Name, _variableName, StringComparison.Ordinal)) { return true; }
		}

		return false;
	}

	private static bool configureGuardObserver(Composite _parentComposite, Node _guardNode, string _observerMode)
	{
		Type _guardType = _guardNode.GetType();
		var _observerField = _guardType.GetField("m_ObserverType", reflectionFlags);
		if (_observerField == null) { return false; }

		object _enumValue;
		try { _enumValue = Enum.Parse(_observerField.FieldType, _observerMode); }
		catch { return false; }

		_observerField.SetValue(_guardNode, _enumValue);

		var _registeredField = typeof(Composite).GetField("m_RegisteredObservers", reflectionFlags);
		if (_registeredField?.GetValue(_parentComposite) is not IList _registeredObservers) { return false; }

		Type _observerInfoType = _guardType.Assembly.GetType("Unity.Behavior.ObserverAbortInfo");
		if (_observerInfoType == null) { return false; }

		var _observerReferenceField = _observerInfoType.GetField("Observer", reflectionFlags);
		if (_observerReferenceField == null) { return false; }

		foreach (var _registration in _registeredObservers)
		{
			if (_registration == null) { continue; }
			if (ReferenceEquals(_observerReferenceField.GetValue(_registration), _guardNode)) { return true; }
		}

		var _newRegistration = Activator.CreateInstance(_observerInfoType);
		_observerReferenceField.SetValue(_newRegistration, _guardNode);
		_registeredObservers.Add(_newRegistration);
		return true;
	}

	private bool tryPatchNavigationSpeeds()
	{
		if (!tryGetRuntimeRoot(out Composite _rootNode)) { return false; }

		bool _patched = false;
		var _queue = new Queue<Node>();
		var _visited = new HashSet<Node>();
		_queue.Enqueue(_rootNode);

		while (_queue.Count > 0)
		{
			Node _currentNode = _queue.Dequeue();
			if (_currentNode == null || !_visited.Add(_currentNode)) { continue; }

			float _desiredSpeed = getDesiredNavigationSpeed(_currentNode);
			if (_desiredSpeed > 0f) { _patched |= trySetNodeSpeed(_currentNode, _desiredSpeed); }

			if (_currentNode is Composite _compositeNode)
			{
				foreach (Node _childNode in _compositeNode.Children) { _queue.Enqueue(_childNode); }
			}

			var _childField = _currentNode.GetType().GetField("m_Child", reflectionFlags);
			if (_childField?.GetValue(_currentNode) is Node _child) { _queue.Enqueue(_child); }
		}

		return _patched;
	}

	private float getDesiredNavigationSpeed(Node _node)
	{
		if (_node == null) { return -1f; }

		string _nodeTypeName = _node.GetType().FullName ?? _node.GetType().Name;
		if (_nodeTypeName == "Unity.Behavior.NavigateToTargetAction") { return chaseSpeed; }
		if (_nodeTypeName != "Unity.Behavior.NavigateToLocationAction") { return -1f; }

		var _parentField = _node.GetType().GetField("m_Parent", reflectionFlags);
		if (_parentField?.GetValue(_node) is not Composite _parentNode) { return patrolSpeed; }

		foreach (Node _siblingNode in _parentNode.Children)
		{
			string _siblingTypeName = _siblingNode.GetType().FullName ?? _siblingNode.GetType().Name;
			if (_siblingTypeName.EndsWith(nameof(PickRandomSearchPointAction), StringComparison.Ordinal)) { return searchSpeed; }
			if (_siblingTypeName.EndsWith(nameof(GetPatrolPointAction), StringComparison.Ordinal)) { return patrolSpeed; }
		}

		return patrolSpeed;
	}

	private static bool trySetNodeSpeed(Node _node, float _speed)
	{
		var _speedField = _node.GetType().GetField("Speed", reflectionFlags);
		if (_speedField?.GetValue(_node) is not BlackboardVariable<float> _speedVariable) { return false; }

		_speedVariable.Value = Mathf.Max(0.1f, _speed);
		return true;
	}

	private bool tryGetRuntimeRoot(out Composite _rootNode)
	{
		_rootNode = null;
		if (!behaviorGraphAgent?.Graph) { return false; }

		var _graphField = typeof(BehaviorGraph).GetField("Graphs", reflectionFlags);
		if (_graphField?.GetValue(behaviorGraphAgent.Graph) is not IList _graphModules || _graphModules.Count == 0) { return false; }

		object _rootModule = _graphModules[0];
		var _rootField = _rootModule.GetType().GetField("Root", reflectionFlags);
		_rootNode = _rootField?.GetValue(_rootModule) as Composite;
		return _rootNode != null;
	}

	private void transitionToState(EnemyState _newState)
	{
		currentState = _newState;
		delayTimer = 0f;

		switch (_newState)
		{
			case EnemyState.Patrolling:
				agent.speed = patrolSpeed;
				patrol();
				audioSource.PlayOneShot(searchEnded);
				break;

			case EnemyState.Chasing:
				agent.speed = chaseSpeed;
				audioSource.PlayOneShot(alertedNoise);
				break;

			case EnemyState.Searching:
				searchTimer = 0f;
				agent.SetDestination(lastKnownPlayerPosition);
				audioSource.PlayOneShot(searchStarted);
				break;
		}
	}

	private void patrol()
	{
		if (!(agent.remainingDistance <= agent.stoppingDistance)) { return; }
		delayTimer += Time.deltaTime;
		if (delayTimer < searchDelay) { return; }
		delayTimer = 0f;
		Vector3 _randomDirection = Random.insideUnitSphere * patrolRadius;
		_randomDirection += transform.position;
		NavMesh.SamplePosition(_randomDirection, out NavMeshHit _hit, patrolRadius, NavMesh.AllAreas);
		agent.SetDestination(_hit.position);
	}

	private void checkForPlayer()
	{
		if (!playerMovement.CheckIfHiding()) { playerWasSeenHiding = false; }
		if (!canSeePlayer()) { return; }
		lastKnownPlayerPosition = player.gameObject.transform.position;
		transitionToState(EnemyState.Chasing);
	}

	private void chasePlayer()
	{
		lastKnownPlayerPosition = player.gameObject.transform.position;
		agent.SetDestination(player.gameObject.transform.position);
	}

	private void searchForPlayer()
	{
		if (agent.remainingDistance > agent.stoppingDistance) { return; }
		if (playerWasSeenHiding && playerMovement.CheckIfHiding())
		{
			if (playerMovement.CheckIfInCloset()) { killPlayer(highLookPoint, 1); }
			if (playerMovement.CheckIfUnderBed()) { killPlayer(lowLookPoint, 2); }
			return;
		}
		delayTimer += Time.deltaTime;

		if (!(delayTimer >= searchDelay)) { return; }
		delayTimer = 0f;
		searchTimer += Time.deltaTime;
		if (searchTimer >= searchDuration) { transitionToState(EnemyState.Patrolling); }
		else
		{
			Vector3 _randomDirection = Random.insideUnitSphere * searchRadius;
			_randomDirection += lastKnownPlayerPosition;
			NavMesh.SamplePosition(_randomDirection, out NavMeshHit _hit, searchRadius, NavMesh.AllAreas);
			agent.SetDestination(_hit.position);
		}
	}

	private void leaveMap()
	{
		Transform _closestExit = null;
		float _closestDistance = Mathf.Infinity;

		foreach (GameObject _exit in exitPoints)
		{
			float _dist = Vector3.Distance(transform.position, _exit.transform.position);
			if (_dist >= _closestDistance) { continue; }
			_closestDistance = _dist;
			_closestExit = _exit.transform;
		}
		if (_closestExit) { agent.SetDestination(_closestExit.position); }
		else { Logger.LogError("No Valid Path"); }
	}

	private void syncGraphStateEffects()
	{
		if (!behaviorGraphAgent) { return; }
		if (!behaviorGraphAgent.GetVariable("State", out BlackboardVariable<State> _stateVariable)) { return; }

		State _graphState = _stateVariable.Value;
		if (graphStateInitialized && _graphState == lastGraphState) { return; }

		graphStateInitialized = true;
		lastGraphState = _graphState;

		switch (_graphState)
		{
			case State.Chasing:
				agent.speed = chaseSpeed;
				if (alertedNoise) { audioSource?.PlayOneShot(alertedNoise); }
				break;

			case State.Searching:
				agent.speed = searchSpeed;
				if (searchStarted) { audioSource?.PlayOneShot(searchStarted); }
				break;

			case State.Patrol:
				agent.speed = patrolSpeed;
				if (searchEnded) { audioSource?.PlayOneShot(searchEnded); }
				break;
		}
	}

	private void tryKillHiddenPlayerInRange()
	{
		if (!player || !playerMovement) { return; }
		if (!playerWasSeenHiding || !playerMovement.CheckIfHiding()) { return; }
		if (Vector3.Distance(transform.position, player.position) > hiddenKillDistance) { return; }
		if (animator && animator.GetBool(isKilling)) { return; }

		if (playerMovement.CheckIfInCloset()) { killPlayer(highLookPoint, 1); }
		else if (playerMovement.CheckIfUnderBed()) { killPlayer(lowLookPoint, 2); }
	}

	private void updateStuckRecovery()
	{
		if (!agent || !agent.enabled || !agent.isOnNavMesh || agent.isStopped || !agent.hasPath || agent.pathPending)
		{
			stuckTimer = 0f;
			return;
		}

		float _remainingDistanceThreshold = Mathf.Max(minRemainingDistanceToConsiderStuck, agent.stoppingDistance);
		if (agent.remainingDistance <= _remainingDistanceThreshold || agent.velocity.sqrMagnitude > stuckVelocityThreshold * stuckVelocityThreshold)
		{
			stuckTimer = 0f;
			return;
		}

		stuckTimer += Time.deltaTime;
		if (stuckTimer < Mathf.Max(0.1f, stuckDuration)) { return; }
		stuckTimer = 0f;

		Vector3 _currentDestination = agent.destination;
		agent.ResetPath();

		if (NavMesh.SamplePosition(_currentDestination, out NavMeshHit _destinationHit, repathRadius, NavMesh.AllAreas))
		{
			agent.SetDestination(_destinationHit.position);
			return;
		}

		Vector2 _randomCircle = Random.insideUnitCircle * repathRadius;
		Vector3 _alternateDirection = transform.position + new Vector3(_randomCircle.x, 0f, _randomCircle.y);
		if (NavMesh.SamplePosition(_alternateDirection, out NavMeshHit _alternateHit, repathRadius, NavMesh.AllAreas)) { agent.SetDestination(_alternateHit.position); }
	}

	private bool canSeePlayer()
	{
		if (!player) { return false; }
		if (playerMovement && playerMovement.CheckIfHiding()) { return false; }

		Vector3 _visionOrigin = transform.position + visionOriginOffset;
		fillPlayerVisibilityPoints();

		float _halfFov = Mathf.Max(1f, currentFOV) * 0.5f;
		for (int _i = 0; _i < playerVisibilityPoints.Length; _i++)
		{
			Vector3 _directionToPoint = playerVisibilityPoints[_i] - _visionOrigin;
			float _distanceToPoint = _directionToPoint.magnitude;
			if (_distanceToPoint <= 0.01f || _distanceToPoint > sightRange) { continue; }

			float _angleToPoint = Vector3.Angle(transform.forward, _directionToPoint);
			if (_angleToPoint > _halfFov) { continue; }

			if (!Physics.Raycast(_visionOrigin, _directionToPoint.normalized, out RaycastHit _hit, _distanceToPoint)) { continue; }
			if (_hit.transform != player && !_hit.transform.IsChildOf(player)) { continue; }

			playerWasSeenHiding = true;
			return true;
		}

		return false;
	}

	private void fillPlayerVisibilityPoints()
	{
		Collider _playerCollider = player.GetComponent<Collider>() ?? player.GetComponentInChildren<Collider>();
		if (_playerCollider)
		{
			Bounds _playerBounds = _playerCollider.bounds;
			playerVisibilityPoints[0] = new Vector3(_playerBounds.center.x, _playerBounds.max.y, _playerBounds.center.z);
			playerVisibilityPoints[1] = _playerBounds.center;
			playerVisibilityPoints[2] = new Vector3(_playerBounds.center.x, _playerBounds.min.y + 0.1f, _playerBounds.center.z);
			return;
		}

		Vector3 _playerPosition = player.position;
		playerVisibilityPoints[0] = _playerPosition + Vector3.up * playerHeadOffset;
		playerVisibilityPoints[1] = _playerPosition + Vector3.up * playerTorsoOffset;
		playerVisibilityPoints[2] = _playerPosition + Vector3.up * playerLegOffset;
	}

	public void OnAnimationCompleted()
	{
		if (isLeaving) { Destroy(gameObject); }
		agent.speed = patrolSpeed;
		currentFOV = visionWidth;
	}

	private void OnCollisionEnter(Collision _collision)
	{
		if (_collision.gameObject.CompareTag("Player")) { killPlayer(highLookPoint, 0); }
	}

	private void killPlayer(Transform _lookPoint, int _killType)
	{
		if (!player || !playerMovement) { return; }
		_lookPoint ??= transform;

		//0 for front, 1 for closet, 2 for bed
		if (_killType > 0)
		{
			if (TryGetComponent(out Collider _collider)) { _collider.enabled = false; }
			if (TryGetComponent(out Rigidbody _rigidBody)) { _rigidBody.isKinematic = true; }
		}
		agent.velocity = Vector3.zero;
		agent.speed = 0f;
		agent.isStopped = true;
		transform.LookAt(player);
		Vector3 _eulerAngles = transform.eulerAngles;
		_eulerAngles.x = 0;
		_eulerAngles.z = 0;
		transform.eulerAngles = _eulerAngles;
		animator.SetBool(isKilling, true);
		animator.SetInteger(killType, _killType);
		playerMovement.lockPlayer(_lookPoint, 4f);
	}

	private void CallMenuOnAnimEnd()
	{
		playerMovement.InvokePlayerDeath();
	}

	private void OnDrawGizmos()
	{
		// Draw the FOV cone
		float _halfFOV = visionWidth / 2f;
		Quaternion _leftRayRotation = Quaternion.AngleAxis(-_halfFOV, Vector3.up);
		Quaternion _rightRayRotation = Quaternion.AngleAxis(_halfFOV, Vector3.up);
		Vector3 _leftRayDirection = _leftRayRotation * transform.forward;
		Vector3 _rightRayDirection = _rightRayRotation * transform.forward;

		Gizmos.color = Color.yellow;
		Gizmos.DrawRay(transform.position + Vector3.up, _leftRayDirection * sightRange);
		Gizmos.DrawRay(transform.position + Vector3.up, _rightRayDirection * sightRange);
	}
}