using UnityEngine;

namespace Game.Entities
{
	public class DuplicatingAnomaly : AnomalyMain
	{
		private Vector3 normalLocation;
		private Quaternion normalRotation;
		private Vector3 duplicateLocation;
		private Quaternion duplicateRotation;
		private GameObject duplicatedAnomaly;

		private void Awake()
		{
			transform.position = normalLocation;
			transform.rotation = normalRotation;
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			duplicatedAnomaly = Instantiate(gameObject, duplicateLocation, duplicateRotation);
		}

		protected override void resetAnomaly()
		{
			base.resetAnomaly();
			Destroy(duplicatedAnomaly);
		}

		#region MyRegion

		public void SetNormalLocationRotaion()
		{
			normalLocation = transform.position;
			normalRotation = transform.rotation;
		}

		public void SetDuplicateLocationRotaion()
		{
			duplicateLocation = transform.position;
			duplicateRotation = transform.rotation;
		}

		#endregion
	}
}