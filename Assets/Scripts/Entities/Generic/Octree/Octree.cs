using System.Collections.Generic;
using UnityEngine;

namespace Game.Entities.Octree
{
	public class Octree
	{
		private OctreeNode root;
		private int maxDepth;
		private float minLeafSize;
		private float agentRadius;
		private LayerMask obstacleMask;
		private LayerMask groundMask;
		private float groundCheckDistance;

		public Octree(Bounds _bounds, int _maxDepth, float _minLeafSize, float _agentRadius, LayerMask _obstacleMask, LayerMask _groundMask, float _groundCheckDistance)
		{
			maxDepth = _maxDepth;
			minLeafSize = _minLeafSize;
			agentRadius = _agentRadius;
			obstacleMask = _obstacleMask;
			groundMask = _groundMask;
			groundCheckDistance = _groundCheckDistance;
			
			root = new OctreeNode(_bounds);
			build();
		}

		private void build()
		{
			buildRecursive(root, 0);
			filterGroundNodes();
			connectNeighbors();
		}

		private void buildRecursive(OctreeNode _node, int _depth)
		{
			if (_depth >= maxDepth || _node.IsOccupied)
			{
				_node.IsLeaf = true;
				_node.IsOccupied = isOccupied(_node.Bounds);
				return;
			}

			if (!containsObstacles(_node.Bounds))
			{
				_node.IsLeaf = true;
				_node.IsOccupied = false;
				return;
			}
			
			_node.IsLeaf = false;
			_node.Children = new OctreeNode[8];
			
			Vector3 _center = _node.Bounds.center;
			Vector3 _ext =  _node.Bounds.extents * 0.5f;

			for (int _i = 0; _i < 8; _i++)
			{
				Vector3 _newCenter = _center + new Vector3(
					(_i & 1) == 0 ? -_ext.x : _ext.x,
					(_i & 2) == 0 ? -_ext.y : _ext.y,
					(_i & 4) == 0 ? -_ext.z : _ext.z
				);

				Vector3 _newSize = _node.Bounds.size * 0.5f;
				Bounds _bounds = new Bounds(_newCenter, _newSize);
				_node.Children[_i] = new OctreeNode(_bounds);
				buildRecursive(_node.Children[_i], _depth + 1);
			}
			
			bool _allChilderOccupied = true;
			foreach (var _child in _node.Children)
			{
				if (!_child.IsOccupied)
				{
					_allChilderOccupied = false;
					break;
				}
			}
			
			_node.IsOccupied = _allChilderOccupied;
			_node.IsLeaf = false;
		}

		private bool isOccupied(Bounds _bounds)
		{
			Vector3 _halfExtents = Vector3.one * agentRadius;
			Collider[] _hits = Physics.OverlapBox(_bounds.center, _halfExtents, Quaternion.identity, obstacleMask);
			
			return _hits.Length > 0;
		}

		private bool containsObstacles(Bounds _bounds)
		{
			Collider[] _colliders = Physics.OverlapBox(_bounds.center, _bounds.extents, Quaternion.identity, obstacleMask);
			return _colliders.Length > 0;
		}

		private void filterGroundNodes()
		{
			List<OctreeNode> _allLeaves = new List<OctreeNode>();
			gatherLeaves(root, _allLeaves);

			foreach (OctreeNode _node in _allLeaves)
			{
				if (_node.IsOccupied) continue;

				Vector3 _rayStart = _node.Bounds.center + Vector3.up * 0.1f;
				if (Physics.Raycast(_rayStart, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask))
				{
					Vector3 _newCenter = _node.Bounds.center;
					_newCenter.y = hit.point.y + 1;
					_node.Bounds.center = _newCenter;
				}
				else
				{
					_node.IsOccupied = true;
				}
			}
		}

		private void connectNeighbors()
		{
			List<OctreeNode> _leaves = new List<OctreeNode>();
			gatherLeaves(root, _leaves);

			foreach (var _node in _leaves)
			{
				if (_node.IsOccupied) { continue; }

				Vector3[] _directions = { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back };

				foreach (Vector3 _direction in _directions)
				{
					Vector3 _neighbourCenter = _node.Bounds.center + _direction * _node.Bounds.extents.x;
					OctreeNode _neighbour = FindLeafAtPoint(_neighbourCenter);
					
					if (_neighbour == null || _neighbour.IsOccupied) { continue; }

					if (!hasLinOfSight(_node.Bounds.center, _neighbour.Bounds.center)) { continue;}
					
					if (!_node.Neighbors.Contains(_neighbour))
					{
						_node.Neighbors.Add(_neighbour);
					}
					if (!_neighbour.Neighbors.Contains(_node))
					{
						_neighbour.Neighbors.Add(_node);
					}
				}
			}
		}

		private bool hasLinOfSight(Vector3 _start, Vector3 _end)
		{
			Vector3 _dir = _end -  _start;
			float _dist = _dir.magnitude;
			return !Physics.CapsuleCast(_start, _end, agentRadius, _dir.normalized, _dist, obstacleMask);
		}

		private void gatherLeaves(OctreeNode _node, List<OctreeNode> _leaves)
		{
			if (_node.IsLeaf)
			{
				_leaves.Add(_node);
				return;
			}
			if (_node.Children == null) { return; }
			foreach (OctreeNode _child in _node.Children) { gatherLeaves(_child, _leaves); }
		}

		public OctreeNode FindLeafAtPoint(Vector3 _point)
		{
			return findLeafAtPointRecursive(root, _point);
		}

		private OctreeNode findLeafAtPointRecursive(OctreeNode _node, Vector3 _point)
		{
			if (!_node.Bounds.Contains(_point)) { return null; }

			if (_node.IsLeaf) { return _node; }

			if (_node.Children == null) { return null; }
			foreach (OctreeNode _child in _node.Children)
			{
				OctreeNode _result = findLeafAtPointRecursive(_child, _point);
				if (_result != null) { return _result; }
			}
			return null;
		}

		public List<OctreeNode> GetAllFreeLeaves()
		{
			List<OctreeNode> _leaves = new List<OctreeNode>();
			gatherLeaves(root, _leaves);
			return _leaves.FindAll(_o => !_o.IsOccupied);
		}
		
		public List<OctreeNode> GetAllLeaves()
		{
			List<OctreeNode> _leaves = new List<OctreeNode>();
			gatherLeaves(root, _leaves);
			return _leaves;
		}
	}
}