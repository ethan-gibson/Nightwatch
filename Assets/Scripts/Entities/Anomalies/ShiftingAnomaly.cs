using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.Entities
{
	public class ShiftingAnomaly : AnomalyMain
	{
		[SerializeField] private float shiftTime = 3f;
		private Vector3 normalLocation;
		private Quaternion normalRotation;
		private Vector3 shiftedLocation;
		private Quaternion shiftedRotation;

		private void Awake()
		{
			transform.position = normalLocation;
			transform.rotation = normalRotation;
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			shifting().Forget();
		}

		protected override void resetAnomaly()
		{
			base.resetAnomaly();
			transform.position = normalLocation;
		}

		private async UniTask shifting()
		{
			float _elapsedTime = 0;
			while (_elapsedTime < shiftTime)
			{
				transform.position = Vector3.Lerp(normalLocation, shiftedLocation, _elapsedTime / shiftTime);
				transform.rotation = Quaternion.Lerp(normalRotation, shiftedRotation, _elapsedTime / shiftTime);
				_elapsedTime += Time.deltaTime;
				await UniTask.Yield();
			}
			transform.position = shiftedLocation;
		}

		#region Editor

		public void SetNormalLocationRotation()
		{
			normalLocation = transform.position;
			normalRotation = transform.rotation;
		}

		public void SetShiftedLocationRotation()
		{
			shiftedLocation = transform.position;
			shiftedRotation = transform.rotation;
		}

		#endregion
	}
}