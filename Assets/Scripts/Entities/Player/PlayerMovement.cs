using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Entities
{
	public class PlayerMovement : EntityMovement
	{
		private PlayerInput playerInput;

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
		}

		private void removeInputActions()
		{
			playerInput.actions["Move"].performed -= _ctx => onMoveInput?.Invoke(_ctx.ReadValue<Vector2>());
			onMoveInput -= movement;
		}
	}
}