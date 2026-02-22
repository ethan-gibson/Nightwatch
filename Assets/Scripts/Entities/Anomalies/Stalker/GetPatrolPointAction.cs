using System;
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
	/// Number of random NavMesh samples attempted before fallback sampling at the origin.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<int> SampleAttempts = new(8);

	private BehaviorGraphAgent graphAgent;
	private BehaviorGraph cachedGraph;
	private Transform cachedTransform;
	private BlackboardVariable<Vector3> searchPointVariable;

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

		if (!trySamplePoint(_origin, _radius, _attempts, out Vector3 _patrolPoint)) { return Status.Failure; }

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

		return true;
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
	private static bool trySamplePoint(Vector3 _origin, float _radius, int _attempts, out Vector3 _point)
	{
		for (int _attempt = 0; _attempt < _attempts; _attempt++)
		{
			Vector2 _randomCircle = UnityEngine.Random.insideUnitCircle * _radius;
			Vector3 _randomDirection = _origin + new Vector3(_randomCircle.x, 0f, _randomCircle.y);
			if (!NavMesh.SamplePosition(_randomDirection, out NavMeshHit _hit, _radius, NavMesh.AllAreas)) { continue; }
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
}