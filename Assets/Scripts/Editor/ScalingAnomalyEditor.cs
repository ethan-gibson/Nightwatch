using Game.Entities;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScalingAnomaly), true)]
public class ScalingAnomalyEditor : UnityEditor.Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();
		ScalingAnomaly _scalingAnomaly = (ScalingAnomaly)target;
		if (GUILayout.Button("Set Normal Scale"))
		{
			_scalingAnomaly.SetNormalScale();
		}
		if (GUILayout.Button("Set Shifted Scale"))
		{
			_scalingAnomaly.SetShiftedScale();
		}
		if (GUILayout.Button("Test Change Anomaly"))
		{
			_scalingAnomaly.CallChangeAnomaly();
		}
	}
}