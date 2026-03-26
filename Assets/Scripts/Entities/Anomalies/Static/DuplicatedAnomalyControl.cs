using UnityEngine;
using Logger = Arti.Utilities.Logger;

namespace Game.Entities
{
	public class DuplicatedAnomalyControl : AnomalyMain
	{
		private GameObject originalObject;
		private DuplicatingAnomaly duplicatingAnomaly;

		public void OnCreate(GameObject _original)
		{
			originalObject = _original;
			duplicatingAnomaly = _original.GetComponent<DuplicatingAnomaly>();
			if (!duplicatingAnomaly) { Logger.LogError("No Script On Original Object"); }
		}

		protected override void anomalyChange()
		{
			changed = true;
			Logger.Log("copy changes is: " + changed);
		}

		protected override void resetAnomaly()
		{
			Logger.Log("Copy Reported");
			duplicatingAnomaly.CopyReported();
			Destroy(this);
		}
	}
}