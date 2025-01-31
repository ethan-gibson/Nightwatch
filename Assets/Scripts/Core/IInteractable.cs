namespace Game.Core
{
	public interface IInteractable
	{
		public float MaxRange { get; }
		public string InteractionText { get; }
		public void OnStartHover();
		public void OnInteract();
		public void OnEndHover();
	}
}