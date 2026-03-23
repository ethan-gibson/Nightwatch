using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Entities.Octree
{
	public class OctreeDebugger : MonoBehaviour
	{
		[Header("Visualization")]
		public bool ShowFreeNodes = true;
		public bool ShowOccupiedNodes = false;
		public bool ShowPath = true;
		public bool ShowNeighbors = false;

		public Color FreeNodeColor = new Color(0, 1, 0, 0.5f);
		public Color OccupiedNodeColor = new Color(1, 0, 0, 0.5f);
		public Color PathColor = Color.cyan;
		public Color NeighborColor = Color.yellow;

		private PathFindingAgent agent;
		private Octree octree;

		private void Start()
		{
			agent = GetComponent<PathFindingAgent>();
		}

		public void SetOctree(Octree _tree)
		{
			octree = _tree;
		}

		private void OnDrawGizmos()
		{
			if (octree == null) { return; }

			if (ShowFreeNodes) { drawNodes(octree.GetAllFreeLeaves(), FreeNodeColor); }

			if (ShowOccupiedNodes) { drawNodes(octree.GetAllLeaves(), OccupiedNodeColor, true); }

			if (ShowPath && agent != null)
			{
				var _path = agent.Path;
				if (_path != null && _path.Count > 1)
				{
					Gizmos.color = PathColor;
					for (int _i = 0; _i < _path.Count - 1; _i++) { Gizmos.DrawLine(_path[_i], _path[_i + 1]); }
				}
			}

			if (!ShowNeighbors) { return; }
			var _freeLeaves = octree.GetAllFreeLeaves();
			Gizmos.color = NeighborColor;
			foreach (var _node in _freeLeaves)
			{
				foreach (var _neighbor in _node.Neighbors) { Gizmos.DrawLine(_node.Bounds.center, _neighbor.Bounds.center); }
			}
		}

		private void drawNodes(List<OctreeNode> _nodes, Color _color, bool _onlyOccupied = false)
		{
			Gizmos.color = _color;
			foreach (var _node in _nodes)
			{
				switch (_onlyOccupied)
				{
					case true when !_node.IsOccupied:
					case false when _node.IsOccupied:
						continue;
					default:
						Gizmos.DrawWireCube(_node.Bounds.center, _node.Bounds.size);
						break;
				}
			}
		}
	}
}