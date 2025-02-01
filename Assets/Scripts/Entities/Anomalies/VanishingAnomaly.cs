using System;
using UnityEngine;

namespace Game.Entities
{
	public class VanishingAnomaly : AnomalyMain
	{
		[SerializeField] MeshRenderer _meshRenderer;

		protected override void anomalyChange()
		{
			base.anomalyChange();
			_meshRenderer.enabled = false;
		}

		protected override void resetAnomaly()
		{
			base.resetAnomaly();
			_meshRenderer.enabled = true;
		}
	}
}