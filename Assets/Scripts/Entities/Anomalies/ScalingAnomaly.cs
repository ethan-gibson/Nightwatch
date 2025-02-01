using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.Entities
{
	public class ScalingAnomaly : AnomalyMain
	{
		[SerializeField] private Vector3 normalScale;
		[SerializeField] private Vector3 shiftedScale;
		[SerializeField] private float growTime = 3f;

		private void Awake()
		{
			transform.localScale = normalScale;
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			scaling().Forget();
		}

		protected override void resetAnomaly()
		{
			base.resetAnomaly();
			cts.Cancel();
			transform.localScale = normalScale;
		}

		private async UniTask scaling()
		{
			cts?.Cancel();
			cts?.Dispose();
			cts = new CancellationTokenSource();
			var _cts = cts.Token;
			//need new tolen each time it runs
			try
			{
				float _elapsedTime = 0;
				while (_elapsedTime < growTime)
				{
					transform.localScale = Vector3.Lerp(normalScale, shiftedScale, _elapsedTime / growTime);
					_elapsedTime += Time.deltaTime;
					await UniTask.Yield(cancellationToken: _cts);
				}
				transform.localScale = shiftedScale;
			}
			catch (OperationCanceledException) { transform.localScale = normalScale; }
		}

		#region Editor

		public void SetNormalScale()
		{
			normalScale = transform.localScale;
		}

		public void SetShiftedScale()
		{
			shiftedScale = transform.localScale;
		}

		#endregion
	}
}