using Game.Core;
using UnityEngine;

public class Door : MonoBehaviour, IInteractable
{
	[field: SerializeField] public float MaxRange { get; set; } = 5f;
	[field: SerializeField] public string InteractionText { get; set; }
	private bool isOpen;

	public void OnStartHover()
	{
		//hudmanager.text
	}

	public void OnInteract()
	{
		float _yRotation = isOpen ? 90f : 0f;
		//if (transform == null) { return; }
		transform.rotation = Quaternion.Euler(0f, _yRotation, 0f);
		isOpen = !isOpen;
	}

	public void OnEndHover() { }
}