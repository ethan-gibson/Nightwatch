using Game.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Entities.Octree
{
	public static class StalkerKillUtility
	{
		public const float DefaultVisibleKillDistance = 1.6f;
		public const float DefaultHiddenKillDistance = 1.75f;
		public const float DefaultKillLockDuration = 4f;

		private static readonly int isKilling = Animator.StringToHash("IsKilling");
		private static readonly int killType = Animator.StringToHash("KillType");
		private const string defaultLookPointName = "HighLookPoint";

		public static bool TryTriggerVisibleKill(GameObject _agentObject, GameObject _playerObject, float _killDistance, float _lockDuration, GameObject _configuredLookPointObject = null)
		{
			if (!_agentObject || !_playerObject) { return false; }

			Animator _animator = _agentObject.GetComponent<Animator>();
			if (_animator && _animator.GetBool(isKilling)) { return true; }

			PlayerMovement _playerMovement = _playerObject.GetComponent<PlayerMovement>();
			if (_playerMovement == null || _playerMovement.CheckIfHiding()) { return false; }

			if (!IsWithinHorizontalKillRange(_agentObject, _playerObject, _killDistance)) { return false; }

			Transform _agentTransform = _agentObject.transform;
			Transform _playerTransform = _playerObject.transform;
			if (_agentTransform == null || _playerTransform == null) { return false; }

			PathFindingAgent _pathfindingAgent = _agentObject.GetComponent<PathFindingAgent>();
			if (_pathfindingAgent)
			{
				_pathfindingAgent.Target = null;
				_pathfindingAgent.StopMoving();
			}

			global::StalkerAnimationEvents _animationEvents = _agentObject.GetComponent<global::StalkerAnimationEvents>();
			_animationEvents?.SetExternalMovementLock(true);

			NavMeshAgent _navMeshAgent = _agentObject.GetComponent<NavMeshAgent>();
			if (_navMeshAgent && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
			{
				_navMeshAgent.velocity = Vector3.zero;
				_navMeshAgent.isStopped = true;
				_navMeshAgent.ResetPath();
			}

			Vector3 _lookDirection = _playerTransform.position - _agentTransform.position;
			_lookDirection.y = 0f;
			if (_lookDirection.sqrMagnitude > 0.0001f)
			{
				_agentTransform.rotation = Quaternion.LookRotation(_lookDirection, Vector3.up);
			}

			if (_animator)
			{
				_animator.SetBool(isKilling, true);
				_animator.SetInteger(killType, 0);
			}

			float _resolvedLockDuration = Mathf.Max(0.1f, _lockDuration);
			_playerMovement.lockPlayer(resolveLookPoint(_agentTransform, _configuredLookPointObject), _resolvedLockDuration);
			return true;
		}

		public static bool IsWithinHorizontalKillRange(GameObject _agentObject, GameObject _targetObject, float _killDistance)
		{
			if (!_agentObject || !_targetObject) { return false; }

			Transform _agentTransform = _agentObject.transform;
			Transform _targetTransform = _targetObject.transform;
			if (_agentTransform == null || _targetTransform == null) { return false; }

			Collider _agentCollider = resolvePrimaryCollider(_agentObject);
			Collider _targetCollider = resolvePrimaryCollider(_targetObject);
			float _resolvedKillDistance = Mathf.Max(0.1f, _killDistance);
			float _killDistanceSqr = _resolvedKillDistance * _resolvedKillDistance;

			Vector2 _agentPoint = resolveClosestHorizontalPoint(_agentTransform, _agentCollider, _targetTransform.position);
			Vector2 _targetPoint = resolveClosestHorizontalPoint(_targetTransform, _targetCollider, _agentTransform.position);
			return (_targetPoint - _agentPoint).sqrMagnitude <= _killDistanceSqr;
		}

		private static Collider resolvePrimaryCollider(GameObject _gameObject)
		{
			if (!_gameObject) { return null; }

			Collider _collider = _gameObject.GetComponent<Collider>();
			if (_collider) { return _collider; }
			return _gameObject.GetComponentInChildren<Collider>();
		}

		private static Vector2 resolveClosestHorizontalPoint(Transform _transform, Collider _collider, Vector3 _referencePosition)
		{
			Vector3 _point = _transform.position;
			if (_collider != null && _collider.enabled)
			{
				_point = _collider.ClosestPoint(_referencePosition);
			}

			return new Vector2(_point.x, _point.z);
		}

		private static Transform resolveLookPoint(Transform _agentTransform, GameObject _configuredLookPointObject)
		{
			if (_configuredLookPointObject) { return _configuredLookPointObject.transform; }

			Transform _fallbackLookPoint = _agentTransform != null ? _agentTransform.Find(defaultLookPointName) : null;
			if (_fallbackLookPoint) { return _fallbackLookPoint; }
			return _agentTransform;
		}
	}
}
