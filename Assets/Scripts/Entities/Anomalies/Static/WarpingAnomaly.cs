using System.Collections.Generic;
using UnityEngine;

namespace Game.Entities
{
	public class WarpingAnomaly : AnomalyMain
	{
		[Tooltip("The material you want to change, start at 0")] [SerializeField]
		private int materialToChange;

		[SerializeField] private Material materialToChangeTo;
		private Material[] originalMaterials;
		[SerializeField] private MeshRenderer meshRenderer;

		private void Awake()
		{
			originalMaterials = meshRenderer.sharedMaterials;
			materialToChangeTo = originalMaterials[materialToChange];
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			var _tempArray = meshRenderer.sharedMaterials;
			_tempArray[materialToChange] = materialToChangeTo;
			meshRenderer.sharedMaterials = _tempArray;
		}

		protected override void resetAnomaly()
		{
			base.resetAnomaly();
			meshRenderer.sharedMaterials = originalMaterials;
		}
	}
}