using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

/// <summary>
/// Applies per-state movement speed and one-shot audio cues for the stalker.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ApplyStalkerStateEffectsAction", story: "Applies stalker state speed/audio", category: "Action", id: "f2ecdd0f3ba543d3be1dd53d8b8f5f3d")]
public partial class ApplyStalkerStateEffectsAction : Action
{
	private const string stateVariableName = "State";
	private const string chaseSpeedVariableName = "ChaseSpeed";
	private const string searchSpeedVariableName = "SearchSpeed";
	private const string patrolSpeedVariableName = "PatrolSpeed";

	/// <summary>
	/// Audio cue played when transitioning into <see cref="State.Chasing"/>.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<AudioClip> AlertedNoise;

	/// <summary>
	/// Audio cue played when transitioning into <see cref="State.Searching"/>.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<AudioClip> SearchStarted;

	/// <summary>
	/// Audio cue played when transitioning into <see cref="State.Patrol"/>.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<AudioClip> SearchEnded;

	/// <summary>
	/// Minimum speed clamped on the <see cref="NavMeshAgent"/>.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> MinimumAgentSpeed = new(0.1f);

	private BehaviorGraphAgent graphAgent;
	private BehaviorGraph cachedGraph;
	private NavMeshAgent navMeshAgent;
	private AudioSource audioSource;
	private BlackboardVariable<State> stateVariable;
	private BlackboardVariable<float> chaseSpeedVariable;
	private BlackboardVariable<float> searchSpeedVariable;
	private BlackboardVariable<float> patrolSpeedVariable;
	private State lastState;
	private bool stateInitialized;

	/// <summary>
	/// Applies speed and transition audio according to the current graph state.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when references are valid; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheReferences()) { return Status.Failure; }

		State _state = stateVariable.Value;
		float _minimumSpeed = MinimumAgentSpeed != null ? MinimumAgentSpeed.Value : 0.1f;
		float _targetSpeed = Mathf.Max(_minimumSpeed, resolveSpeedForState(_state));
		if (navMeshAgent) { navMeshAgent.speed = _targetSpeed; }

		if (!stateInitialized || _state != lastState)
		{
			stateInitialized = true;
			lastState = _state;
			playStateTransitionCue(_state);
		}

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
	/// Caches graph variables and components required for state-effect application.
	/// </summary>
	/// <returns><c>true</c> when all required references are available; otherwise <c>false</c>.</returns>
	private bool tryCacheReferences()
	{
		if (!GameObject) { return false; }

		navMeshAgent ??= GameObject.GetComponent<NavMeshAgent>();
		audioSource ??= GameObject.GetComponent<AudioSource>();

		if (!graphAgent)
		{
			graphAgent = GameObject.GetComponent<BehaviorGraphAgent>();
			if (!graphAgent) { return false; }
		}

		if (!graphAgent.Graph) { return false; }

		if (!ReferenceEquals(cachedGraph, graphAgent.Graph))
		{
			cachedGraph = graphAgent.Graph;
			stateVariable = null;
			chaseSpeedVariable = null;
			searchSpeedVariable = null;
			patrolSpeedVariable = null;
			stateInitialized = false;
		}

		if (stateVariable == null && !graphAgent.GetVariable(stateVariableName, out stateVariable)) { return false; }
		if (chaseSpeedVariable == null && !graphAgent.GetVariable(chaseSpeedVariableName, out chaseSpeedVariable)) { return false; }
		if (searchSpeedVariable == null && !graphAgent.GetVariable(searchSpeedVariableName, out searchSpeedVariable)) { return false; }
		if (patrolSpeedVariable == null && !graphAgent.GetVariable(patrolSpeedVariableName, out patrolSpeedVariable)) { return false; }

		return true;
	}

	/// <summary>
	/// Resolves target move speed for the current stalker state.
	/// </summary>
	/// <param name="_state">Current graph state.</param>
	/// <returns>Configured speed for the given state.</returns>
	private float resolveSpeedForState(State _state)
	{
		switch (_state)
		{
			case State.Chasing:
				return chaseSpeedVariable.Value;
			case State.Searching:
				return searchSpeedVariable.Value;
			default:
				return patrolSpeedVariable.Value;
		}
	}

	/// <summary>
	/// Plays state transition cues when clips are assigned.
	/// </summary>
	/// <param name="_state">Current graph state.</param>
	private void playStateTransitionCue(State _state)
	{
		if (!audioSource) { return; }

		AudioClip _clip = null;
		switch (_state)
		{
			case State.Chasing:
				_clip = AlertedNoise != null ? AlertedNoise.Value : null;
				break;
			case State.Searching:
				_clip = SearchStarted != null ? SearchStarted.Value : null;
				break;
			case State.Patrol:
				_clip = SearchEnded != null ? SearchEnded.Value : null;
				break;
		}

		if (_clip) { audioSource.PlayOneShot(_clip); }
	}
}