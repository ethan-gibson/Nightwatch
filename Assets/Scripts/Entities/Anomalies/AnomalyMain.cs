using Game.Manager;
using UnityEngine;

namespace Game.Entities
{
	public abstract class AnomalyMain : MonoBehaviour
	{
		protected bool changed;
		[Tooltip("How much does this anomaly count for")]
		[SerializeField] private int anomalyWeight;

		public delegate void AnomalySpawned(int _weight);
		private AnomalySpawned anomalySpawned;

		private void Awake()
		{
			anomalySpawned += GameManager.Instance.increaseAnomalyCount;
		}

		private void onDestroy()
		{
			anomalySpawned -= GameManager.Instance.increaseAnomalyCount;
		}

		protected virtual void anomalyChange()
		{
			changed = true;
			anomalySpawned?.Invoke(anomalyWeight);
		}

		protected virtual void resetAnomaly()
		{
			changed = false;
			anomalySpawned?.Invoke(-anomalyWeight);
			StopAllCoroutines();
		}

		public void CallChangeAnomaly()
		{
			anomalyChange();
		}

		public void CallAnomalyReset()
		{
			resetAnomaly();
		}
	}
}