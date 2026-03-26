using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using Logger = Arti.Utilities.Logger;

namespace Game.Entities
{
	public class ShiftingAnomaly : AnomalyMain
	{
		[SerializeField]
		private float shiftTime = 3f;
		[SerializeField]
		private Vector3 normalLocation;
		[SerializeField]
		private Quaternion normalRotation;
		[SerializeField]
		private Vector3 shiftedLocation;
		[SerializeField]
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
			transform.rotation = normalRotation;
		}

		private async UniTask shifting()
		{
			float _elapsedTime = 0;

			cts?.Cancel();
			cts?.Dispose();
			cts = new CancellationTokenSource();
			//need new token each time it runs
			try
			{
				while (_elapsedTime < shiftTime)
				{
					if (_elapsedTime <= 0) { return; }
					transform.position = Vector3.Lerp(normalLocation, shiftedLocation, _elapsedTime / shiftTime);
					transform.rotation = Quaternion.Lerp(normalRotation, shiftedRotation, _elapsedTime / shiftTime);
					_elapsedTime += Time.deltaTime;
					await UniTask.Yield(cancellationToken: cts.Token);
				}
				transform.position = shiftedLocation;
			}
			catch (OperationCanceledException) { }
		}

		private bool IsValid(Quaternion quaternion)
		{
			return !float.IsNaN(quaternion.x) && !float.IsNaN(quaternion.y) &&
			       !float.IsNaN(quaternion.z) && !float.IsNaN(quaternion.w);
		}

		private void OnDestroy()
		{
			if (cts == null) { return; }
			cts?.Cancel();
			cts?.Dispose();
			cts = null;
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