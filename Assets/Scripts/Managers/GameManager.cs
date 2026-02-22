using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using Arti.Utilities;
using Cysharp.Threading.Tasks;
using Game.Entities;
using UnityEngine;
using Unity.Behavior;
using UnityEngine.Serialization;
using Game.UI;
using Random = UnityEngine.Random;
using Logger = Arti.Utilities.Logger;

namespace Game.Manager
{
	public class GameManager : InstanceFactory<GameManager>
	{
		private static readonly int staticCoverageShaderId = Shader.PropertyToID("_staticCoverage");
		private const string stalkerEnterExitTag = "StalkerEnterExit";
		private const string anomalyTag = "Anomaly";
		private const string stalkerNameToken = "Stalker";
		private const string playerTag = "Player";
		private const float stalkerScanInterval = 0.5f;
		private static readonly WaitForSeconds statusTextDuration = new(3f);

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
		[FormerlySerializedAs("gameLenght")]
		private float gameLength = 360f;
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
		private float nextStalkerScanTime;

		private HUDManager hudManager;
		private Transform player;
		private PlayerMovement playerMovement;
		private PlayerPhone playerPhone;
		private GameObject activeStalkerInstance;
		private GameObject[] stalkerEntryExitPoints;

		private CancellationTokenSource countdownCts;
		private CancellationTokenSource anomalyTriggerCts;
		private CancellationTokenSource stalkerSpawnerCts;

		protected override void Awake()
		{
			base.Awake();
			if (Instance != this) { return; }

			hudManager = GetComponent<HUDManager>();
			gameLength = Mathf.Max(30f, gameLength);
			baseAnomalyCooldown = Mathf.Max(2f, anomalyCooldown);
			baseStalkerCooldown = Mathf.Max(10f, stalkerCooldown);
			anomalyCooldown = baseAnomalyCooldown;
			stalkerCooldown = baseStalkerCooldown;
		}

		private void Start()
		{
			stalkerEntryExitPoints = GameObject.FindGameObjectsWithTag(stalkerEnterExitTag);
			anomalies = FindObjectsByType<AnomalyMain>(FindObjectsSortMode.None);
			foreach (AnomalyMain _anomaly in anomalies)
			{
				if (_anomaly) { _anomaly.AnomalySpawnedEvent += increaseAnomalyCount; }
			}

			player = GameObject.FindGameObjectWithTag(playerTag)?.transform;
			if (player)
			{
				playerMovement = player.GetComponent<PlayerMovement>();
				if (playerMovement) { playerMovement.OnPlayerKilled += CallMenu; }

				playerPhone = player.GetComponentInChildren<PlayerPhone>();
				if (playerPhone) { playerPhone.Report += reportCheck; }
			}

			ensureExistingStalkerGraphSetup();
			hudManager?.UpdateGameTime(hour.ToString(CultureInfo.InvariantCulture));
			refreshDirectorRates();
			runCountdownLoop().Forget();
			runAnomalyLoop().Forget();
			runStalkerLoop().Forget();
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
			countdownCts.Reset();
			anomalyTriggerCts.Reset();
			stalkerSpawnerCts.Reset();
			countdownCts = null;
			anomalyTriggerCts = null;
			stalkerSpawnerCts = null;
		}

		private void increaseAnomalyCount(int _weight)
		{
			if (gameEnded) { return; }

			int scoreCap = Mathf.Max(1, maxAnomalyWeight);
			anomalyCounter = Mathf.Clamp(anomalyCounter + _weight, 0, scoreCap);

			if (staticMaterial) { staticMaterial.SetFloat(staticCoverageShaderId, (float)anomalyCounter / scoreCap); }

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
			yield return statusTextDuration;
			hudManager.SetReportText("", Color.black);
		}

		private IEnumerator setReportText(bool _check)
		{
			if (hudManager == null) { yield break; }

			if (_check) { hudManager.SetReportText("Anomalies Reported", Color.green); }
			else { hudManager.SetReportText("No Anomalies Found", Color.red); }

			yield return statusTextDuration;
			hudManager.SetReportText("", Color.black);
		}

		private async UniTask runCountdownLoop()
		{
			CancellationToken cancellationToken = resetToken(ref countdownCts).Token;
			float hourLength = Mathf.Max(1f, gameLength / 6f);
			float nextHourTimestamp = hourLength;

			try
			{
				while (!gameEnded)
				{
					float _deltaTime = Time.deltaTime;
					elapsedGameTime += _deltaTime;
					reportPressure = Mathf.Max(0f, reportPressure - (Mathf.Max(0f, reportPressureDecayPerSecond) * _deltaTime));
					refreshDirectorRates();

					if (elapsedGameTime >= nextHourTimestamp && hour < 6f)
					{
						hour += 1f;
						hudManager?.UpdateGameTime(hour.ToString(CultureInfo.InvariantCulture));
						nextHourTimestamp += hourLength;
					}

					if (elapsedGameTime >= gameLength)
					{
						bringUpMenu("Anomalies Defeated");
						return;
					}

					await UniTask.Yield(cancellationToken: cancellationToken);
				}
			}
			catch (OperationCanceledException) { }
		}

		private async UniTask runAnomalyLoop()
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

		private async UniTask runStalkerLoop()
		{
			CancellationToken cancellationToken = resetToken(ref stalkerSpawnerCts).Token;

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
					spawnStalker();
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
			spawnStalker();
		}

		private bool hasActiveStalker()
		{
			if (activeStalkerInstance) { return true; }
			if (Time.time < nextStalkerScanTime) { return false; }
			nextStalkerScanTime = Time.time + stalkerScanInterval;

			GameObject[] activeAnomalies = GameObject.FindGameObjectsWithTag(anomalyTag);
			foreach (GameObject anomalyObject in activeAnomalies)
			{
				if (anomalyObject == null) { continue; }
				if (!anomalyObject.name.Contains(stalkerNameToken, StringComparison.OrdinalIgnoreCase)) { continue; }
				if (!anomalyObject.TryGetComponent<BehaviorGraphAgent>(out _)) { continue; }

				activeStalkerInstance = anomalyObject;
				return true;
			}

			return false;
		}

		private bool spawnStalker()
		{
			if (gameEnded) { return false; }
			if (stalkerPrefab == null)
			{
				Logger.LogError("Stalker prefab is not assigned!");
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
			configureSpawnedStalker(spawnedStalker);
			activeStalkerInstance = spawnedStalker;
			lastStalkerSpawnTime = Time.time;
			reportPressure = Mathf.Max(0f, reportPressure - 0.12f);
			flickeringLights?.StartFlickering(stalkerFlickerDuration);
			return true;
		}

		private static void configureSpawnedStalker(GameObject spawnedStalker)
		{
			if (spawnedStalker == null) { return; }

			if (!spawnedStalker.TryGetComponent<BehaviorGraphAgent>(out _)) { Logger.LogError("Spawned stalker is missing BehaviorGraphAgent and cannot run stalker behavior."); }

			if (!spawnedStalker.TryGetComponent<StalkerAnimationEvents>(out _))
			{
				spawnedStalker.AddComponent<StalkerAnimationEvents>();
				Logger.LogWarning("Spawned stalker was missing StalkerAnimationEvents. Added animation event receiver automatically.");
			}
		}

		private static void ensureExistingStalkerGraphSetup()
		{
			GameObject[] activeAnomalies = GameObject.FindGameObjectsWithTag(anomalyTag);
			foreach (GameObject anomalyObject in activeAnomalies)
			{
				if (anomalyObject == null) { continue; }
				if (!anomalyObject.name.Contains(stalkerNameToken, StringComparison.OrdinalIgnoreCase)) { continue; }
				if (!anomalyObject.TryGetComponent<BehaviorGraphAgent>(out _)) { continue; }

				configureSpawnedStalker(anomalyObject);
			}
		}

		private void refreshDirectorRates()
		{
			float pressure = getDirectorPressure01();

			float derivedAnomalyFloor = Mathf.Max(2f, baseAnomalyCooldown - (Mathf.Max(0f, anomalyCooldownReduction) * 6f));
			float anomalyFloor = Mathf.Max(2f, Mathf.Min(minimumAnomalyCooldown, derivedAnomalyFloor));
			anomalyFloor = Mathf.Clamp(anomalyFloor, 2f, baseAnomalyCooldown);

			float stalkerFloor = Mathf.Clamp(Mathf.Max(10f, minimumStalkerCooldown), 10f, baseStalkerCooldown);

			anomalyCooldown = Mathf.Lerp(baseAnomalyCooldown, anomalyFloor, pressure);
			stalkerCooldown = Mathf.Lerp(baseStalkerCooldown, stalkerFloor, pressure);
		}

		private float getDirectorPressure01()
		{
			float gameProgress = Mathf.Clamp01(elapsedGameTime / Mathf.Max(1f, gameLength));
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