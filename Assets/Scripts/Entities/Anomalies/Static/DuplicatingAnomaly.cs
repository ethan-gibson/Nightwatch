using UnityEngine;

namespace Game.Entities
{
	public class DuplicatingAnomaly : AnomalyMain
	{
		[SerializeField]
		private Vector3 normalLocation;
		[SerializeField]
		private Quaternion normalRotation;
		[SerializeField]
		private Vector3 duplicateLocation;
		[SerializeField]
		private Quaternion duplicateRotation;
		[SerializeField]
		private GameObject duplicatedAnomaly;
		private GameObject copy;

		private void Awake()
		{
			transform.position = normalLocation;
			transform.rotation = normalRotation;
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			copy = Instantiate(duplicatedAnomaly, duplicateLocation, duplicateRotation);
			copy.AddComponent<DuplicatedAnomalyControl>().OnCreate(gameObject);
			copy.GetComponent<AnomalyMain>().CallChangeAnomaly();
		}

		public void CopyReported()
		{
			resetAnomaly();
		}

		protected override void resetAnomaly()
		{
			if (!copy) { return; }
			base.resetAnomaly();
			Destroy(copy);
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