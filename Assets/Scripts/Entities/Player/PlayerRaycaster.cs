using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Entities
{
	/// <summary>
	/// This script handles the interaction raycast for the player
	/// </summary>
	public class PlayerRaycaster : MonoBehaviour
	{
		private const float range = 100;
		private IInteractable currentTarget;
		[SerializeField] private LayerMask mask;
		[SerializeField] private GameObject RaycastCamera;
		private PlayerInput playerInput;

		private void Awake()
		{
			playerInput = GetComponent<PlayerInput>();
			setInputActions();
		}

		private void setInputActions()
		{
			playerInput.actions["Interact"].started += _ => interact();
		}

		private void removeInputActions()
		{
			playerInput.actions["Interact"].started -= _ => interact();
		}

		private void OnDestroy()
		{
			removeInputActions();
		}

		private void Update()
		{
			raycastForInteractable();
		}

		private void interact()
		{
			currentTarget?.OnInteract();
		}

		private void raycastForInteractable()
		{
			Ray _ray = new Ray(RaycastCamera.transform.position, RaycastCamera.transform.forward);
			//start
			if (!Physics.Raycast(_ray, out var _hit, range, mask))
			{
				clearTarget();
				return;
			}
			//Debug.Log("We Hit" + whatIHit.collider.name + " " + whatIHit.point);
			if (!_hit.collider.TryGetComponent<IInteractable>(out var _interactable))
			{
				clearTarget();
				return;
			}
			if (_hit.distance > _interactable.MaxRange)
			{
				clearTarget();
				return;
			}
			if (_interactable == currentTarget) { return; }
			//HUDManager.Instance.SetInteractionText(_interactable.InteractionText);
			currentTarget = _interactable;
			currentTarget.OnStartHover();
		}

		private void clearTarget()
		{
			if (currentTarget == null) { return; }
			currentTarget.OnEndHover();
			currentTarget = null;
			//HUDManager.Instance.SetInteractionText("");
		}
	}
}