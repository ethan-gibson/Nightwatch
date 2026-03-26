using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Entities.Octree
{
	public class OctreeDebugger : MonoBehaviour
	{
		[Header("Visualization")]
		public bool ShowFreeNodes = true;
		public bool ShowOccupiedNodes = false;
		public bool ShowPath = true;
		public bool ShowNavMeshPath = true;
		public bool ShowNeighbors = false;

		public Color FreeNodeColor = new Color(0, 1, 0, 0.5f);
		public Color OccupiedNodeColor = new Color(1, 0, 0, 0.5f);
		public Color PathColor = Color.cyan;
		public Color NavMeshPathColor = new Color(1f, 0.5f, 0f, 1f);
		public Color NeighborColor = Color.yellow;

		private PathFindingAgent agent;
		private NavMeshAgent navMeshAgent;
		private Octree octree;

		private void Start()
		{
			cacheReferences();
		}

		public void SetOctree(Octree _tree)
		{
			octree = _tree;
		}

		private void OnDrawGizmos()
		{
			cacheReferences();

			if (octree != null)
			{
				if (ShowFreeNodes) { drawNodes(octree.GetAllFreeLeaves(), FreeNodeColor); }

				if (ShowOccupiedNodes) { drawNodes(octree.GetAllLeaves(), OccupiedNodeColor, true); }
			}

			if (ShowPath)
			{
				drawAgentPath();
			}

			if (!ShowNeighbors || octree == null) { return; }
			var _freeLeaves = octree.GetAllFreeLeaves();
			Gizmos.color = NeighborColor;
			foreach (var _node in _freeLeaves)
			{
				foreach (var _neighbor in _node.Neighbors) { Gizmos.DrawLine(_node.Bounds.center, _neighbor.Bounds.center); }
			}
		}

		private void cacheReferences()
		{
			agent ??= GetComponent<PathFindingAgent>();
			navMeshAgent ??= GetComponent<NavMeshAgent>();
		}

		private void drawAgentPath()
		{
			if (agent == null) { return; }

			IReadOnlyList<Vector3> _path = agent.Path;
			if (_path != null && _path.Count > 1)
			{
				Gizmos.color = PathColor;
				for (int _i = 0; _i < _path.Count - 1; _i++)
				{
					Gizmos.DrawLine(_path[_i], _path[_i + 1]);
				}
				return;
			}

			if (_path != null && _path.Count == 1)
			{
				Gizmos.color = PathColor;
				Gizmos.DrawLine(transform.position, _path[0]);
				Gizmos.DrawSphere(_path[0], 0.15f);
				return;
			}

			if (!ShowNavMeshPath || navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh) { return; }

			Vector3[] _corners = navMeshAgent.path.corners;
			if (_corners == null || _corners.Length == 0) { return; }

			Gizmos.color = NavMeshPathColor;
			Gizmos.DrawLine(transform.position, _corners[0]);
			Gizmos.DrawSphere(_corners[0], 0.12f);
			for (int _i = 0; _i < _corners.Length - 1; _i++)
			{
				Gizmos.DrawLine(_corners[_i], _corners[_i + 1]);
				Gizmos.DrawSphere(_corners[_i + 1], 0.12f);
			}
		}

		private void drawNodes(IReadOnlyList<OctreeNode> _nodes, Color _color, bool _onlyOccupied = false)
		{
			Gizmos.color = _color;
			for (int _i = 0; _i < _nodes.Count; _i++)
			{
				OctreeNode _node = _nodes[_i];
				switch (_onlyOccupied)
				{
					case true when !_node.IsOccupied:
					case false when _node.IsOccupied:
						continue;
					default:
						Bounds _drawBounds = _node.IsOccupied ? _node.Bounds : _node.TraversalBounds;
						Gizmos.DrawWireCube(_drawBounds.center, _drawBounds.size);
						break;
				}
			}
		}
	}
}
