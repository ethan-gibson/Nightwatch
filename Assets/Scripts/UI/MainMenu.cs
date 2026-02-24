using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.UI
{
	public class MainMenu : MonoBehaviour
	{
		public void Play()
		{
			SceneManager.LoadScene("GameMap");
		}

		public void Quit()
		{
			Application.Quit();
		}
	}
}
