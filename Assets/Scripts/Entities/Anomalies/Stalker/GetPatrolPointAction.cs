using System;
using System.Collections.Generic;
using Game.Entities.Octree;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

/// <summary>
/// Chooses a valid NavMesh patrol point around the agent and writes it into <c>SearchPoint</c>.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "GetPatrolPoint", story: "Sets a point to patrol to", category: "Action", id: "e6a422d2207565790bec466d30f39a10")]
public partial class GetPatrolPointAction : Action
{
	private const string searchPointVariableName = "SearchPoint";

	/// <summary>
	/// Radius around the agent used to sample patrol destinations.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> PatrolRadius = new(10f);
	/// <summary>
	/// Minimum distance from the agent a patrol point must be. Prevents picking nodes that are already underfoot.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> MinPatrolRadius = new(3f);
	/// <summary>
	/// Maximum vertical (Y) difference allowed between the agent and a candidate node.
	/// Prevents selecting nodes on unreachable floors. Set to 0 to disable.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> MaxVerticalDistance = new(2f);
	/// <summary>
	/// Number of random NavMesh samples attempted before fallback sampling at the origin.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<int> SampleAttempts = new(8);

	private BehaviorGraphAgent graphAgent;
	private BehaviorGraph cachedGraph;
	private Transform cachedTransform;
	private BlackboardVariable<Vector3> searchPointVariable;
	private PathFindingAgent pathfindingAgent;

	/// <summary>
	/// Samples a patrol point and stores it in the graph blackboard.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when a point is sampled and written; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheReferences()) { return Status.Failure; }

		Vector3 _origin = cachedTransform.position;
		float _radius = Mathf.Max(0.1f, PatrolRadius.Value);
		int _attempts = Mathf.Max(1, SampleAttempts.Value);

		if (!tryGetRandomNavMeshPoint(_origin, _radius, _attempts, out Vector3 _patrolPoint)
			&& !tryGetRandomOctreeNode(_origin, _radius, _attempts, out _patrolPoint))
		{
			return Status.Failure;
		}

		searchPointVariable.Value = _patrolPoint;
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
	/// Caches component and blackboard references used by this action.
	/// </summary>
	/// <returns>
	/// <c>true</c> when references are valid and the <c>SearchPoint</c> variable is available; otherwise <c>false</c>.
	/// </returns>
	private bool tryCacheReferences()
	{
		if (!GameObject) { return false; }

		if (!cachedTransform) { cachedTransform = GameObject.transform; }

		if (!graphAgent)
		{
			graphAgent = GameObject.GetComponent<BehaviorGraphAgent>();
			if (!graphAgent) { return false; }
		}

		if (!graphAgent.Graph) { return false; }

		if (!ReferenceEquals(cachedGraph, graphAgent.Graph))
		{
			cachedGraph = graphAgent.Graph;
			searchPointVariable = null;
		}

		if (searchPointVariable == null && !graphAgent.GetVariable(searchPointVariableName, out searchPointVariable)) { return false; }

		pathfindingAgent = GameObject.GetComponent<PathFindingAgent>();
		return true;
	}

	private bool tryGetRandomNavMeshPoint(Vector3 _origin, float _radius, int _attempts, out Vector3 _point)
	{
		float _minRadius = MinPatrolRadius != null ? Mathf.Max(0f, MinPatrolRadius.Value) : 0f;
		float _maxVertical = MaxVerticalDistance != null ? MaxVerticalDistance.Value : 0f;
		bool _useVerticalFilter = _maxVertical > 0f;

		for (int _attempt = 0; _attempt < _attempts; _attempt++)
		{
			Vector2 _randomCircle = UnityEngine.Random.insideUnitCircle * _radius;
			Vector3 _samplePosition = _origin + new Vector3(_randomCircle.x, 0f, _randomCircle.y);
			if (!NavMesh.SamplePosition(_samplePosition, out NavMeshHit _hit, _radius, NavMesh.AllAreas)) { continue; }
			if (_useVerticalFilter && Mathf.Abs(_hit.position.y - _origin.y) > _maxVertical) { continue; }

			Vector2 _originXZ = new Vector2(_origin.x, _origin.z);
			Vector2 _hitXZ = new Vector2(_hit.position.x, _hit.position.z);
			float _distance = Vector2.Distance(_originXZ, _hitXZ);
			if (_distance < _minRadius || _distance > _radius) { continue; }

			_point = _hit.position;
			return true;
		}

		if (NavMesh.SamplePosition(_origin, out NavMeshHit _fallbackHit, _radius, NavMesh.AllAreas))
		{
			_point = _fallbackHit.position;
			return true;
		}

		_point = _origin;
		return false;
	}

	/// <summary>
	/// Attempts to sample a reachable NavMesh point around an origin.
	/// </summary>
	/// <param name="_origin">Center point used for sampling attempts.</param>
	/// <param name="_radius">Sampling radius.</param>
	/// <param name="_attempts">Maximum number of random attempts.</param>
	/// <param name="_point">Resolved point when sampling succeeds.</param>
	/// <returns>
	/// <c>true</c> when a valid point is found; otherwise <c>false</c>.
	/// </returns>
	private bool tryGetRandomOctreeNode(Vector3 _origin, float _radius, int _attempts, out Vector3 _point)
	{
		if (pathfindingAgent == null)
		{
			_point = _origin;
			return false;
		}

		var _freeLeaves = pathfindingAgent.FreeLeaves;
		if (_freeLeaves == null || _freeLeaves.Count == 0)
		{
			_point = _origin;
			return false;
		}

		float _minRadius = MinPatrolRadius != null ? Mathf.Max(0f, MinPatrolRadius.Value) : 0f;
		float _maxVertical = MaxVerticalDistance != null ? MaxVerticalDistance.Value : 0f;
		bool _useVerticalFilter = _maxVertical > 0f;

		for (int _attempt = 0; _attempt < _attempts; _attempt++)
		{
			List<OctreeNode> _candidates = new List<OctreeNode>();
			for (int _leafIndex = 0; _leafIndex < _freeLeaves.Count; _leafIndex++)
			{
				OctreeNode _leafNode = _freeLeaves[_leafIndex];
				if (_leafNode.Neighbors.Count == 0) { continue; }
				if (_useVerticalFilter && Mathf.Abs(_leafNode.SamplePoint.y - _origin.y) > _maxVertical) { continue; }
				float _dist = Vector3.Distance(_leafNode.SamplePoint, _origin);
				if (_dist < _minRadius || _dist > _radius) { continue; }
				_candidates.Add(_leafNode);
			}

			if (_candidates.Count == 0) { continue; }

			int _randomIndex = UnityEngine.Random.Range(0, _candidates.Count);
			_point = _candidates[_randomIndex].SamplePoint;
			return true;
		}

		if (NavMesh.SamplePosition(_origin, out NavMeshHit _fallbackHit, _radius, NavMesh.AllAreas))
		{
			_point = _fallbackHit.position;
			return true;
		}

		_point = _origin;
		return false;
	}
}
