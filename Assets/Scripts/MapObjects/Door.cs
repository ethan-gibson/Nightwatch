using Game.Core;
using Game.Entities.Octree;
using UnityEngine;
using Game.Manager;

public class Door : MonoBehaviour, IInteractable
{
	[field: SerializeField]
	public float MaxRange { get; set; } = 5f;
	[field: SerializeField]
	public string InteractionText { get; set; }
	private bool isOpen;
	[SerializeField]
	private float openRot;
	[SerializeField]
	private float closeRot;
	[SerializeField]
	private bool isClosetDoor;

	private Collider doorCollider;
	private Bounds doorwayBounds;
	private LayerMask obstacleLayer;
	private LayerMask nonObstacleLayer;

	private void Awake()
	{
		doorCollider = GetComponentInChildren<Collider>();
		if (!doorCollider)
		{
			Debug.LogError("Door has no collider!", this);
			return;
		}

		obstacleLayer = doorCollider.gameObject.layer;
		nonObstacleLayer = LayerMask.NameToLayer("Default");

		doorwayBounds = doorCollider.bounds;
	}

	public void OnStartHover()
	{
		HUDManager.Instance.SetInteractionText(InteractionText);
	}

	public void OnInteract()
	{
		float yRotation = isOpen ? closeRot : openRot;
		transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
		isOpen = !isOpen;

		// Update collider layer so octree ignores it when open
		doorCollider.gameObject.layer = isOpen ? nonObstacleLayer : obstacleLayer;

		notifyDoorStateChanged();
	}

	private void OnCollisionEnter(Collision collision)
	{
		// Anomaly can still open the door even if it's closed
		if (collision.gameObject.CompareTag("Anomaly") && !isOpen && !isClosetDoor) { OnInteract(); }
	}

	public void OnEndHover()
	{
		HUDManager.Instance.SetInteractionText("");
	}

	private void notifyDoorStateChanged()
	{
		OctreeDoorEvents.NotifyDoorStateChanged(doorwayBounds);
	}
}