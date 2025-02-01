using Game.Entities;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DuplicatingAnomaly), true)]
public class DuplicatingAnomalyEditor : Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();
		DuplicatingAnomaly _duplicatingAnomaly = (DuplicatingAnomaly)target;
		if (GUILayout.Button("Set Normal Location And Rotation"))
		{
			_duplicatingAnomaly.SetNormalLocationRotaion();
		}
		if (GUILayout.Button("Set Duplicate Location And Rotation"))
		{
			_duplicatingAnomaly.SetDuplicateLocationRotaion();
		}
		if (GUILayout.Button("Test Duplicated Anomaly"))
		{
			_duplicatingAnomaly.CallChangeAnomaly();
		}
	}
}