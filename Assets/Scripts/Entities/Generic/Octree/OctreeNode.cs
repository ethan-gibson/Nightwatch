using System.Collections.Generic;
using UnityEngine;

namespace Game.Entities.Octree
{
    public class OctreeNode
    {
	    public Bounds Bounds;
	    public OctreeNode[] Children;
	    public bool IsOccupied;
	    public bool IsLeaf;
	    
	    public List<OctreeNode> Neighbors;

	    public OctreeNode(Bounds _bounds)
	    {
		    Bounds = _bounds;
		    Children = null;
		    IsOccupied = false;
		    IsLeaf = true;
		    Neighbors = new List<OctreeNode>();
	    }
    }
}
