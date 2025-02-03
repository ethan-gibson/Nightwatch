using Game.Entities;
using UnityEditor;
using UnityEngine;

namespace Editor
{
	[CustomEditor(typeof(HuntingAnomaly), true)]
	public class HuntingAnomalyEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			HuntingAnomaly _huntingAnomalyEditor = (HuntingAnomaly)target;
			if (GUILayout.Button("Set Spawn Point"))
			{
				_huntingAnomalyEditor.SetSpawnLocation();
			}
			if (GUILayout.Button("Test Change"))
			{
				_huntingAnomalyEditor.TestSetActive();
			}
			if (GUILayout.Button("Test Reset Anomaly"))
			{
				_huntingAnomalyEditor.TestReset();
			}
		}
	}
}