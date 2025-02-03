using Game.Entities;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VanishingAnomaly), true)]
public class VanishingAnomalyEditor : Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();
		VanishingAnomaly _vanishingAnomaly = (VanishingAnomaly)target;
		if (GUILayout.Button("Test Vanish Anomaly"))
		{
			_vanishingAnomaly.CallChangeAnomaly();
		}
		if (GUILayout.Button("Reset Anomaly"))
		{
			_vanishingAnomaly.CallAnomalyReset();
		}
	}
}