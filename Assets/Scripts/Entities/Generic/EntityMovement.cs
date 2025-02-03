using UnityEngine;

namespace Game.Entities
{
	public abstract class EntityMovement : MonoBehaviour
	{
		protected CharacterController characterController { get; set; }
		[SerializeField] protected float speed = 5f;
		protected Vector3 velocity;
		private Vector3 movementDirection;

		protected delegate void MoveInputEvent(Vector2 _direction);

		protected MoveInputEvent onMoveInput;
		private const float gravity = 17f;
		private bool isGrounded => characterController.isGrounded;
		[SerializeField] [Range(0, .5f)] private float moveSmoothTime = .3f;
		private Vector2 currentDir;
		private Vector2 currentDirVelocity;

		protected virtual void Awake()
		{
			characterController = GetComponent<CharacterController>();
		}

		protected virtual void Update()
		{
			if (characterController.enabled) { characterController.Move(velocity * Time.deltaTime); }
		}

		protected virtual void FixedUpdate()
		{
			moveEntity();
			if (!isGrounded)
			{
				velocity.y -= gravity * Time.fixedDeltaTime;
				velocity.y = Mathf.Clamp(velocity.y, -20, 20);
			}
			else { velocity.y = -5; }
		}

		protected void movement(Vector2 _movementDirection)
		{
			movementDirection = new Vector3(_movementDirection.x, 0, _movementDirection.y);
		}

		protected virtual void moveEntity()
		{
			currentDir = Vector2.SmoothDamp(currentDir, new(movementDirection.x, movementDirection.z), ref currentDirVelocity, moveSmoothTime);
			velocity = (transform.forward * currentDir.y + transform.right * currentDir.x) * speed + Vector3.up * velocity.y;
		}
	}
}