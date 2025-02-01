using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Entities
{
	public class PlayerCamera : MonoBehaviour
	{
		[SerializeField] private Transform playerTransform;
		[SerializeField] private float mouseSens = 3f;
		[SerializeField] private float maxLookAngle = 85f;
		private Vector2 lookInput;
		private float xRotation = 0f;

		void Start()
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
		}

		private void Update()
		{
			SetRotation();
		}

		public void OnLook(InputAction.CallbackContext _ctx)
		{
			lookInput = _ctx.ReadValue<Vector2>() * Time.deltaTime * mouseSens;
		}

		private void SetRotation()
		{
			if (lookInput.sqrMagnitude < 0.1f) { return; }
			Vector2 _lookInput = lookInput;
			playerTransform.Rotate(Vector3.up, _lookInput.x);

			xRotation -= lookInput.y;
			xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);
			transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
		}
	}
}