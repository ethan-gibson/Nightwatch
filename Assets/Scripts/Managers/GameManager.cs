using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using Arti.Utilities;
using Cysharp.Threading.Tasks;
using Game.Entities;
using UnityEngine;
using Game.UI;
using Random = UnityEngine.Random;
using Logger = Arti.Utilities.Logger;

namespace Game.Manager
{
	public class GameManager : InstanceFactory<GameManager>
	{
		[SerializeField]
		private int maxAnomalyWeight = 15;
		[SerializeField]
		private int anomalyWarningAmount = 10;
		[SerializeField]
		private int falseReportPenalty = 1;

		[SerializeField]
		private InGameMenu inGameMenu;
		[SerializeField]
		private FlickeringLights flickeringLights;
		[SerializeField]
		private Material staticMaterial;
		[SerializeField]
		private AnomalyMain[] anomalies;
		[SerializeField]
		private GameObject stalkerPrefab;

		[SerializeField]
		private float gameLenght = 360f;
		[SerializeField]
		private float anomalyCooldown = 40f;
		[SerializeField]
		private float anomalyCooldownReduction = 4f;
		[SerializeField]
		private float stalkerCooldown = 130f;

		[Header("Director / Difficulty")]
		[SerializeField]
		private float minimumAnomalyCooldown = 8f;

		[SerializeField]
		private float minimumStalkerCooldown = 55f;
		[SerializeField]
		private float reportPressureGainOnFalseReport = 0.18f;
		[SerializeField]
		private float reportPressureReliefOnCorrectReport = 0.10f;
		[SerializeField]
		private float reportPressureDecayPerSecond = 0.03f;

		[Header("Stalker Events")]
		[SerializeField]
		private float emergencyStalkerPressureThreshold = 0.80f;

		[SerializeField]
		private float emergencyStalkerDelay = 20f;
		[SerializeField]
		private float stalkerFlickerDuration = 30f;

		private int anomalyCounter;
		private bool playerWarned;
		private bool gameEnded;
		private float hour;
		private float elapsedGameTime;
		private float reportPressure;
		private float baseAnomalyCooldown;
		private float baseStalkerCooldown;
		private float lastStalkerSpawnTime = Mathf.NegativeInfinity;

		private HUDManager hudManager;
		private Transform player;
		private PlayerMovement playerMovement;
		private PlayerPhone playerPhone;
		private GameObject activeStalkerInstance;
		private GameObject[] stalkerEntryExitPoints;

		private CancellationTokenSource countDownCts;
		private CancellationTokenSource anomalyTriggerCts;
		private CancellationTokenSource hunterSpawnerCts;

		protected override void Awake()
		{
			base.Awake();
			if (Instance != this) { return; }

			hudManager = GetComponent<HUDManager>();
			gameLenght = Mathf.Max(30f, gameLenght);
			baseAnomalyCooldown = Mathf.Max(2f, anomalyCooldown);
			baseStalkerCooldown = Mathf.Max(10f, stalkerCooldown);
			anomalyCooldown = baseAnomalyCooldown;
			stalkerCooldown = baseStalkerCooldown;
		}

		private void Start()
		{
			stalkerEntryExitPoints = GameObject.FindGameObjectsWithTag("StalkerEnterExit");
			anomalies = FindObjectsByType<AnomalyMain>(FindObjectsSortMode.None);
			foreach (AnomalyMain _anomaly in anomalies)
			{
				if (_anomaly) { _anomaly.AnomalySpawnedEvent += increaseAnomalyCount; }
			}

			player = GameObject.FindGameObjectWithTag("Player")?.transform;
			if (player)
			{
				playerMovement = player.GetComponent<PlayerMovement>();
				if (playerMovement) { playerMovement.OnPlayerKilled += CallMenu; }

				playerPhone = player.GetComponentInChildren<PlayerPhone>();
				if (playerPhone) { playerPhone.Report += reportCheck; }
			}

			EnsureExistingStalkerRuntimeBridges();
			hudManager?.UpdateGameTime(hour.ToString(CultureInfo.InvariantCulture));
			refreshDirectorRates();
			countDown().Forget();
			anomalyTrigger().Forget();
			hunterSpawner().Forget();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			stopDirectorLoops();

			if (anomalies != null)
			{
				foreach (AnomalyMain _anomaly in anomalies)
				{
					if (_anomaly) { _anomaly.AnomalySpawnedEvent -= increaseAnomalyCount; }
				}
			}

			if (playerMovement) { playerMovement.OnPlayerKilled -= CallMenu; }
			if (playerPhone) { playerPhone.Report -= reportCheck; }
		}

		private void stopDirectorLoops()
		{
			countDownCts.Reset();
			anomalyTriggerCts.Reset();
			hunterSpawnerCts.Reset();
			countDownCts = null;
			anomalyTriggerCts = null;
			hunterSpawnerCts = null;
		}

		private void increaseAnomalyCount(int _weight)
		{
			if (gameEnded) { return; }

			int scoreCap = Mathf.Max(1, maxAnomalyWeight);
			anomalyCounter = Mathf.Clamp(anomalyCounter + _weight, 0, scoreCap);

			if (staticMaterial) { staticMaterial.SetFloat("_staticCoverage", (float)anomalyCounter / scoreCap); }

			if (anomalyCounter >= scoreCap)
			{
				bringUpMenu();
				return;
			}

			if (anomalyCounter >= anomalyWarningAmount && !playerWarned) { warnPlayer(); }
			refreshDirectorRates();
		}

		public void CallMenu()
		{
			bringUpMenu();
		}

		private void warnPlayer()
		{
			playerWarned = true;
			if (playerPhone) { playerPhone.PlayWarning(); }
			StopAllCoroutines();
			StartCoroutine(setWarningText());
		}

		private void bringUpMenu(string _text = "Game Over")
		{
			if (gameEnded) { return; }
			gameEnded = true;
			stopDirectorLoops();

			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
			if (inGameMenu)
			{
				inGameMenu.gameObject.SetActive(true);
				inGameMenu.SetGameOverText(_text);
			}
			Time.timeScale = 0;
		}

		private void reportCheck(bool _check)
		{
			OnReportResult(_check);
			StartCoroutine(setReportText(_check));
		}

		public void OnReportResult(bool _result)
		{
			if (gameEnded) { return; }

			if (_result)
			{
				reportPressure = Mathf.Max(0f, reportPressure - Mathf.Max(0f, reportPressureReliefOnCorrectReport));
				refreshDirectorRates();
				return;
			}

			reportPressure = Mathf.Clamp01(reportPressure + Mathf.Max(0f, reportPressureGainOnFalseReport));
			if (falseReportPenalty > 0) { increaseAnomalyCount(falseReportPenalty); }
			tryTriggerEmergencyStalkerSpawn();
			refreshDirectorRates();
		}

		private IEnumerator setWarningText()
		{
			if (hudManager == null) { yield break; }
			hudManager.SetReportText("WARNING: TOO MANY ANOMALIES", Color.red);
			yield return new WaitForSeconds(3f);
			hudManager.SetReportText("", Color.black);
		}

		private IEnumerator setReportText(bool _check)
		{
			if (hudManager == null) { yield break; }

			if (_check) { hudManager.SetReportText("Anomalies Reported", Color.green); }
			else { hudManager.SetReportText("No Anomalies Found", Color.red); }

			yield return new WaitForSeconds(3f);
			hudManager.SetReportText("", Color.black);
		}

		private async UniTask countDown()
		{
			CancellationToken cancellationToken = resetToken(ref countDownCts).Token;
			float hourLength = Mathf.Max(1f, gameLenght / 6f);
			float nextHourTimestamp = hourLength;

			try
			{
				while (!gameEnded)
				{
					elapsedGameTime += Time.deltaTime;
					reportPressure = Mathf.Max(0f, reportPressure - (Mathf.Max(0f, reportPressureDecayPerSecond) * Time.deltaTime));
					refreshDirectorRates();

					if (elapsedGameTime >= nextHourTimestamp && hour < 6f)
					{
						hour += 1f;
						hudManager?.UpdateGameTime(hour.ToString());
						nextHourTimestamp += hourLength;
					}

					if (elapsedGameTime >= gameLenght)
					{
						bringUpMenu("Anomalies Defeated");
						return;
					}

					await UniTask.Yield(cancellationToken: cancellationToken);
				}
			}
			catch (OperationCanceledException) { }
		}

		private async UniTask anomalyTrigger()
		{
			CancellationToken cancellationToken = resetToken(ref anomalyTriggerCts).Token;

			try
			{
				while (!gameEnded)
				{
					float waitTime = Mathf.Max(0.5f, anomalyCooldown);
					while (waitTime >= 0f && !gameEnded)
					{
						waitTime -= Time.deltaTime;
						await UniTask.Yield(cancellationToken: cancellationToken);
					}

					if (gameEnded) { return; }
					SpawnAnomaly();
				}
			}
			catch (OperationCanceledException) { }
		}

		public void SpawnAnomaly()
		{
			tryTriggerAnomaly();
		}

		private bool tryTriggerAnomaly()
		{
			if (anomalies == null || anomalies.Length == 0) { return false; }

			int startIndex = Random.Range(0, anomalies.Length);
			for (int i = 0; i < anomalies.Length; i++)
			{
				int index = (startIndex + i) % anomalies.Length;
				AnomalyMain anomaly = anomalies[index];
				if (anomaly == null || anomaly.IsVisible() || anomaly.IsChanged()) { continue; }

				anomaly.CallChangeAnomaly();
				return true;
			}

			return false;
		}

		private async UniTask hunterSpawner()
		{
			CancellationToken cancellationToken = resetToken(ref hunterSpawnerCts).Token;

			try
			{
				while (!gameEnded)
				{
					float waitTime = Mathf.Max(5f, stalkerCooldown);
					while (waitTime >= 0f && !gameEnded)
					{
						if (shouldForceStalkerSpawn()) { break; }

						waitTime -= Time.deltaTime;
						await UniTask.Yield(cancellationToken: cancellationToken);
					}

					if (gameEnded) { return; }
					SpawnHunter();
				}
			}
			catch (OperationCanceledException) { }
		}

		private bool shouldForceStalkerSpawn()
		{
			if (reportPressure < emergencyStalkerPressureThreshold) { return false; }
			if (hasActiveStalker()) { return false; }
			return (Time.time - lastStalkerSpawnTime) >= Mathf.Max(1f, emergencyStalkerDelay);
		}

		private void tryTriggerEmergencyStalkerSpawn()
		{
			if (!shouldForceStalkerSpawn()) { return; }
			SpawnHunter();
		}

		private bool hasActiveStalker()
		{
			if (activeStalkerInstance != null) { return true; }

			EnemyAI[] existingStalkers = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
			if (existingStalkers.Length > 0)
			{
				activeStalkerInstance = existingStalkers[0].gameObject;
				return true;
			}

			GameObject[] activeAnomalies = GameObject.FindGameObjectsWithTag("Anomaly");
			foreach (GameObject anomalyObject in activeAnomalies)
			{
				if (anomalyObject == null) { continue; }
				if (!anomalyObject.name.Contains("Stalker", StringComparison.OrdinalIgnoreCase)) { continue; }

				bool isStalkerCandidate = anomalyObject.GetComponent("BehaviorGraphAgent") != null ||
				                          anomalyObject.GetComponent<HuntingAnomaly>() != null;
				if (!isStalkerCandidate) { continue; }

				activeStalkerInstance = anomalyObject;
				return true;
			}

			return false;
		}

		private bool SpawnHunter()
		{
			if (gameEnded) { return false; }
			if (stalkerPrefab == null)
			{
				Logger.LogError("Hunter prefab is not assigned!");
				return false;
			}

			if (hasActiveStalker()) { return false; }

			if (stalkerEntryExitPoints == null || stalkerEntryExitPoints.Length == 0)
			{
				Logger.LogError("No stalker entry/exit points found!");
				return false;
			}

			int randomIndex = Random.Range(0, stalkerEntryExitPoints.Length);
			Transform spawnPoint = stalkerEntryExitPoints[randomIndex].transform;
			GameObject spawnedStalker = Instantiate(stalkerPrefab, spawnPoint.position, spawnPoint.rotation);
			ConfigureSpawnedStalker(spawnedStalker);
			activeStalkerInstance = spawnedStalker;
			lastStalkerSpawnTime = Time.time;
			reportPressure = Mathf.Max(0f, reportPressure - 0.12f);
			flickeringLights?.StartFlickering(stalkerFlickerDuration);
			return true;
		}

		private static void ConfigureSpawnedStalker(GameObject spawnedStalker)
		{
			if (spawnedStalker == null) { return; }

			if (spawnedStalker.GetComponent<EnemyAI>() == null)
			{
				spawnedStalker.AddComponent<EnemyAI>();
				Logger.LogWarning("Spawned stalker was missing EnemyAI. Added runtime bridge component automatically.");
			}

			if (spawnedStalker.TryGetComponent(out HuntingAnomaly legacyHuntingAnomaly) && legacyHuntingAnomaly.enabled) { legacyHuntingAnomaly.enabled = false; }
		}

		private static void EnsureExistingStalkerRuntimeBridges()
		{
			GameObject[] activeAnomalies = GameObject.FindGameObjectsWithTag("Anomaly");
			foreach (GameObject anomalyObject in activeAnomalies)
			{
				if (anomalyObject == null) { continue; }
				if (!anomalyObject.name.Contains("Stalker", StringComparison.OrdinalIgnoreCase)) { continue; }

				bool isStalkerCandidate = anomalyObject.GetComponent("BehaviorGraphAgent") != null ||
				                          anomalyObject.GetComponent<HuntingAnomaly>() != null;
				if (!isStalkerCandidate) { continue; }

				ConfigureSpawnedStalker(anomalyObject);
			}
		}

		private void refreshDirectorRates()
		{
			float pressure = getDirectorPressure01();

			float legacyAnomalyFloor = Mathf.Max(2f, baseAnomalyCooldown - (Mathf.Max(0f, anomalyCooldownReduction) * 6f));
			float anomalyFloor = Mathf.Max(2f, Mathf.Min(minimumAnomalyCooldown, legacyAnomalyFloor));
			anomalyFloor = Mathf.Clamp(anomalyFloor, 2f, baseAnomalyCooldown);

			float stalkerFloor = Mathf.Clamp(Mathf.Max(10f, minimumStalkerCooldown), 10f, baseStalkerCooldown);

			anomalyCooldown = Mathf.Lerp(baseAnomalyCooldown, anomalyFloor, pressure);
			stalkerCooldown = Mathf.Lerp(baseStalkerCooldown, stalkerFloor, pressure);
		}

		private float getDirectorPressure01()
		{
			float gameProgress = Mathf.Clamp01(elapsedGameTime / Mathf.Max(1f, gameLenght));
			float anomalyPressure = Mathf.Clamp01((float)anomalyCounter / Mathf.Max(1, maxAnomalyWeight));
			return Mathf.Clamp01((gameProgress * 0.5f) + (anomalyPressure * 0.35f) + reportPressure);
		}

		private static CancellationTokenSource resetToken(ref CancellationTokenSource tokenSource)
		{
			tokenSource?.Cancel();
			tokenSource?.Dispose();
			tokenSource = new CancellationTokenSource();
			return tokenSource;
		}
	}
}