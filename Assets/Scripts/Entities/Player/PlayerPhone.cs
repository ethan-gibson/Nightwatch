using System.Collections.Generic;
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
		private HashSet<AnomalyMain> scannedObjects;
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
			scannedObjects = new HashSet<AnomalyMain>();
		}

		private void FixedUpdate()
		{
			if (!reportPrepped) { phoneCamera.Render(); }
		}

		private void setInputActions()
		{
			playerInput.actions["Scan"].started += OnScanPerformed;
			playerInput.actions["DiscardScan"].started += OnDiscardScanStarted;
		}

		private void removeInputActions()
		{
			playerInput.actions["Scan"].started -= OnScanPerformed;
			playerInput.actions["DiscardScan"].started -= OnDiscardScanStarted;
		}

		private void OnDestroy()
		{
			removeInputActions();
		}
		
		private void OnScanPerformed(InputAction.CallbackContext context)
		{
			prepReport();
		}

		private void OnDiscardScanStarted(InputAction.CallbackContext context)
		{
			discardReport();
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
				scannedObjects ??= new HashSet<AnomalyMain>();
				reportPrepped = true;
				Ray _ray = new Ray(raycastCamera.transform.position, raycastCamera.transform.forward * range);
				var _tempArray = Physics.SphereCastAll(_ray, range, mask);
				foreach (var _hit in _tempArray)
				{
					if (!Physics.Linecast(raycastCamera.transform.position, _hit.point)) { continue; }
					AnomalyMain _anomalyMain = _hit.collider.GetComponent<AnomalyMain>();
					if (_anomalyMain == null) { continue; }
					scannedObjects.Add(_anomalyMain);
				}
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
			foreach (var _anomaly in scannedObjects)
			{
				if (_anomaly.IsChanged() == false) { continue; }
				_goodReport = true; //If we find at least one, it's valid
				_anomaly.CallAnomalyReset();
			}
			Report?.Invoke(_goodReport);
			playAudio(_goodReport);
			scannedObjects.Clear();
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