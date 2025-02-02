using Game.Core;
using Game.Entities;
using Game.Manager;
using UnityEngine;

public class Bed : MonoBehaviour,IInteractable
{
    [field: SerializeField] public float MaxRange { get; set; }
    [field: SerializeField] public string InteractionText { get; set; }
    private bool isHiding = false;
    private Vector3 playerReturnLocation;
    private PlayerMovement player;
    [SerializeField] private Transform playerHideLocation;

    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerMovement>();
    }
    public void OnStartHover()
    {
        Debug.Log(InteractionText);
        //HUDManager.Instance.SetInteractionText(InteractionText);
    }
    public void OnInteract()
    {
        Debug.unityLogger.Log(isHiding);
        player.HideUnderBed(playerHideLocation);
        
    }
    public void OnEndHover()
    {
        //HUDManager.Instance.SetInteractionText("");
    }
}
