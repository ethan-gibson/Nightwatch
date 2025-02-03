using Game.Core;
using Game.Entities;
using Game.Manager;
using UnityEngine;

public class Bed : MonoBehaviour,IInteractable
{
    [field: SerializeField] public float MaxRange { get; set; }
    [field: SerializeField] public string InteractionText { get; set; }
    private Vector3 playerReturnLocation;
    private PlayerMovement player;
    [SerializeField] private Transform playerHideLocation;

    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerMovement>();
    }
    public void OnStartHover()
    {
        HUDManager.Instance.SetInteractionText(InteractionText);
    }
    public void OnInteract()
    {
        player.HideUnderBed(playerHideLocation);
        
    }
    public void OnEndHover()
    {
        HUDManager.Instance.SetInteractionText("");
    }
}
