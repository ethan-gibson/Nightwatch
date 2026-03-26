using System;
using UnityEngine;

namespace Game.Entities
{
	public class VanishingAnomaly : AnomalyMain
	{
		[SerializeField]
		MeshRenderer meshRenderer;

		private void Awake()
		{
			meshRenderer = GetComponent<MeshRenderer>();
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			meshRenderer.enabled = false;
		}

		protected override void resetAnomaly()
		{
			base.resetAnomaly();
			meshRenderer.enabled = true;
		}
	}
}