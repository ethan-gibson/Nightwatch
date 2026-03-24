using System;
using UnityEngine;

namespace Game.Entities.Octree
{
	public static class OctreeDoorEvents
	{
		public static event Action<Bounds> DoorStateChanged;

		public static void NotifyDoorStateChanged(Bounds _bounds)
		{
			DoorStateChanged?.Invoke(_bounds);
		}
	}
}
