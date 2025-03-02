using System;
using System.Threading;
using Game.Core;
using UnityEngine;

namespace Game.Entities
{
	public abstract class AnomalyMain : MonoBehaviour
	{
		protected bool changed;

		[Tooltip("How much does this anomaly count for")] [SerializeField]
		private int anomalyWeight;

		public delegate void AnomalySpawned(int _weight);

		protected CancellationTokenSource cts;

		public AnomalySpawned AnomalySpawnedEvent;
		private bool isVisible;

		private void Awake()
		{
			cts = new CancellationTokenSource();
		}

		protected virtual void anomalyChange()
		{
			changed = true;
			AnomalySpawnedEvent?.Invoke(anomalyWeight);
		}

		protected virtual void resetAnomaly()
		{
			changed = false;
			cts?.Cancel();
			AnomalySpawnedEvent?.Invoke(-anomalyWeight);
		}

		public void CallChangeAnomaly()
		{
			anomalyChange();
		}

		public void CallAnomalyReset()
		{
			resetAnomaly();
		}

		public bool IsChanged()
		{
			return changed;
		}

		public bool IsVisible()
		{
			return isVisible;
		}

		private void OnBecameVisible()
		{
			isVisible = true;
		}

		private void OnBecameInvisible()
		{
			isVisible = false;
		}
	}
}