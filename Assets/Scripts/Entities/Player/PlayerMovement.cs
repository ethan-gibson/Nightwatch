using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Entities
{
	public class PlayerMovement : EntityMovement
	{
		private PlayerInput playerInput;
		
		public delegate void LookInputEvent(InputAction.CallbackContext _ctx);
		private event LookInputEvent onLookInput;
		[SerializeField] private PlayerCamera playerCamera;

		protected override void Awake()
		{
			base.Awake();
			playerInput = GetComponent<PlayerInput>();
			setInputActions();
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
		}

		private void removeInputActions()
		{
			playerInput.actions["Move"].performed -= _ctx => onMoveInput?.Invoke(_ctx.ReadValue<Vector2>());
			playerInput.actions["Look"].performed -= _ctx => onMoveInput?.Invoke(_ctx.ReadValue<Vector2>());
			onMoveInput -= movement;
			onLookInput -= playerCamera.OnLook;
		}
	}
}