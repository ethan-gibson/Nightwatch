using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Entities
{
	public class PlayerPhone : MonoBehaviour
	{
		private Camera phoneCamera;
		private const float range = 10;
		[SerializeField] private LayerMask mask;
		[SerializeField] private GameObject raycastCamera;
		private PlayerInput playerInput;
		private bool reportPrepped;
		private RaycastHit[] scannedObjects;

		public delegate void MadeReport(bool _report);

		public event MadeReport Report;

		private void Awake()
		{
			playerInput = GetComponent<PlayerInput>();
			setInputActions();
			phoneCamera = raycastCamera.GetComponent<Camera>();
			phoneCamera.enabled = false;
		}

		private void FixedUpdate()
		{
			if (!reportPrepped) { phoneCamera.Render(); }
		}

		private void setInputActions()
		{
			playerInput.actions["Scan"].started += _ => prepReport();
			playerInput.actions["DiscardScan"].started += _ => discardReport();
		}

		private void removeInputActions()
		{
			playerInput.actions["Scan"].started -= _ => prepReport();
			playerInput.actions["DiscardScan"].started -= _ => discardReport();
		}

		private void OnDestroy()
		{
			removeInputActions();
		}

		private void prepReport()
		{
			if (!reportPrepped)
			{
				reportPrepped = true;
				Ray _ray = new Ray(raycastCamera.transform.position, raycastCamera.transform.forward);
				scannedObjects = Physics.SphereCastAll(_ray, 0.5f, range, mask);
			}
			else { report(); }
		}

		private void discardReport()
		{
			reportPrepped = false;
			scannedObjects = null;
		}

		private void report()
		{
			bool _goodReport = false;
			reportPrepped = false;
			foreach (var _hit in scannedObjects)
			{
				if (!_hit.collider.CompareTag("Anomaly")) { continue; } // f it's an anomaly
				if (!Physics.Linecast(raycastCamera.transform.position, _hit.point)) { continue; } //If it's visible
				AnomalyMain _anomalyMain = _hit.collider.GetComponent<AnomalyMain>();
				if (_anomalyMain == null) { continue; } //If it has a script that descends from AnomalyMain
				if (_anomalyMain.IsChanged() == false) { continue; }
				Debug.Log("Found a script derived from AnomalyMain: " + _anomalyMain.GetType().Name);
				_goodReport = true; //If we find at least one, it's valid
				_anomalyMain.CallAnomalyReset();
			}
			Debug.Log("report was " + _goodReport);
			Report?.Invoke(_goodReport);
		}
	}
}