using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Game.Entities.Octree
{
	public class PathFindingAgent : MonoBehaviour
	{
		private const float targetRepathDistance = 0.25f;
		private const float targetRepathInterval = 0.15f;

		private sealed class NodePriorityQueue
		{
			private readonly struct Entry
			{
				public readonly OctreeNode Node;
				public readonly float Priority;

				public Entry(OctreeNode _node, float _priority)
				{
					Node = _node;
					Priority = _priority;
				}
			}

			private readonly List<Entry> entries = new List<Entry>();

			public int Count => entries.Count;

			public void Enqueue(OctreeNode _node, float _priority)
			{
				Entry _entry = new Entry(_node, _priority);
				entries.Add(_entry);
				siftUp(entries.Count - 1);
			}

			public OctreeNode Dequeue()
			{
				Entry _root = entries[0];
				int _lastIndex = entries.Count - 1;
				entries[0] = entries[_lastIndex];
				entries.RemoveAt(_lastIndex);

				if (entries.Count > 0)
				{
					siftDown(0);
				}

				return _root.Node;
			}

			private void siftUp(int _index)
			{
				while (_index > 0)
				{
					int _parentIndex = (_index - 1) / 2;
					if (entries[_parentIndex].Priority <= entries[_index].Priority) { return; }

					Entry _parentEntry = entries[_parentIndex];
					entries[_parentIndex] = entries[_index];
					entries[_index] = _parentEntry;
					_index = _parentIndex;
				}
			}

			private void siftDown(int _index)
			{
				while (true)
				{
					int _leftChildIndex = (_index * 2) + 1;
					int _rightChildIndex = _leftChildIndex + 1;
					int _smallestIndex = _index;

					if (_leftChildIndex < entries.Count && entries[_leftChildIndex].Priority < entries[_smallestIndex].Priority)
					{
						_smallestIndex = _leftChildIndex;
					}

					if (_rightChildIndex < entries.Count && entries[_rightChildIndex].Priority < entries[_smallestIndex].Priority)
					{
						_smallestIndex = _rightChildIndex;
					}

					if (_smallestIndex == _index) { return; }

					Entry _entry = entries[_smallestIndex];
					entries[_smallestIndex] = entries[_index];
					entries[_index] = _entry;
					_index = _smallestIndex;
				}
			}
		}

		private enum NavigationMode
		{
			None,
			NavMesh,
			Octree
		}

		public Transform Target;

		[SerializeField]
		private float agentRadius = 0.4f;
		[SerializeField]
		private float agentHeight = 2f;
		[SerializeField]
		private float moveSpeed = 3f;
		[SerializeField]
		private LayerMask obstacleLayerMask;
		[SerializeField]
		private LayerMask groundLayerMask;
		[SerializeField]
		private float groundCheckDistance = 2f;
		[SerializeField]
		private Vector3 octreeOrigin = new Vector3(-6f, 0f, 6f);
		[SerializeField]
		private Vector3 octreeSize = new Vector3(30f, 20f, 30f);
		[SerializeField]
		private float pointResolveDistance = 4f;
		[SerializeField]
		private float waypointDistance = 0.1f;
		[SerializeField]
		private bool constrainMovementToGround = true;
		[SerializeField]
		[FormerlySerializedAs("preferNavMesh")]
		private bool preferOctree = true;
		[SerializeField]
		[FormerlySerializedAs("allowOctreeFallback")]
		private bool allowNavMeshFallback = true;
		[SerializeField]
		private float navMeshAttachDistance = 2f;
		[SerializeField]
		private float navMeshDestinationSampleDistance = 2f;

		private Octree octree;
		private readonly List<Vector3> path = new List<Vector3>();
		private int currentPathIndex;
		private bool hasDestination;
		private Vector3 lastRequestedTargetPosition;
		private bool hasRequestedTargetPosition;
		private float nextTargetRepathTime;
		private StalkerAnimationEvents stalkerAnimationEvents;
		private NavMeshAgent navMeshAgent;
		private StalkerMovementAuthority movementAuthority = StalkerMovementAuthority.Octree;
		private NavigationMode activeNavigationMode;
		private Vector3 navMeshDestination;
		private bool hasNavMeshDestination;
		private OctreeDebugger octreeDebugger;

		public bool IsMoving { get; private set; }
		public bool IsMovementLocked => movementAuthority == StalkerMovementAuthority.AnimationLocked || isAnimationMovementLocked();
		public bool IsPathPending => activeNavigationMode == NavigationMode.NavMesh && navMeshAgent && navMeshAgent.enabled && navMeshAgent.isOnNavMesh && navMeshAgent.pathPending;
		public bool LastRequestSucceeded { get; private set; }
		public bool HasPath => activeNavigationMode == NavigationMode.NavMesh ? hasActiveNavMeshPath() : hasOctreePath();
		public StalkerMovementAuthority MovementAuthority => movementAuthority;
		public bool IsUsingNavMeshMovement => movementAuthority == StalkerMovementAuthority.NavMeshLeave || activeNavigationMode == NavigationMode.NavMesh;
		public float MoveSpeed
		{
			get => moveSpeed;
			set => moveSpeed = Mathf.Max(0f, value);
		}
		public float CurrentSpeed { get; private set; }
		public Vector3 Destination { get; private set; }
		public IReadOnlyList<Vector3> Path => path;
		public IReadOnlyList<OctreeNode> FreeLeaves => octree == null ? (IReadOnlyList<OctreeNode>)System.Array.Empty<OctreeNode>() : octree.GetAllFreeLeaves();

		private void Awake()
		{
			ensureAnimationEvents();
			navMeshAgent = GetComponent<NavMeshAgent>();
			ensureRuntimeDriver();
			ensureOctree();
			updateNavMeshAuthorityState();
		}

		private void OnEnable()
		{
			OctreeDoorEvents.DoorStateChanged += handleDoorStateChanged;
			updateNavMeshAuthorityState();
		}

		private void OnDisable()
		{
			OctreeDoorEvents.DoorStateChanged -= handleDoorStateChanged;
		}

		private void Update()
		{
			if (movementAuthority != StalkerMovementAuthority.Octree)
			{
				CurrentSpeed = 0f;
				return;
			}

			bool _hasDynamicTarget = false;
			if (Target)
			{
				_hasDynamicTarget = true;
				Vector3 _targetPosition = Target.position;
				Destination = _targetPosition;
				hasDestination = true;

				if (!isAnimationMovementLocked())
				{
					float _targetMovedDistanceSqr = getPlanarDistanceSqr(_targetPosition, lastRequestedTargetPosition);
					bool _targetMoved = !hasRequestedTargetPosition || _targetMovedDistanceSqr >= targetRepathDistance * targetRepathDistance;
					bool _shouldRepathToTarget = (!IsMoving || !HasPath || !LastRequestSucceeded || (_targetMoved && Time.time >= nextTargetRepathTime))
						&& !IsAtDestination(_targetPosition, waypointDistance);
					if (_shouldRepathToTarget)
					{
						RequestPath(_targetPosition);
					}
				}
			}

			if (isAnimationMovementLocked())
			{
				pauseNavMeshMovement();
				CurrentSpeed = 0f;
				return;
			}

			resumeNavMeshMovement();

			if (!_hasDynamicTarget && hasDestination && !IsMoving && !HasPath && !LastRequestSucceeded && !IsAtDestination(Destination, waypointDistance))
			{
				RequestPath(Destination);
			}

			if (activeNavigationMode == NavigationMode.NavMesh)
			{
				updateNavMeshMovement();
			}
			else if (hasOctreePath())
			{
				moveAlongPath();
			}
			else if (IsMoving)
			{
				finishMovement(true);
			}
		}

		public bool RequestPath(Vector3 _targetPos)
		{
			if (movementAuthority != StalkerMovementAuthority.Octree) { return false; }

			Destination = _targetPos;
			hasDestination = true;
			lastRequestedTargetPosition = _targetPos;
			hasRequestedTargetPosition = true;
			nextTargetRepathTime = Time.time + targetRepathInterval;

			if (preferOctree)
			{
				if (requestOctreePath(_targetPos)) { return true; }
				if (allowNavMeshFallback && tryRequestNavMeshPath(_targetPos)) { return true; }
			}
			else
			{
				if (tryRequestNavMeshPath(_targetPos)) { return true; }
				if (requestOctreePath(_targetPos)) { return true; }
			}

			if (HasPath || IsMoving)
			{
				LastRequestSucceeded = true;
				return true;
			}

			movementFailed();
			return false;
		}

		private bool requestOctreePath(Vector3 _targetPos)
		{
			ensureOctree();
			float _movementPlaneY = transform.position.y;

			OctreeNode _startNode = resolveNode(transform.position);
			OctreeNode _goalNode = resolveNode(_targetPos);
			if (_startNode == null || _goalNode == null) { return false; }

			if (_startNode == _goalNode)
			{
				Vector3 _finalPoint = getFinalWaypoint(_goalNode, _targetPos, _movementPlaneY);
				setPathToSinglePoint(_finalPoint);
				return true;
			}

			List<OctreeNode> _nodePath = aStar(_startNode, _goalNode);
			if (_nodePath == null || _nodePath.Count == 0) { return false; }

			List<Vector3> _movementPath = buildMovementPath(_nodePath, _targetPos, _goalNode, _movementPlaneY);
			if (_movementPath.Count == 0) { return false; }

			activeNavigationMode = NavigationMode.Octree;
			stopNavMeshMovement(_resetPath: true);
			hasNavMeshDestination = false;
			clearOctreePathState();
			for (int _i = 0; _i < _movementPath.Count; _i++)
			{
				path.Add(_movementPath[_i]);
			}
			updateNavMeshAuthorityState();
			LastRequestSucceeded = true;
			advancePastReachedWaypoints();
			IsMoving = hasOctreePath();
			if (!IsMoving)
			{
				finishMovement(true);
			}

			return true;
		}

		public bool SetDestination(Vector3 _destination)
		{
			Destination = _destination;
			hasDestination = true;
			if (movementAuthority == StalkerMovementAuthority.NavMeshLeave)
			{
				LastRequestSucceeded = false;
				return false;
			}

			if (movementAuthority == StalkerMovementAuthority.AnimationLocked || isAnimationMovementLocked())
			{
				LastRequestSucceeded = false;
				return true;
			}

			return RequestPath(_destination);
		}

		public bool TryRecoverMovement(float _destinationSampleRadius = 0f)
		{
			if (movementAuthority != StalkerMovementAuthority.Octree || isAnimationMovementLocked()) { return false; }
			if (!tryGetRecoveryTarget(out Vector3 _recoveryTarget)) { return false; }

			resetMovementStateForRecovery();
			if (RequestPath(_recoveryTarget)) { return true; }

			float _resolvedSampleRadius = Mathf.Max(_destinationSampleRadius, waypointDistance);
			if (!tryResolveRecoveryDestination(_recoveryTarget, _resolvedSampleRadius, out Vector3 _recoveryDestination)) { return false; }

			resetMovementStateForRecovery();
			return RequestPath(_recoveryDestination);
		}

		public void StopMoving()
		{
			stopNavMeshMovement(_resetPath: true);
			activeNavigationMode = NavigationMode.None;
			hasNavMeshDestination = false;
			path.Clear();
			currentPathIndex = 0;
			IsMoving = false;
			CurrentSpeed = 0f;
			if (!Target)
			{
				hasDestination = false;
			}

			updateNavMeshAuthorityState();
		}

		public bool IsAtDestination(Vector3 _position, float _distance)
		{
			if (!constrainMovementToGround) { return Vector3.Distance(transform.position, _position) <= _distance; }

			Vector2 _currentPosition = new Vector2(transform.position.x, transform.position.z);
			Vector2 _targetPosition = new Vector2(_position.x, _position.z);
			return Vector2.Distance(_currentPosition, _targetPosition) <= _distance;
		}

		public void SetMovementAuthority(StalkerMovementAuthority _authority)
		{
			if (movementAuthority == _authority) { return; }

			movementAuthority = _authority;
			CurrentSpeed = 0f;

			if (_authority == StalkerMovementAuthority.NavMeshLeave)
			{
				clearNavigationState(_clearDestination: true);
			}
			else if (_authority == StalkerMovementAuthority.AnimationLocked)
			{
				clearNavigationState(_clearDestination: false);
			}
			else
			{
				stopNavMeshMovement(_resetPath: true);
				activeNavigationMode = NavigationMode.None;
				hasNavMeshDestination = false;
				LastRequestSucceeded = false;
				hasRequestedTargetPosition = false;
				nextTargetRepathTime = 0f;
			}

			updateNavMeshAuthorityState();
		}

		private void ensureOctree()
		{
			if (octree != null) { return; }

			obstacleLayerMask |= LayerMask.GetMask("Wall", "Obstacle");

			if (groundLayerMask.value == 0)
			{
				groundLayerMask = LayerMask.GetMask("Ground");
			}

			Bounds _bounds = new Bounds(octreeOrigin, octreeSize);
			int _maxDepth = 7;
			float _resolvedMinLeafSize = Mathf.Max(0.1f, agentRadius * 0.5f);
			octree = new Octree(_bounds, _maxDepth, _resolvedMinLeafSize, agentRadius, agentHeight, obstacleLayerMask, groundLayerMask, groundCheckDistance);

			if (!TryGetComponent(out OctreeDebugger _octreeDebugger)) { return; }
			octreeDebugger =  _octreeDebugger;
			octreeDebugger.SetOctree(octree);
		}

		private void ensureRuntimeDriver()
		{
			if (!TryGetComponent<BehaviorGraphAgent>(out _)) { return; }
			if (!TryGetComponent<Animator>(out _)) { return; }
			if (TryGetComponent<StalkerRuntimeDriver>(out _)) { return; }

			gameObject.AddComponent<StalkerRuntimeDriver>();
		}

		private void ensureAnimationEvents()
		{
			if (!TryGetComponent<BehaviorGraphAgent>(out _)) { return; }
			if (!TryGetComponent<Animator>(out _)) { return; }
			if (TryGetComponent<StalkerAnimationEvents>(out _)) { return; }

			gameObject.AddComponent<StalkerAnimationEvents>();
		}

		private void updateNavMeshAuthorityState()
		{
			navMeshAgent ??= GetComponent<NavMeshAgent>();
			if (!navMeshAgent) { return; }

			if (movementAuthority == StalkerMovementAuthority.NavMeshLeave)
			{
				if (!navMeshAgent.enabled) { navMeshAgent.enabled = true; }
				return;
			}

			if (activeNavigationMode == NavigationMode.NavMesh)
			{
				if (!navMeshAgent.enabled) { navMeshAgent.enabled = true; }
				if (navMeshAgent.isOnNavMesh)
				{
					bool _movementLocked = movementAuthority == StalkerMovementAuthority.AnimationLocked || isAnimationMovementLocked();
					navMeshAgent.isStopped = _movementLocked;
					if (_movementLocked)
					{
						navMeshAgent.velocity = Vector3.zero;
					}
				}
				return;
			}

			if (!navMeshAgent.enabled) { return; }
			if (navMeshAgent.isOnNavMesh)
			{
				navMeshAgent.isStopped = true;
				navMeshAgent.ResetPath();
				navMeshAgent.velocity = Vector3.zero;
			}

			navMeshAgent.enabled = false;
		}

		private OctreeNode resolveNode(Vector3 _point)
		{
			OctreeNode _leafNode = octree.FindLeafAtPoint(_point);
			if (_leafNode != null && !_leafNode.IsOccupied && _leafNode.Neighbors.Count > 0)
			{
				return _leafNode;
			}

			return octree.FindNearestConnectedFreeLeaf(_point, pointResolveDistance);
		}

		private List<OctreeNode> aStar(OctreeNode _startNode, OctreeNode _goalNode)
		{
			NodePriorityQueue _openQueue = new NodePriorityQueue();
			HashSet<OctreeNode> _closedSet = new HashSet<OctreeNode>();
			Dictionary<OctreeNode, OctreeNode> _cameFrom = new Dictionary<OctreeNode, OctreeNode>();
			Dictionary<OctreeNode, float> _gScore = new Dictionary<OctreeNode, float>();

			_gScore[_startNode] = 0f;
			_openQueue.Enqueue(_startNode, getHeuristic(_startNode, _goalNode));

			while (_openQueue.Count > 0)
			{
				OctreeNode _currentNode = _openQueue.Dequeue();
				if (_closedSet.Contains(_currentNode)) { continue; }

				if (_currentNode == _goalNode)
				{
					return reconstructPath(_cameFrom, _currentNode);
				}

				_closedSet.Add(_currentNode);

				for (int _i = 0; _i < _currentNode.Neighbors.Count; _i++)
				{
					OctreeNode _neighbourNode = _currentNode.Neighbors[_i];
					if (_closedSet.Contains(_neighbourNode)) { continue; }

					float _tentativeScore = _gScore[_currentNode] + Vector3.Distance(_currentNode.SamplePoint, _neighbourNode.SamplePoint);
					if (_gScore.TryGetValue(_neighbourNode, out float _bestKnownScore) && _tentativeScore >= _bestKnownScore) { continue; }

					_cameFrom[_neighbourNode] = _currentNode;
					_gScore[_neighbourNode] = _tentativeScore;
					float _priority = _tentativeScore + getHeuristic(_neighbourNode, _goalNode);
					_openQueue.Enqueue(_neighbourNode, _priority);
				}
			}

			return null;
		}

		private float getHeuristic(OctreeNode _currentNode, OctreeNode _goalNode)
		{
			return Vector3.Distance(_currentNode.SamplePoint, _goalNode.SamplePoint);
		}

		private List<OctreeNode> reconstructPath(Dictionary<OctreeNode, OctreeNode> _cameFrom, OctreeNode _currentNode)
		{
			List<OctreeNode> _nodePath = new List<OctreeNode>();
			_nodePath.Add(_currentNode);

			while (_cameFrom.TryGetValue(_currentNode, out OctreeNode _previousNode))
			{
				_currentNode = _previousNode;
				_nodePath.Add(_currentNode);
			}

			_nodePath.Reverse();
			return _nodePath;
		}

		private List<Vector3> buildMovementPath(List<OctreeNode> _nodePath, Vector3 _targetPos, OctreeNode _goalNode, float _movementPlaneY)
		{
			List<Vector3> _smoothedPath = smoothPath(_nodePath);
			if (_smoothedPath.Count == 0)
			{
				_smoothedPath.Add(_goalNode.SamplePoint);
			}

			for (int _i = 0; _i < _smoothedPath.Count; _i++)
			{
				_smoothedPath[_i] = getMovementPoint(_smoothedPath[_i], _movementPlaneY);
			}

			Vector3 _resolvedFinalPoint = getFinalWaypoint(_goalNode, _targetPos, _movementPlaneY);
			Vector3 _lastPoint = _smoothedPath[_smoothedPath.Count - 1];
			if ((_resolvedFinalPoint - _lastPoint).sqrMagnitude > waypointDistance * waypointDistance)
			{
				_smoothedPath.Add(_resolvedFinalPoint);
			}

			return _smoothedPath;
		}

		private List<Vector3> smoothPath(List<OctreeNode> _nodePath)
		{
			List<Vector3> _smoothedPath = new List<Vector3>();
			if (_nodePath.Count == 0) { return _smoothedPath; }
			if (_nodePath.Count == 1)
			{
				_smoothedPath.Add(_nodePath[0].SamplePoint);
				return _smoothedPath;
			}

			int _currentIndex = 0;
			while (_currentIndex < _nodePath.Count - 1)
			{
				// Default to the immediate next A* node — always valid since A* edges
				// were verified by HasClearPath at graph build time.
				int _nextIndex = _currentIndex + 1;
				for (int _candidateIndex = _nodePath.Count - 1; _candidateIndex > _currentIndex + 1; _candidateIndex--)
				{
					if (!octree.HasClearPath(_nodePath[_currentIndex].SamplePoint, _nodePath[_candidateIndex].SamplePoint)) { continue; }
					_nextIndex = _candidateIndex;
					break;
				}

				_smoothedPath.Add(_nodePath[_nextIndex].SamplePoint);
				_currentIndex = _nextIndex;
			}

			return _smoothedPath;
		}

		private void moveAlongPath()
		{
			advancePastReachedWaypoints();
			if (!hasOctreePath())
			{
				finishMovement(true);
				return;
			}

			Vector3 _targetPoint = path[currentPathIndex];
			Vector3 _direction = _targetPoint - transform.position;
			float _distanceToWaypoint = _direction.magnitude;
			float _stepDistance = moveSpeed * Time.deltaTime;
			if (_distanceToWaypoint <= _stepDistance)
			{
				transform.position = _targetPoint;
				currentPathIndex++;
				advancePastReachedWaypoints();
				if (!hasOctreePath())
				{
					finishMovement(true);
					return;
				}

				CurrentSpeed = moveSpeed;
				return;
			}

			Vector3 _step = _direction.normalized * _stepDistance;
			transform.position += _step;
			CurrentSpeed = moveSpeed;

			Vector3 _flatDirection = new Vector3(_direction.x, 0f, _direction.z);
			if (_flatDirection.sqrMagnitude > 0.0001f)
			{
				transform.rotation = Quaternion.LookRotation(_flatDirection, Vector3.up);
			}
		}

		private void advancePastReachedWaypoints()
		{
			float _waypointDistanceSqr = waypointDistance * waypointDistance;

			while (currentPathIndex < path.Count)
			{
				Vector3 _waypointDirection = path[currentPathIndex] - transform.position;
				if (_waypointDirection.sqrMagnitude > _waypointDistanceSqr) { break; }
				currentPathIndex++;
			}
		}

		private void finishMovement(bool _wasSuccessful)
		{
			LastRequestSucceeded = _wasSuccessful;
			StopMoving();
		}

		private void movementFailed()
		{
			LastRequestSucceeded = false;
			StopMoving();
		}

		private void setPathToSinglePoint(Vector3 _point)
		{
			stopNavMeshMovement(_resetPath: true);
			activeNavigationMode = NavigationMode.Octree;
			hasNavMeshDestination = false;
			updateNavMeshAuthorityState();
			clearOctreePathState();
			path.Add(_point);
			LastRequestSucceeded = true;
			advancePastReachedWaypoints();
			IsMoving = hasOctreePath();
			if (!IsMoving)
			{
				finishMovement(true);
			}
		}

		private void handleDoorStateChanged(Bounds _doorBounds)
		{
			if (octree == null) { return; }

			octree.RebuildRegion(_doorBounds);
			if (octreeDebugger != null)
			{
				octreeDebugger.SetOctree(octree);
			}
			if (movementAuthority != StalkerMovementAuthority.Octree || activeNavigationMode != NavigationMode.Octree || isAnimationMovementLocked() || !hasDestination || (!IsMoving && !hasOctreePath())) { return; }

			RequestPath(Destination);
		}

		private void clearNavigationState(bool _clearDestination)
		{
			Target = null;
			stopNavMeshMovement(_resetPath: true);
			activeNavigationMode = NavigationMode.None;
			hasNavMeshDestination = false;
			path.Clear();
			currentPathIndex = 0;
			IsMoving = false;
			CurrentSpeed = 0f;
			LastRequestSucceeded = false;
			hasRequestedTargetPosition = false;
			nextTargetRepathTime = 0f;

			if (_clearDestination)
			{
				hasDestination = false;
			}
		}

		private void resetMovementStateForRecovery()
		{
			stopNavMeshMovement(_resetPath: true);
			activeNavigationMode = NavigationMode.None;
			hasNavMeshDestination = false;
			clearOctreePathState();
			IsMoving = false;
			CurrentSpeed = 0f;
			LastRequestSucceeded = false;
			hasRequestedTargetPosition = false;
			nextTargetRepathTime = 0f;
			updateNavMeshAuthorityState();
		}

		private bool tryGetRecoveryTarget(out Vector3 _recoveryTarget)
		{
			if (Target)
			{
				_recoveryTarget = Target.position;
				return true;
			}

			if (hasDestination)
			{
				_recoveryTarget = Destination;
				return true;
			}

			_recoveryTarget = Vector3.zero;
			return false;
		}

		private bool tryResolveRecoveryDestination(Vector3 _targetPos, float _sampleRadius, out Vector3 _recoveryDestination)
		{
			if (NavMesh.SamplePosition(_targetPos, out NavMeshHit _navMeshHit, _sampleRadius, NavMesh.AllAreas))
			{
				_recoveryDestination = _navMeshHit.position;
				return true;
			}

			ensureOctree();
			OctreeNode _recoveryNode = octree.FindNearestConnectedFreeLeaf(_targetPos, Mathf.Max(_sampleRadius, pointResolveDistance));
			if (_recoveryNode != null)
			{
				_recoveryDestination = getFinalWaypoint(_recoveryNode, _targetPos, transform.position.y);
				return true;
			}

			_recoveryDestination = Vector3.zero;
			return false;
		}

		private void updateNavMeshMovement()
		{
			if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
			{
				if (tryRepathAfterNavMeshFailure()) { return; }

				movementFailed();
				return;
			}

			if (navMeshAgent.pathPending)
			{
				IsMoving = true;
				CurrentSpeed = 0f;
				return;
			}

			if (navMeshAgent.pathStatus == NavMeshPathStatus.PathInvalid || navMeshAgent.pathStatus == NavMeshPathStatus.PathPartial)
			{
				if (tryRepathAfterNavMeshFailure()) { return; }

				movementFailed();
				return;
			}

			CurrentSpeed = resolveNavMeshSpeed();
			float _arrivalDistance = Mathf.Max(waypointDistance, navMeshAgent.stoppingDistance);
			bool _arrivedAtResolvedDestination = hasNavMeshDestination && IsAtDestination(navMeshDestination, _arrivalDistance);
			if (_arrivedAtResolvedDestination || (!navMeshAgent.hasPath && navMeshAgent.remainingDistance <= _arrivalDistance))
			{
				finishMovement(true);
				return;
			}

			IsMoving = navMeshAgent.hasPath || navMeshAgent.remainingDistance > _arrivalDistance || CurrentSpeed > 0.01f;
			if (!IsMoving)
			{
				finishMovement(true);
			}
		}

		private bool tryRequestNavMeshPath(Vector3 _targetPos)
		{
			navMeshAgent ??= GetComponent<NavMeshAgent>();
			if (!navMeshAgent) { return false; }
			if (!tryPrepareNavMeshForMovement()) { return false; }
			if (!tryResolveNavMeshDestination(_targetPos, out Vector3 _resolvedDestination)) { return false; }

			NavMeshPath _candidatePath = new NavMeshPath();
			if (!navMeshAgent.CalculatePath(_resolvedDestination, _candidatePath) || _candidatePath.status != NavMeshPathStatus.PathComplete)
			{
				return false;
			}

			activeNavigationMode = NavigationMode.NavMesh;
			hasNavMeshDestination = true;
			navMeshDestination = _resolvedDestination;
			clearOctreePathState();
			CurrentSpeed = 0f;
			LastRequestSucceeded = true;
			updateNavMeshAuthorityState();

			float _arrivalDistance = Mathf.Max(waypointDistance, navMeshAgent.stoppingDistance);
			if (IsAtDestination(_resolvedDestination, _arrivalDistance))
			{
				activeNavigationMode = NavigationMode.None;
				hasNavMeshDestination = false;
				updateNavMeshAuthorityState();
				IsMoving = false;
				return true;
			}

			if (!navMeshAgent.SetDestination(_resolvedDestination))
			{
				activeNavigationMode = NavigationMode.None;
				hasNavMeshDestination = false;
				updateNavMeshAuthorityState();
				return false;
			}

			IsMoving = true;
			return true;
		}

		private bool tryPrepareNavMeshForMovement()
		{
			if (!navMeshAgent.enabled) { navMeshAgent.enabled = true; }
			if (navMeshAgent.isOnNavMesh) { return true; }

			float _attachDistance = Mathf.Max(navMeshAttachDistance, waypointDistance);
			if (!NavMesh.SamplePosition(transform.position, out NavMeshHit _navMeshHit, _attachDistance, NavMesh.AllAreas))
			{
				return false;
			}

			return navMeshAgent.Warp(_navMeshHit.position);
		}

		private bool tryResolveNavMeshDestination(Vector3 _targetPos, out Vector3 _resolvedDestination)
		{
			float _sampleDistance = Mathf.Max(navMeshDestinationSampleDistance, waypointDistance);
			if (NavMesh.SamplePosition(_targetPos, out NavMeshHit _navMeshHit, _sampleDistance, NavMesh.AllAreas))
			{
				_resolvedDestination = _navMeshHit.position;
				return true;
			}

			_resolvedDestination = Vector3.zero;
			return false;
		}

		private bool hasActiveNavMeshPath()
		{
			if (!navMeshAgent || !navMeshAgent.enabled) { return false; }
			if (!navMeshAgent.isOnNavMesh) { return false; }
			if (navMeshAgent.pathPending || navMeshAgent.hasPath) { return true; }

			float _arrivalDistance = Mathf.Max(waypointDistance, navMeshAgent.stoppingDistance);
			return activeNavigationMode == NavigationMode.NavMesh && navMeshAgent.remainingDistance > _arrivalDistance;
		}

		private bool hasOctreePath()
		{
			return currentPathIndex < path.Count;
		}

		private float resolveNavMeshSpeed()
		{
			if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh || navMeshAgent.isStopped) { return 0f; }

			float _velocitySqrMagnitude = navMeshAgent.velocity.sqrMagnitude;
			if (_velocitySqrMagnitude <= 0.0001f && navMeshAgent.hasPath && !navMeshAgent.pathPending)
			{
				_velocitySqrMagnitude = navMeshAgent.desiredVelocity.sqrMagnitude;
			}

			return _velocitySqrMagnitude > 0f ? Mathf.Sqrt(_velocitySqrMagnitude) : 0f;
		}

		private void pauseNavMeshMovement()
		{
			if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh) { return; }
			if (activeNavigationMode != NavigationMode.NavMesh && movementAuthority != StalkerMovementAuthority.NavMeshLeave) { return; }

			navMeshAgent.isStopped = true;
			navMeshAgent.velocity = Vector3.zero;
		}

		private void resumeNavMeshMovement()
		{
			if (!navMeshAgent || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh) { return; }
			if (activeNavigationMode != NavigationMode.NavMesh) { return; }
			if (movementAuthority != StalkerMovementAuthority.Octree || isAnimationMovementLocked()) { return; }

			navMeshAgent.isStopped = false;
		}

		private void stopNavMeshMovement(bool _resetPath)
		{
			if (!navMeshAgent || !navMeshAgent.enabled) { return; }
			if (navMeshAgent.isOnNavMesh)
			{
				navMeshAgent.isStopped = true;
				if (_resetPath)
				{
					navMeshAgent.ResetPath();
				}
				navMeshAgent.velocity = Vector3.zero;
			}
		}

		private bool tryRepathAfterNavMeshFailure()
		{
			if (!hasDestination) { return false; }

			stopNavMeshMovement(_resetPath: true);
			activeNavigationMode = NavigationMode.None;
			hasNavMeshDestination = false;
			updateNavMeshAuthorityState();
			return RequestPath(Destination);
		}

		private void clearOctreePathState()
		{
			path.Clear();
			currentPathIndex = 0;
		}

		private bool isAnimationMovementLocked()
		{
			stalkerAnimationEvents ??= GetComponent<StalkerAnimationEvents>();
			return stalkerAnimationEvents && stalkerAnimationEvents.IsMovementLocked;
		}

		private Vector3 getMovementPoint(Vector3 _point, float _movementPlaneY)
		{
			if (!constrainMovementToGround) { return _point; }
			return new Vector3(_point.x, _movementPlaneY, _point.z);
		}

		private Vector3 getFinalWaypoint(OctreeNode _goalNode, Vector3 _targetPos, float _movementPlaneY)
		{
			if (!constrainMovementToGround)
			{
				return octree.HasClearPath(transform.position, _targetPos) ? _targetPos : _goalNode.SamplePoint;
			}

			Vector3 _goalSamplePoint = getMovementPoint(_goalNode.SamplePoint, _movementPlaneY);
			Vector3 _targetPoint = getMovementPoint(_targetPos, _movementPlaneY);
			Vector3 _checkTargetPoint = new Vector3(_targetPos.x, _goalNode.SamplePoint.y, _targetPos.z);
			if (!octree.HasClearPath(_goalNode.SamplePoint, _checkTargetPoint)) { return _goalSamplePoint; }
			return _targetPoint;
		}

		private static float getPlanarDistanceSqr(Vector3 _a, Vector3 _b)
		{
			float _deltaX = _a.x - _b.x;
			float _deltaZ = _a.z - _b.z;
			return (_deltaX * _deltaX) + (_deltaZ * _deltaZ);
		}
	}
}
