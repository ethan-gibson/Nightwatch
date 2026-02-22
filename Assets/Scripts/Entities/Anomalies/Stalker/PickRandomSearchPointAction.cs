using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

/// <summary>
/// Picks a random NavMesh search location around the last known player position and writes it to <c>SearchPoint</c>.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "PickRandomSearchPointAction", story: "Picks a random point to search", category: "Action", id: "3b8694717de0cfa9a0ec58fb377f0e10")]
public partial class PickRandomSearchPointAction : Action
{
	private const string searchPointVariableName = "SearchPoint";
	private const string lastKnownPositionVariableName = "LastKnownPosition";

	/// <summary>
	/// Radius around the search origin used for random search point sampling.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> SearchRadius = new(5f);
	/// <summary>
	/// Number of random NavMesh samples attempted before fallback sampling at the origin.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<int> SampleAttempts = new(10);

	private BehaviorGraphAgent graphAgent;
	private BehaviorGraph cachedGraph;
	private Transform cachedTransform;
	private BlackboardVariable<Vector3> searchPointVariable;
	private BlackboardVariable<Vector3> lastKnownPositionVariable;

	/// <summary>
	/// Selects and stores a search point for the search branch.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when a search point is written; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheReferences()) { return Status.Failure; }

		Vector3 _origin = cachedTransform.position;
		if (lastKnownPositionVariable != null) { _origin = lastKnownPositionVariable.Value; }

		float _radius = Mathf.Max(0.1f, SearchRadius.Value);
		int _attempts = Mathf.Max(1, SampleAttempts.Value);

		if (!trySamplePoint(_origin, _radius, _attempts, out Vector3 _searchPoint)) { return Status.Failure; }

		searchPointVariable.Value = _searchPoint;
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
	/// Caches component and blackboard references needed by this action.
	/// </summary>
	/// <returns>
	/// <c>true</c> when references are valid and <c>SearchPoint</c> is available; otherwise <c>false</c>.
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
			lastKnownPositionVariable = null;
		}

		if (searchPointVariable == null && !graphAgent.GetVariable(searchPointVariableName, out searchPointVariable)) { return false; }

		if (lastKnownPositionVariable == null) { graphAgent.GetVariable(lastKnownPositionVariableName, out lastKnownPositionVariable); }

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