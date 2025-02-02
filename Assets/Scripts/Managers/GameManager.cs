using System;
using System.Collections;
using System.Collections.Generic;
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
		private bool playerWarned;
		private HUDManager hudManager;
		[SerializeField] private AudioSource anomalyWarningSound;
		[SerializeField] private List<AnomalyMain> anomalies;
		[SerializeField] private float gameLenght = 500;
		[SerializeField] private float anomalyCooldown = 40f;
		[SerializeField] private float anomalyCooldownReduction = 4f;
		private float hour = 0;
		private Transform player;
		private PlayerPhone playerPhone;
		private CancellationTokenSource cts;

		private void Start()
		{
			if (Instance == null) { Instance = this; }
			else { Destroy(gameObject); }
			hudManager = GetComponent<HUDManager>();
			cts = new CancellationTokenSource();
			foreach (var _anomaly in anomalies)
			{
				_anomaly.AnomalySpawnedEvent += increaseAnomalyCount;
				var _temp = _anomaly.gameObject.GetComponent<HuntingAnomaly>();//HuntingAnomaly can kill player, so it gets special delegate
				if (_temp != null) { _anomaly.gameObject.GetComponent<HuntingAnomaly>().caughtPlayer += Callmenu; }
			}
			player = GameObject.FindGameObjectWithTag("Player").transform;
			playerPhone = player.GetComponentInChildren<PlayerPhone>();
			playerPhone.Report += reportCheck;
			countDown().Forget();
			}

		private void OnDestroy()
		{
			foreach (var _anomaly in anomalies) { _anomaly.AnomalySpawnedEvent -= increaseAnomalyCount; }
			playerPhone.Report -= reportCheck;
			if (cts == null) { return; }
			cts?.Cancel();
			cts?.Dispose();
			cts = null;
		}

		private void increaseAnomalyCount(int _weight)
		{
			anomalyCounter += _weight;
			if (anomalyCounter >= maxAnomalyWeight) { bringUpMenu(); }
			if (anomalyCounter == anomalyWarningAmount && !playerWarned) { anomalyWarningSound.Play(); }
		}

		public void Callmenu()
		{
			bringUpMenu();
		}

		private void bringUpMenu(string _text = "Game Over")
		{
			Time.timeScale = 0;
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
			inGameMenu.gameObject.SetActive(true);
			inGameMenu.SetGameOverText(_text);
		}

		private void reportCheck(bool _check)
		{
			StartCoroutine(setReportText(_check));
		}

		private IEnumerator setReportText(bool _check)
		{
			if (_check)
			{
				hudManager.SetReportText("Anomalies Reported", Color.green);
			}
			else
			{
				hudManager.SetReportText("No Anomalies Found", Color.red);
				StartCoroutine(anomalyBoost());
			}
			yield return new WaitForSeconds(3f);
			hudManager.SetReportText("", Color.black);//color doesnt matter here
		}

		private IEnumerator anomalyBoost()
		{
			anomalyCooldown -= 15;
			yield return new WaitForSeconds(30f);
			anomalyCooldown += 15;
		}

		private async UniTask countDown()
		{
			float waitTime = gameLenght / 6;
			try
			{
				while (waitTime >= 0)
				{
					waitTime -= Time.deltaTime;
					await UniTask.Yield(cancellationToken: cts.Token);
				}
				anomalyCooldown -= anomalyCooldownReduction;
				hour++;
				if (hour >= 6) { bringUpMenu("Anomalies Defeated"); }
				hudManager.UpdateGameTime(hour.ToString());
				countDown().Forget();
			}
			catch (OperationCanceledException) { }
		}

		private async UniTask anomalyTrigger()
		{
			float waitTime = anomalyCooldown;
			try
			{
				while (waitTime >= 0)
				{
					gameLenght -= Time.deltaTime;
					await UniTask.Yield(cancellationToken: cts.Token);
				}
				getAnomalyToTrigger();
			}
			catch (OperationCanceledException) { }
		}

		private void getAnomalyToTrigger()
		{
			int x = Random.Range(0, anomalies.Count);
			if (anomalies[x].IsVisible())
			{
				//if player can potentially see it, trigger a differant anomaly
				getAnomalyToTrigger();
				return;
			}
			Debug.Log(anomalies[x].name + " was activated");
			anomalies[x].CallChangeAnomaly();
			anomalyTrigger().Forget();
		}
	}
}