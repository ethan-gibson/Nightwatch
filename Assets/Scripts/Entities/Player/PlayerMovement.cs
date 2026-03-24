using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using Logger = Arti.Utilities.Logger;

namespace Game.Entities
{
	public class PlayerMovement : EntityMovement
	{
		private PlayerInput playerInput;
		private bool isCrouched;
		private bool inputActionsBound;
		private Collider playerCollider;

		public delegate void LookInputEvent(InputAction.CallbackContext _ctx);

		public delegate void CrouchInputEvent(InputAction.CallbackContext _ctx);

		public delegate void PlayerKilledEvent();

		private event LookInputEvent onLookInput;
		private event CrouchInputEvent onCrouchInput;
		public event PlayerKilledEvent OnPlayerKilled;
		[SerializeField] private PlayerCamera playerCamera;
		[SerializeField] private GameObject playerPhone;
		private CancellationTokenSource cts;
		private float hideHeight = 0.3f;
		private bool isHiding;
		private bool underBed;
		private bool inCloset;
		private Vector3 enterPos;
		private float cachedSpeed;
		private AudioSource audioSource;

		protected override void Awake()
		{
			base.Awake();
			playerInput = GetComponent<PlayerInput>();
			playerCollider = GetComponent<Collider>();
			setInputActions();
			cts = new CancellationTokenSource();
			cachedSpeed = speed;
			playerCamera = GetComponentInChildren<PlayerCamera>();
			audioSource = GetComponent<AudioSource>();
		}

		private void OnDestroy()
		{
			removeInputActions();
		}

		private void setInputActions()
		{
			if (playerInput == null || inputActionsBound) { return; }

			playerInput.actions["Move"].performed += onMovePerformed;
			playerInput.actions["Move"].canceled += onMoveCanceled;
			onMoveInput += movement;
			playerInput.actions["Look"].performed += onLookPerformed;
			onLookInput += playerCamera.OnLook;
			playerInput.actions["Crouch"].performed += onCrouchPerformed;
			onCrouchInput += crouch;
			playerInput.actions["Interact"].started += onInteractStarted;
			inputActionsBound = true;
		}

		private void removeInputActions()
		{
			if (playerInput == null || !inputActionsBound) { return; }

			playerInput.actions["Move"].performed -= onMovePerformed;
			playerInput.actions["Move"].canceled -= onMoveCanceled;
			playerInput.actions["Look"].performed -= onLookPerformed;
			playerInput.actions["Crouch"].performed -= onCrouchPerformed;
			playerInput.actions["Interact"].started -= onInteractStarted;
			onMoveInput -= movement;
			if (playerCamera != null) { onLookInput -= playerCamera.OnLook; }
			onCrouchInput -= crouch;
			inputActionsBound = false;
		}

		private void LateUpdate()
		{
			if (velocity.magnitude > 5.1f) //lowest is 5 for some reason
			{
				if (!audioSource.isPlaying) { audioSource.Play(); }
			}
			else
			{
				if (audioSource.isPlaying) { audioSource.Stop(); }
			}
		}

		private void crouch(InputAction.CallbackContext ctx)
		{
			if (isHiding) { return; }
			switch (isCrouched)
			{
				case false:
					playerCollider.transform.localScale = new Vector3(1, 0.5f, 1);
					playerPhone.transform.localScale = new Vector3(0.008f, 0.012f, 0.024f);
					isCrouched = true;
					break;
				case true:
					playerCollider.transform.localScale = new Vector3(1, 1, 1);
					playerPhone.transform.localScale = new Vector3(0.008f, 0.012f, 0.012f);
					isCrouched = false;
					break;
			}
		}

		private void exitHiding()
		{
			if (!isHiding) { return; }
			if (underBed) { exitBed(); }
			if (inCloset) { exitCloset(); }
		}

		public void HideUnderBed(Transform _underBed)
		{
			playerPhone.SetActive(false);
			underBed = true;
			StartCoroutine(enterExitCooldown());
			speed = 0;
			enterPos = transform.position;
			transform.localScale = new Vector3(1, hideHeight, 1);
			characterController.enabled = false;
			transform.position = _underBed.position;
			Logger.Log("Under bed");
		}

		private void exitBed()
		{
			playerPhone.SetActive(true);
			isHiding = false;
			speed = cachedSpeed;
			transform.position = enterPos;
			characterController.enabled = true;
			transform.localScale = new Vector3(1, 1, 1);
		}

		public void HideInCloset(Transform _closet)
		{
			playerPhone.SetActive(false);
			inCloset = true;
			StartCoroutine(enterExitCooldown());
			speed = 0;
			enterPos = transform.position;
			characterController.enabled = false;
		}

		private void exitCloset()
		{
			playerPhone.SetActive(true);
			inCloset = false;
			isHiding = false;
			speed = cachedSpeed;
			transform.position = enterPos;
			characterController.enabled = true;
		}

		private IEnumerator enterExitCooldown()
		{
			yield return new WaitForSeconds(0.5f);
			isHiding = true;
		}

		private void onMovePerformed(InputAction.CallbackContext _ctx)
		{
			onMoveInput?.Invoke(_ctx.ReadValue<Vector2>());
		}

		private void onMoveCanceled(InputAction.CallbackContext _ctx)
		{
			onMoveInput?.Invoke(Vector2.zero);
		}

		private void onLookPerformed(InputAction.CallbackContext _ctx)
		{
			onLookInput?.Invoke(_ctx);
		}

		private void onCrouchPerformed(InputAction.CallbackContext _ctx)
		{
			onCrouchInput?.Invoke(_ctx);
		}

		private void onInteractStarted(InputAction.CallbackContext _ctx)
		{
			exitHiding();
		}

		private void disablePlayerControl()
		{
			removeInputActions();
			ClearMovementState();
			speed = 0f;
			if (playerCamera != null) { playerCamera.SetMouseSens(0f); }

			if (characterController != null && characterController.enabled)
			{
				characterController.Move(Vector3.zero);
			}

			if (audioSource != null && audioSource.isPlaying)
			{
				audioSource.Stop();
			}
		}

		public void lockPlayer(Transform lookPoint, float lookTime)
		{
			camLookAt(lookPoint, lookTime).Forget();
			Camera.main.fieldOfView = 40f; //zoom in on killer
			disablePlayerControl();
		}

		public bool CheckIfHiding()
		{
			return isHiding;
		}

		public bool CheckIfUnderBed()
		{
			return underBed;
		}

		public bool CheckIfInCloset()
		{
			return inCloset;
		}

		public void InvokePlayerDeath()
		{
			disablePlayerControl();
			OnPlayerKilled?.Invoke();
		}

		private async UniTask camLookAt(Transform lookPoint, float lookAtTime)
		{
			float elapsedTime = 0;
			try
			{
				while (elapsedTime <= lookAtTime)
				{
					if (!playerCamera) { return; }
					playerCamera.transform.LookAt(lookPoint);
					elapsedTime += Time.deltaTime;
					await UniTask.Yield(cancellationToken: cts.Token);
				}
			}
			catch (OperationCanceledException) { }
		}
	}
}
