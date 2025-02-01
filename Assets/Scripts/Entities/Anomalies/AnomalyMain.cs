using System.Threading;
using Game.Core;
using Game.Manager;
using UnityEngine;

namespace Game.Entities
{
	public abstract class AnomalyMain : MonoBehaviour, IInteractable
	{
		protected bool changed;

		[Tooltip("How much does this anomaly count for")] [SerializeField]
		private int anomalyWeight;

		public delegate void AnomalySpawned(int _weight);
		protected CancellationTokenSource cts;

		private AnomalySpawned anomalySpawned;
		
		private void Awake()
		{
			cts = new CancellationTokenSource();
			anomalySpawned += GameManager.Instance.increaseAnomalyCount;
		}

		private void onDestroy()
		{
			anomalySpawned -= GameManager.Instance.increaseAnomalyCount;
			cts.Cancel();
			cts.Dispose();
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
		}

		public virtual void CallChangeAnomaly()
		{
			anomalyChange();
		}

		public virtual void CallAnomalyReset()
		{
			resetAnomaly();
		}

		[field: SerializeField] public float MaxRange { get; set; } = 10;
		public string InteractionText { get; set; } = "anomaly";

		public void OnStartHover()
		{
			
		}

		public void OnInteract()
		{
			if(!changed){return;}
			resetAnomaly();
			//GameManager.Instance.boostSpawnRate();
		}

		public void OnEndHover()
		{
			
		}
	}
}