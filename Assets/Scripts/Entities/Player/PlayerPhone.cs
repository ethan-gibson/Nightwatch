using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Entities
{
	public class PlayerPhone : MonoBehaviour
	{
		private Camera phoneCamera;
		[SerializeField] float range = 40;
		[SerializeField] private LayerMask mask;
		[SerializeField] private GameObject raycastCamera;
		private PlayerInput playerInput;
		private bool reportPrepped;
		private RaycastHit[] scannedObjects;
		private AudioSource audioSource;
		[SerializeField] private AudioClip warning;
		[SerializeField] private AudioClip goodReport;
		[SerializeField] private AudioClip badReport;

		public delegate void MadeReport(bool _report);

		public event MadeReport Report;

		private void Awake()
		{
			playerInput = GetComponent<PlayerInput>();
			setInputActions();
			phoneCamera = raycastCamera.GetComponent<Camera>();
			phoneCamera.enabled = false;
			audioSource = GetComponent<AudioSource>();
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

		public void PlayWarning()
		{
			audioSource.clip = warning;
			audioSource.Play();
		}

		private void prepReport()
		{
			if (!reportPrepped)
			{
				reportPrepped = true;
				Ray _ray = new Ray(raycastCamera.transform.position, raycastCamera.transform.forward * range);
				scannedObjects = Physics.SphereCastAll(_ray, range, mask);
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
			playAudio(_goodReport);
		}

		private void playAudio(bool _report)
		{
			if (_report)
			{
				audioSource.clip = goodReport;
				audioSource.Play();
			}
			else
			{
				audioSource.clip = badReport;
				audioSource.Play();
			}
		}
	}
}