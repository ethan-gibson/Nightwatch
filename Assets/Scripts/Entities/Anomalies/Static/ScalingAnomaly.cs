using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Logger = Arti.Utilities.Logger;

namespace Game.Entities
{
	public class ScalingAnomaly : AnomalyMain
	{
		[SerializeField]
		private Vector3 normalScale;
		[SerializeField]
		private Vector3 shiftedScale;
		[SerializeField]
		private float growTime = 3f;

		private void Awake()
		{
			transform.localScale = normalScale;
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			changed = true;
			Logger.Log(changed);
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
			//need new tolen each time it runs
			try
			{
				float _elapsedTime = 0;
				while (_elapsedTime < growTime)
				{
					transform.localScale = Vector3.Lerp(normalScale, shiftedScale, _elapsedTime / growTime);
					_elapsedTime += Time.deltaTime;
					await UniTask.Yield(cancellationToken: cts.Token);
				}
				transform.localScale = shiftedScale;
			}
			catch (OperationCanceledException) { }
		}

		private void OnDestroy()
		{
			if (cts == null) { return; }
			cts?.Cancel();
			cts?.Dispose();
			cts = null;
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