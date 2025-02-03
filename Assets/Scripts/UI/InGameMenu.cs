using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

namespace Game.UI
{
	public class InGameMenu : MonoBehaviour
	{
		[SerializeField] private TextMeshProUGUI gameOverText;

		public void SetGameOverText(string _text)
		{
			gameOverText.text = _text;
		}

		public void RestartGame()
		{
			Time.timeScale = 1;
			SceneManager.LoadScene("AnomalyTest");
		}

		public void MainMenu()
		{
			Time.timeScale = 1;
			SceneManager.LoadScene("MainMenu");
		}

		public void QuitGame()
		{
			Application.Quit();
		}
	}
}
