using UnityEngine;

namespace Game.Entities
{
	public class DuplicatingAnomaly : AnomalyMain
	{
		[SerializeField] private Vector3 normalLocation;
		[SerializeField] private Quaternion normalRotation;
		[SerializeField] private Vector3 duplicateLocation;
		[SerializeField] private Quaternion duplicateRotation;
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
		#region Editor

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