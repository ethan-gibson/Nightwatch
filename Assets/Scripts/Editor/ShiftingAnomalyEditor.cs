using Game.Entities;
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(ShiftingAnomaly), true)]
public class ShiftingAnomalyEditor : Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();
		ShiftingAnomaly _shiftingAnomaly = (ShiftingAnomaly)target;
		if (GUILayout.Button("Set Normal Location"))
		{
			_shiftingAnomaly.SetNormalLocationRotation();
		}
		if (GUILayout.Button("Set Shifted Location"))
		{
			_shiftingAnomaly.SetShiftedLocationRotation();
		}
		if (GUILayout.Button("Test Change Anomaly"))
		{
			_shiftingAnomaly.CallChangeAnomaly();
		}
	}
}