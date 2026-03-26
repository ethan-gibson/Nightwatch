using System.Collections.Generic;
using UnityEngine;

namespace Game.Entities.Octree
{
	public class OctreeNode
	{
		public Bounds Bounds { get; }
		public Bounds TraversalBounds { get; set; }
		public OctreeNode[] Children { get; set; }
		public bool IsOccupied { get; set; }
		public bool IsLeaf { get; set; }
		public bool HasGround { get; set; }
		public Vector3 SamplePoint { get; set; }

		public List<OctreeNode> Neighbors { get; }

		public OctreeNode(Bounds _bounds)
		{
			Bounds = _bounds;
			TraversalBounds = _bounds;
			Children = null;
			IsOccupied = false;
			IsLeaf = true;
			HasGround = false;
			SamplePoint = _bounds.center;
			Neighbors = new List<OctreeNode>();
		}
	}
}
