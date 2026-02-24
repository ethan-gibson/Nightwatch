using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

/// <summary>
/// Updates locomotion outputs such as animator speed parameter and walking audio playback.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "UpdateStalkerLocomotionAction", story: "Updates stalker locomotion outputs", category: "Action", id: "8cafdbfa6434400caf09356922f8e31e")]
public partial class UpdateStalkerLocomotionAction : Action
{
	private const string defaultAnimatorSpeedParameter = "Velocity";
	private const string velocityAnimatorSpeedParameter = "Velocity";
	private const string speedMagnitudeAnimatorSpeedParameter = "SpeedMagnitude";
	private const string defaultWalkingAudioSourceChild = "LowLookPoint";
	private static readonly int velocityAnimatorSpeedParameterHash = Animator.StringToHash(velocityAnimatorSpeedParameter);
	private static readonly int speedMagnitudeAnimatorSpeedParameterHash = Animator.StringToHash(speedMagnitudeAnimatorSpeedParameter);

	/// <summary>
	/// Animator float parameter receiving current move speed magnitude.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<string> AnimatorSpeedParameter = new(defaultAnimatorSpeedParameter);

	/// <summary>
	/// If true, writes movement magnitude to the animator speed parameter.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<bool> UpdateAnimatorSpeed = new(true);

	/// <summary>
	/// If true, toggles walking audio based on movement magnitude.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<bool> ControlWalkingAudio = new(true);

	/// <summary>
	/// Minimum velocity required to play walking audio.
	/// </summary>
	[SerializeReference]
	public BlackboardVariable<float> WalkingAudioThreshold = new(0.1f);

	private NavMeshAgent navMeshAgent;
	private Animator animator;
	private AudioSource walkingAudioSource;
	private string cachedAnimatorParameterName;
	private int cachedAnimatorParameterHash;
	private float lastAnimatorSpeedValue;
	private bool hasAnimatorSpeedValue;
	private string cachedAnimatorParameterPresenceName;
	private bool hasConfiguredAnimatorFloatParameter;
	private bool hasVelocityAnimatorFloatParameter;
	private bool hasSpeedMagnitudeAnimatorFloatParameter;

	/// <summary>
	/// Applies one locomotion output update from the current <see cref="NavMeshAgent"/> state.
	/// </summary>
	/// <returns>
	/// <see cref="Node.Status.Success"/> when references are valid; otherwise <see cref="Node.Status.Failure"/>.
	/// </returns>
	protected override Status OnStart()
	{
		if (!tryCacheReferences()) { return Status.Failure; }

		float _velocitySqrMagnitude = navMeshAgent.velocity.sqrMagnitude;

		if (UpdateAnimatorSpeed != null && UpdateAnimatorSpeed.Value && animator)
		{
			string _parameterName = AnimatorSpeedParameter != null && !string.IsNullOrWhiteSpace(AnimatorSpeedParameter.Value)
				? AnimatorSpeedParameter.Value
				: defaultAnimatorSpeedParameter;

			if (!string.Equals(cachedAnimatorParameterName, _parameterName, StringComparison.Ordinal))
			{
				cachedAnimatorParameterName = _parameterName;
				cachedAnimatorParameterHash = Animator.StringToHash(_parameterName);
				cachedAnimatorParameterPresenceName = null;
				hasAnimatorSpeedValue = false;
			}
			refreshAnimatorParameterPresence(_parameterName);

			float _velocityMagnitude = Mathf.Sqrt(_velocitySqrMagnitude);
			if (!hasAnimatorSpeedValue || !Mathf.Approximately(_velocityMagnitude, lastAnimatorSpeedValue))
			{
				lastAnimatorSpeedValue = _velocityMagnitude;
				hasAnimatorSpeedValue = true;
				setAnimatorSpeed(_velocityMagnitude);
			}
		}

		if (ControlWalkingAudio != null && ControlWalkingAudio.Value && walkingAudioSource)
		{
			float _threshold = Mathf.Max(0f, WalkingAudioThreshold != null ? WalkingAudioThreshold.Value : 0.1f);
			float _thresholdSqr = _threshold * _threshold;
			if (_velocitySqrMagnitude > _thresholdSqr)
			{
				if (!walkingAudioSource.isPlaying) { walkingAudioSource.Play(); }
			}
			else
			{
				if (walkingAudioSource.isPlaying) { walkingAudioSource.Stop(); }
			}
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
	/// Caches components used by locomotion output updates.
	/// </summary>
	/// <returns><c>true</c> when the action has a valid graph-owned <see cref="GameObject"/> context.</returns>
	private bool tryCacheReferences()
	{
		if (!GameObject) { return false; }

		navMeshAgent ??= GameObject.GetComponent<NavMeshAgent>();
		animator ??= GameObject.GetComponent<Animator>();

		if (!walkingAudioSource)
		{
			Transform _walkingAudioTransform = GameObject.transform.Find(defaultWalkingAudioSourceChild);
			walkingAudioSource = _walkingAudioTransform ? _walkingAudioTransform.GetComponent<AudioSource>() : null;
		}

		return navMeshAgent;
	}

	/// <summary>
	/// Caches which float animator parameters are available for locomotion speed writes.
	/// </summary>
	private void refreshAnimatorParameterPresence(string _configuredParameterName)
	{
		if (string.Equals(cachedAnimatorParameterPresenceName, _configuredParameterName, StringComparison.Ordinal)) { return; }

		hasConfiguredAnimatorFloatParameter = false;
		hasVelocityAnimatorFloatParameter = false;
		hasSpeedMagnitudeAnimatorFloatParameter = false;

		AnimatorControllerParameter[] _parameters = animator.parameters;
		for (int _i = 0; _i < _parameters.Length; _i++)
		{
			AnimatorControllerParameter _parameter = _parameters[_i];
			if (_parameter.type != AnimatorControllerParameterType.Float) { continue; }

			if (!hasConfiguredAnimatorFloatParameter && string.Equals(_parameter.name, _configuredParameterName, StringComparison.Ordinal))
			{
				hasConfiguredAnimatorFloatParameter = true;
			}

			if (!hasVelocityAnimatorFloatParameter && string.Equals(_parameter.name, velocityAnimatorSpeedParameter, StringComparison.Ordinal))
			{
				hasVelocityAnimatorFloatParameter = true;
			}

			if (!hasSpeedMagnitudeAnimatorFloatParameter && string.Equals(_parameter.name, speedMagnitudeAnimatorSpeedParameter, StringComparison.Ordinal))
			{
				hasSpeedMagnitudeAnimatorFloatParameter = true;
			}
		}

		cachedAnimatorParameterPresenceName = _configuredParameterName;
	}

	/// <summary>
	/// Writes locomotion speed to configured and known fallback animator parameters.
	/// </summary>
	private void setAnimatorSpeed(float _speedMagnitude)
	{
		if (hasConfiguredAnimatorFloatParameter) { animator.SetFloat(cachedAnimatorParameterHash, _speedMagnitude); }
		else if (hasSpeedMagnitudeAnimatorFloatParameter) { animator.SetFloat(speedMagnitudeAnimatorSpeedParameterHash, _speedMagnitude); }
		else if (hasVelocityAnimatorFloatParameter) { animator.SetFloat(velocityAnimatorSpeedParameterHash, _speedMagnitude); }

		// Keep common parameters in sync to avoid skating when graph is configured to one but the blend tree reads the other.
		if (hasSpeedMagnitudeAnimatorFloatParameter && cachedAnimatorParameterHash != speedMagnitudeAnimatorSpeedParameterHash)
		{
			animator.SetFloat(speedMagnitudeAnimatorSpeedParameterHash, _speedMagnitude);
		}

		if (hasVelocityAnimatorFloatParameter && cachedAnimatorParameterHash != velocityAnimatorSpeedParameterHash)
		{
			animator.SetFloat(velocityAnimatorSpeedParameterHash, _speedMagnitude);
		}
	}
}
