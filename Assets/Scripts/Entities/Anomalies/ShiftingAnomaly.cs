using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.Entities
{
	public class ShiftingAnomaly : AnomalyMain
	{
		[SerializeField] private float shiftTime = 3f;
		[SerializeField] private Vector3 normalLocation;
		[SerializeField] private Quaternion normalRotation;
		[SerializeField] private Vector3 shiftedLocation;
		[SerializeField] private Quaternion shiftedRotation;

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


			cts?.Cancel();
			cts?.Dispose();
			cts = new CancellationTokenSource();
			var _cts = cts.Token;
			//need new tolen each time it runs
			try
			{
				while (_elapsedTime < shiftTime)
				{
					transform.position = Vector3.Lerp(normalLocation, shiftedLocation, _elapsedTime / shiftTime);
					transform.rotation = Quaternion.Lerp(normalRotation, shiftedRotation, _elapsedTime / shiftTime);
					_elapsedTime += Time.deltaTime;
					await UniTask.Yield();
				}
				transform.position = shiftedLocation;
			}
			catch (OperationCanceledException) { transform.position = normalLocation; }
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