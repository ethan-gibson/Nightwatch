using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Entities;
using UnityEngine;
using Game.UI;
using Random = UnityEngine.Random;

namespace Game.Manager
{
	public class GameManager : MonoBehaviour
	{
		public static GameManager Instance;
		private int anomalyCounter = 0;
		[SerializeField] private int maxAnomalyWeight = 15;
		[SerializeField] private int anomalyWarningAmount = 10;
		[SerializeField] private InGameMenu inGameMenu;
		[SerializeField] private FlickeringLights flickeringLights;
		private bool playerWarned;
		private HUDManager hudManager;
		[SerializeField] private AnomalyMain[] anomalies;
		[SerializeField] private float gameLenght = 500;
		[SerializeField] private float anomalyCooldown = 40f;
		[SerializeField] private float anomalyCooldownReduction = 4f;
		private float hour;
		private Transform player;
		private PlayerPhone playerPhone;
		[SerializeField] private Material staticMaterial;
		private CancellationTokenSource countDownCts;
		private CancellationTokenSource anomalyTriggerCts;
		private CancellationTokenSource hunterSpawnerCts;
		private GameObject[] stalkerEntryExitPoints;
		[SerializeField] private GameObject stalkerPrefab;
		[SerializeField] private float stalkerCooldown = 130f; //would be 110ish since hes only on the map for 20s

		private void Start()
		{
			if (Instance == null) { Instance = this; }
			else { Destroy(gameObject); }
			hudManager = GetComponent<HUDManager>();
			countDownCts = new CancellationTokenSource();
			anomalyTriggerCts = new CancellationTokenSource();
			hunterSpawnerCts = new CancellationTokenSource();
			stalkerEntryExitPoints = GameObject.FindGameObjectsWithTag("StalkerEnterExit");
			anomalies = FindObjectsOfType<AnomalyMain>();
			foreach (var _anomaly in anomalies) { _anomaly.AnomalySpawnedEvent += increaseAnomalyCount; }
			player = GameObject.FindGameObjectWithTag("Player").transform;
			player.GetComponent<PlayerMovement>().OnPlayerKilled += CallMenu;
			playerPhone = player.GetComponentInChildren<PlayerPhone>();
			playerPhone.Report += reportCheck;
			countDown().Forget();
			anomalyTrigger().Forget();
			hunterSpawner().Forget();
		}

		private void OnDestroy()
		{
			foreach (var _anomaly in anomalies) { _anomaly.AnomalySpawnedEvent -= increaseAnomalyCount; }
			player.GetComponent<PlayerMovement>().OnPlayerKilled -= CallMenu;
			playerPhone.Report -= reportCheck;
			countDownCts?.Cancel();
			countDownCts?.Dispose();
			anomalyTriggerCts?.Cancel();
			anomalyTriggerCts?.Dispose();
			hunterSpawnerCts?.Cancel();
			hunterSpawnerCts?.Dispose();
		}

		private void increaseAnomalyCount(int _weight)
		{
			anomalyCounter += _weight;
			Debug.Log(anomalyCounter);
			if (anomalyCounter >= maxAnomalyWeight) { bringUpMenu(); } //lost game
			if (anomalyCounter == anomalyWarningAmount && !playerWarned) { warnPlayer(); }
			staticMaterial.SetFloat("_staticCoverage", (float)anomalyCounter / maxAnomalyWeight);
		}

		public void CallMenu()
		{
			bringUpMenu();
		}

		private void warnPlayer()
		{
			playerWarned = true;
			playerPhone.PlayWarning();
			StopAllCoroutines();
			StartCoroutine(setWarningText());
		}

		private void bringUpMenu(string _text = "Game Over")
		{
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
			inGameMenu.gameObject.SetActive(true);
			inGameMenu.SetGameOverText(_text);
			Time.timeScale = 0;
		}

		private void reportCheck(bool _check)
		{
			StartCoroutine(setReportText(_check));
		}

		private IEnumerator setWarningText()
		{
			hudManager.SetReportText("WARNING: TOO MANY ANOMALIES", Color.red);
			yield return new WaitForSeconds(3f);
			hudManager.SetReportText("", Color.black);
		}

		private IEnumerator setReportText(bool _check)
		{
			if (_check) { hudManager.SetReportText("Anomalies Reported", Color.green); }
			else
			{
				hudManager.SetReportText("No Anomalies Found", Color.red);
				anomalyBoost();
			}
			yield return new WaitForSeconds(3f);
			hudManager.SetReportText("", Color.black); //color doesnt matter here
		}

		private void anomalyBoost()
		{
			if (anomalyCooldown <= 2) { return; }
			anomalyCooldown -= 1;
		}

		private async UniTask countDown()
		{
			countDownCts?.Cancel();
			countDownCts?.Dispose();
			countDownCts = new CancellationTokenSource();
			float waitTime = gameLenght / 6;
			try
			{
				while (waitTime >= 0)
				{
					waitTime -= Time.deltaTime;
					await UniTask.Yield(cancellationToken: countDownCts.Token);
				}
				if (anomalyCooldown >= 5) { anomalyCooldown -= anomalyCooldownReduction; }
				hour++;
				if (hour >= 6) { bringUpMenu("Anomalies Defeated"); }
				hudManager.UpdateGameTime(hour.ToString());
				countDown().Forget();
			}
			catch (OperationCanceledException) { }
		}

		private async UniTask anomalyTrigger()
		{
			anomalyTriggerCts?.Cancel();
			anomalyTriggerCts?.Dispose();
			anomalyTriggerCts = new CancellationTokenSource();
			float _waitTime = anomalyCooldown;
			try
			{
				while (_waitTime >= 0)
				{
					_waitTime -= Time.deltaTime;
					await UniTask.Yield(cancellationToken: anomalyTriggerCts.Token);
				}
				Debug.Log("triggering anomaly");
				getAnomalyToTrigger();
			}
			catch (OperationCanceledException) { }
		}

		private void getAnomalyToTrigger()
		{
			int _x = Random.Range(0, anomalies.Length);
			if (anomalies[_x].IsVisible() || anomalies[_x].IsChanged())
			{
				getAnomalyToTrigger();
				return;
			}
			Debug.Log(anomalies[_x].name + " was activated");
			anomalies[_x].CallChangeAnomaly();
			anomalyTrigger().Forget();
		}

		private async UniTask hunterSpawner()
		{
			hunterSpawnerCts?.Cancel();
			hunterSpawnerCts?.Dispose();
			hunterSpawnerCts = new CancellationTokenSource();
			float _waitTime = stalkerCooldown;
			try
			{
				while (true)
				{
					while (_waitTime >= 0)
					{
						_waitTime -= Time.deltaTime;
						await UniTask.Yield(cancellationToken: hunterSpawnerCts.Token);
					}
					SpawnHunter();
					_waitTime = stalkerCooldown; // Reset the cooldown
				}
			}
			catch (OperationCanceledException) { }
		}

		// Method to spawn the hunter enemy
		private void SpawnHunter()
		{
			if (stalkerPrefab == null)
			{
				Debug.LogError("Hunter prefab is not assigned!");
				return;
			}

			// Choose a random entry/exit point for the hunter
			if (stalkerEntryExitPoints.Length == 0)
			{
				Debug.LogError("No stalker entry/exit points found!");
				return;
			}

			int randomIndex = Random.Range(0, stalkerEntryExitPoints.Length);
			Transform spawnPoint = stalkerEntryExitPoints[randomIndex].transform;

			// Instantiate the hunter at the chosen spawn point
			Instantiate(stalkerPrefab, spawnPoint.position, spawnPoint.rotation);
			flickeringLights.StartFlickering(30); //stalker lifetime plus extra so he will lights stay flickering for a bit
		}
	}
}