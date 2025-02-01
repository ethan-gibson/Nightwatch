using UnityEngine;

namespace Game.Manager
{
	public class GameManager : MonoBehaviour
	{
		public static GameManager Instance;
		private int anomalyCounter = 0;
		[SerializeField] private int maxAnomalyWeight = 15;

		private void Start()
		{
			if (Instance == null) { Instance = this; }
			else { Destroy(gameObject); }
		}

		public void increaseAnomalyCount(int weight)
		{
			anomalyCounter += weight;
			if (anomalyCounter >= maxAnomalyWeight)
			{
				endGame();
			}
		}

		private void endGame()
		{
			//gg :)
		}
	}
}