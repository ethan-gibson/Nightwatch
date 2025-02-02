using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Entities
{
	public class PlayerMovement : EntityMovement
	{
		private PlayerInput playerInput;
		private bool isCrouched;
		private Collider playerCollider;

		public delegate void LookInputEvent(InputAction.CallbackContext _ctx);

		public delegate void CrouchInputEvent(InputAction.CallbackContext _ctx);

		private event LookInputEvent onLookInput;
		private event CrouchInputEvent onCrouchInput;
		[SerializeField] private PlayerCamera playerCamera;
		private CancellationTokenSource cts;
		private float hideHeight = 0.3f;
		private bool isHiding;
		private bool underBed;
		private bool inCloset;
		private Vector3 enterPos;
		private float cachedSpeed;

		protected override void Awake()
		{
			base.Awake();
			playerInput = GetComponent<PlayerInput>();
			playerCollider = GetComponent<Collider>();
			setInputActions();
			cts = new CancellationTokenSource();
			cachedSpeed = speed;
		}

		private void OnDestroy()
		{
			removeInputActions();
		}

		private void setInputActions()
		{
			playerInput.actions["Move"].performed += _ctx => onMoveInput?.Invoke(_ctx.ReadValue<Vector2>());
			onMoveInput += movement;
			playerInput.actions["Look"].performed += _ctx => onLookInput?.Invoke(_ctx);
			onLookInput += playerCamera.OnLook;
			playerInput.actions["Crouch"].performed += _ctx => onCrouchInput?.Invoke(_ctx);
			onCrouchInput += crouch;
			playerInput.actions["Interact"].started += _ => exitHiding();
		}

		private void removeInputActions()
		{
			playerInput.actions["Move"].performed -= _ctx => onMoveInput?.Invoke(_ctx.ReadValue<Vector2>());
			playerInput.actions["Look"].performed -= _ctx => onMoveInput?.Invoke(_ctx.ReadValue<Vector2>());
			playerInput.actions["Crouch"].performed -= _ctx => onCrouchInput?.Invoke(_ctx);
			playerInput.actions["Interact"].started -= _ => exitHiding();
			onMoveInput -= movement;
			onLookInput -= playerCamera.OnLook;
			onCrouchInput -= crouch;
		}

		private void crouch(InputAction.CallbackContext ctx)
		{
			if (isCrouched) { return; }
			switch (isCrouched)
			{
				case false:
					playerCollider.transform.localScale = new Vector3(1, 0.5f, 1);
					isCrouched = true;
					break;
				case true:
					playerCollider.transform.localScale = new Vector3(1, 1, 1);
					isCrouched = false;
					break;
			}
		}

		private void exitHiding()
		{
			if (underBed) { exitBed(); }
			if (inCloset) { exitCloset(); }
		}

		public void HideUnderBed(Transform _underBed)
		{
			underBed = true;
			isHiding = true;
			speed = 0;
			enterPos = transform.position;
			transform.localScale = new Vector3(1, hideHeight, 1);
			characterController.enabled = false;
			transform.position = _underBed.position;
		}

		private void exitBed()
		{
			underBed = false;
			isHiding = false;
			speed = cachedSpeed;
			transform.position = enterPos;
			characterController.enabled = true;
			transform.localScale = new Vector3(1, 1, 1);
		}

		public void HideInCloset(Transform _closet)
		{
			inCloset = true;
			isHiding = true;
			speed = 0;
			enterPos = transform.position;
			characterController.enabled = false;
		}

		private void exitCloset()
		{
			inCloset = false;
			isHiding = false;
			speed = cachedSpeed;
			transform.position = enterPos;
			characterController.enabled = true;
		}

		public void lockPlayer(Transform lookPoint, float lookTime)
		{
			Debug.Log("controls locked");
			camLookAt(lookPoint, lookTime).Forget();
			removeInputActions();
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