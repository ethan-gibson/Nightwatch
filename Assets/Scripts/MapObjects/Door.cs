using Game.Core;
using Game.Entities.Octree;
using UnityEngine;
using Game.Manager;

public class Door : MonoBehaviour, IInteractable
{
	[field: SerializeField] public float MaxRange { get; set; } = 5f;
	[field: SerializeField] public string InteractionText { get; set; }
	private bool isOpen;
	[SerializeField] private float openRot;
	[SerializeField] private float closeRot;
	[SerializeField] private bool isClosetDoor;

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
		notifyDoorStateChanged();
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (collision.gameObject.CompareTag("Anomaly") && !isOpen && !isClosetDoor) { OnInteract(); }
		//if door is shut and stalker hits it, open
	}

	public void OnEndHover()
	{
		HUDManager.Instance.SetInteractionText("");
	}

	private void notifyDoorStateChanged()
	{
		Collider _doorCollider = GetComponentInChildren<Collider>();
		if (_doorCollider != null)
		{
			OctreeDoorEvents.NotifyDoorStateChanged(_doorCollider.bounds);
			return;
		}

		OctreeDoorEvents.NotifyDoorStateChanged(new Bounds(transform.position, Vector3.one));
	}
}
