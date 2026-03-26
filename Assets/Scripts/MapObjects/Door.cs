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
	[SerializeField]
	private float openRot;
	[SerializeField]
	private float closeRot;
	[SerializeField]
	private bool isClosetDoor;
	[SerializeField]
	private LayerMask openDoorLayer = 1;
	[SerializeField]
	private LayerMask closedDoorLayer = 1 << 12;

	private bool isOpen;
	private Collider doorCollider;
	private Bounds doorwayBounds;

	private void Awake()
	{
		doorCollider = GetComponentInChildren<Collider>();
		if (!doorCollider)
		{
			Debug.LogError("Door has no collider!", this);
			enabled = false;
			return;
		}

		ensureDoorLayers();
		doorwayBounds = buildDoorwayBounds();
		isOpen = resolveStartingState();
		applyDoorState(isOpen);
	}

	public void OnStartHover()
	{
		HUDManager.Instance.SetInteractionText(InteractionText);
	}

	public void OnInteract()
	{
		setDoorState(!isOpen, true);
	}

	private void OnCollisionEnter(Collision _collision)
	{
		if (_collision.gameObject.CompareTag("Anomaly") && !isOpen && !isClosetDoor) { OnInteract(); }
	}

	public void OnEndHover()
	{
		HUDManager.Instance.SetInteractionText("");
	}

	private void setDoorState(bool _isOpen, bool _notifyDoorStateChanged)
	{
		isOpen = _isOpen;
		applyDoorState(isOpen);

		if (_notifyDoorStateChanged)
		{
			notifyDoorStateChanged();
		}
	}

	private void applyDoorState(bool _isOpen)
	{
		float _yRotation = _isOpen ? openRot : closeRot;
		transform.localRotation = Quaternion.Euler(0f, _yRotation, 0f);
		doorCollider.gameObject.layer = getLayerIndex(_isOpen ? openDoorLayer : closedDoorLayer);
	}

	private bool resolveStartingState()
	{
		float _currentYRotation = transform.localEulerAngles.y;
		float _openDelta = Mathf.Abs(Mathf.DeltaAngle(_currentYRotation, openRot));
		float _closeDelta = Mathf.Abs(Mathf.DeltaAngle(_currentYRotation, closeRot));
		return _openDelta < _closeDelta;
	}

	private Bounds buildDoorwayBounds()
	{
		Quaternion _startingRotation = transform.localRotation;
		transform.localRotation = Quaternion.Euler(0f, closeRot, 0f);
		Physics.SyncTransforms();
		Bounds _combinedBounds = doorCollider.bounds;
		transform.localRotation = Quaternion.Euler(0f, openRot, 0f);
		Physics.SyncTransforms();
		_combinedBounds.Encapsulate(doorCollider.bounds);
		transform.localRotation = _startingRotation;
		Physics.SyncTransforms();
		return _combinedBounds;
	}

	private void ensureDoorLayers()
	{
		if (openDoorLayer.value == 0)
		{
			int _defaultLayerIndex = LayerMask.NameToLayer("Default");
			openDoorLayer = toLayerMask(_defaultLayerIndex);
		}

		if (closedDoorLayer.value == 0)
		{
			int _obstacleLayerIndex = LayerMask.NameToLayer("Obstacle");
			closedDoorLayer = toLayerMask(_obstacleLayerIndex);
		}
	}

	private static int getLayerIndex(LayerMask _layerMask)
	{
		int _layerValue = _layerMask.value;
		if (_layerValue <= 0) { return 0; }

		int _layerIndex = 0;
		while ((_layerValue & 1) == 0)
		{
			_layerValue >>= 1;
			_layerIndex++;
		}

		return _layerIndex;
	}

	private static LayerMask toLayerMask(int _layerIndex)
	{
		if (_layerIndex < 0) { return 0; }
		return 1 << _layerIndex;
	}

	private void notifyDoorStateChanged()
	{
		OctreeDoorEvents.NotifyDoorStateChanged(doorwayBounds);
	}
}
