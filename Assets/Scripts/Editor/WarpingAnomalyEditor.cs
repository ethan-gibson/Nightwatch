using Game.Entities;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WarpingAnomaly), true)]
public class WarpingAnomalyEditor : UnityEditor.Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();
		WarpingAnomaly _warpingAnomaly = (WarpingAnomaly)target;
		if (GUILayout.Button("Warp Anomaly"))
		{
			_warpingAnomaly.CallChangeAnomaly();
		}
		if (GUILayout.Button("Undo Warp"))
		{
			_warpingAnomaly.CallAnomalyReset();
		}
	}
}