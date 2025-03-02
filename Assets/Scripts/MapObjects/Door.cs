using Game.Core;
using UnityEngine;
using Game.Manager;

public class Door : MonoBehaviour, IInteractable
{
	[field: SerializeField] public float MaxRange { get; set; } = 5f;
	[field: SerializeField] public string InteractionText { get; set; }
	private bool isOpen;
	[SerializeField] private float openRot;
	[SerializeField] private float closeRot;

	public void OnStartHover()
	{
		HUDManager.Instance.SetInteractionText(InteractionText);
	}

	public void OnInteract()
	{
		float _yRotation = isOpen ? closeRot : openRot;
		//if (transform == null) { return; }
		transform.localRotation = Quaternion.Euler(0f, _yRotation, 0f);
		isOpen = !isOpen;
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (collision.gameObject.CompareTag("Anomaly") && !isOpen) { OnInteract(); }
		//if door is shut and stalker hits it, open
	}

	public void OnEndHover()
	{
		HUDManager.Instance.SetInteractionText("");
	}
}