using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Entities.Octree
{
	public class PathFindingAgent : MonoBehaviour
	{
		public Transform Target;
		[SerializeField]
		private float agentRadius;
		[SerializeField]
		private float moveSpeed;
		[SerializeField]
		private LayerMask obstacleLayerMask;
		[SerializeField]
		private LayerMask groundLayerMask;
		[SerializeField]
		private float groundCheckDistance;
		[SerializeField]
		private Vector3 octreeOrigin = new Vector3(-6,0,6);
		[SerializeField]
		private Vector3 octreeSize = new Vector3(30,20,30);


		private Octree octree;
		private List<Vector3> path = new List<Vector3>();
		private int currentPathIndex;

		public bool IsMoving { get; private set; }
		public bool HasPath => path != null && path.Count > 0;
		public Vector3 Destination { get; private set; }
		
		public List<Vector3> Path { get { return path; } }

		public List<OctreeNode> FreeLeaves => octree.GetAllFreeLeaves();

		private void Start()
		{
			obstacleLayerMask = LayerMask.GetMask("Wall", "Anomaly", "Obstacle");
			Bounds _bounds = new Bounds(octreeOrigin, octreeSize);
			int _maxDepth = 7;
			float _minLeafSize = 0.1f;
			
			octree = new Octree(_bounds, _maxDepth, _minLeafSize, agentRadius, obstacleLayerMask, groundLayerMask, groundCheckDistance);

			if (TryGetComponent<OctreeDebugger>(out var _octreeDebugger)) { _octreeDebugger.SetOctree(octree); }
		}

		private void Update()
		{
			if (Target && (path == null || path.Count == 0)) { RequestPath(Target.position); }

			if (path != null && path.Count > 0) { moveAlongPath(); }
		}

		public void RequestPath(Vector3 _targetPos)
		{
			OctreeNode _startNode = octree.FindLeafAtPoint(transform.position);
			OctreeNode _endNode = octree.FindLeafAtPoint(_targetPos);

			if (_startNode == null || _endNode == null || _startNode.IsOccupied || _endNode.IsOccupied)
			{
				Debug.LogWarning("Start or goal node is null or occupied.");
				return;
			}

			List<OctreeNode> nodePath = aStar(_startNode, _endNode);
			if (nodePath == null) { return; }
			path = smoothPath(nodePath);
			currentPathIndex = 0;
			IsMoving = true;
		}

		public void SetDestination(Vector3 _destination)
		{
			Destination = _destination;
			RequestPath(_destination);
		}

		public void StopMoving()
		{
			path = null;
			IsMoving = false;
		}

		private List<OctreeNode> aStar(OctreeNode _start, OctreeNode _goal)
		{
			var _openSet = new List<OctreeNode>();
			var _closedSet = new HashSet<OctreeNode>();
			var _cameFrom = new Dictionary<OctreeNode, OctreeNode>();
			var _gScore = new Dictionary<OctreeNode, float>();
			var _fScore = new Dictionary<OctreeNode, float>();

			_openSet.Add(_start);
			_gScore[_start] = 0;
			_fScore[_start] = Vector3.Distance(_start.Bounds.center, _goal.Bounds.center);

			while (_openSet.Count > 0)
			{
				OctreeNode _current = getLowestFScore(_openSet, _fScore);
				if (_current == _goal) { return reconstructPath(_cameFrom, _current); }

				_openSet.Remove(_current);
				_closedSet.Add(_current);

				foreach (OctreeNode _neighbour in _current.Neighbors)
				{
					if (_closedSet.Contains(_neighbour)) { continue; }

					float _tentativeG = _gScore[_current] + Vector3.Distance(_current.Bounds.center, _neighbour.Bounds.center);

					if (_gScore.ContainsKey(_neighbour) && !(_tentativeG < _gScore[_neighbour])) { continue; }
					_cameFrom[_neighbour] = _current;
					_gScore[_neighbour] = _tentativeG;
					_fScore[_neighbour] = _tentativeG + Vector3.Distance(_neighbour.Bounds.center, _goal.Bounds.center);

					if (!_openSet.Contains(_neighbour)) { _openSet.Add(_neighbour); }
				}
			}
			return null;
		}

		private OctreeNode getLowestFScore(List<OctreeNode> _openSet, Dictionary<OctreeNode, float> _fScore)
		{
			OctreeNode _best = null;
			float _bestScore = float.MaxValue;
			foreach (OctreeNode _node in _openSet)
			{
				if (_fScore.TryGetValue(_node, out var _score) && _score < _bestScore)
				{
					_best = _node;
					_bestScore = _score;
				}
			}
			return _best;
		}

		private List<OctreeNode> reconstructPath(Dictionary<OctreeNode, OctreeNode> _cameFrom, OctreeNode _current)
		{
			var _path = new List<OctreeNode>();
			while (_cameFrom.ContainsKey(_current))
			{
				_path.Add(_current);
				_current = _cameFrom[_current];
			}
			_path.Add(_current);
			_path.Reverse();
			return _path;
		}

		private List<Vector3> smoothPath(List<OctreeNode> _nodePath)
		{
			if (_nodePath.Count < 2) return new List<Vector3>();

			List<Vector3> _smoothed = new List<Vector3>();
			_smoothed.Add(_nodePath[0].Bounds.center);
			int _current = 0;

			while (_current < _nodePath.Count - 1)
			{
				int _next = _nodePath.Count - 1;
				for (int _i = _nodePath.Count - 1; _i > _current; _i--)
				{
					if (!hasClearLineOfSight(_nodePath[_current].Bounds.center, _nodePath[_i].Bounds.center)) { continue; }
					_next = _i;
					break;
				}
				_smoothed.Add(_nodePath[_next].Bounds.center);
				_current = _next;
			}
			return _smoothed;
		}

		private bool hasClearLineOfSight(Vector3 _start, Vector3 _end)
		{
			Vector3 _direction = _end - _start;
			float _distance = _direction.magnitude;
			return !Physics.CapsuleCast(_start, _end, agentRadius, _direction.normalized, _distance, obstacleLayerMask);
		}

		private void moveAlongPath()
		{
			if (currentPathIndex >= path.Count) { return; }

			Vector3 _targetPos = path[currentPathIndex];
			Vector3 _direction = _targetPos - transform.position;
			if (_direction.magnitude < 0.1f)
			{
				currentPathIndex++;
				return;
			}

			transform.position += _direction.normalized * (moveSpeed * Time.deltaTime);
			if (_direction != Vector3.zero) { transform.rotation = Quaternion.LookRotation(_direction); }
		}
	}
}