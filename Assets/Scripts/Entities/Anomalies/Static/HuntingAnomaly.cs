using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Entities
{
	public class HuntingAnomaly : AnomalyMain
	{
		[Tooltip("How long will it chase for")] [SerializeField]
		private float lifeTime;

		[SerializeField] private float speed;
		[SerializeField] private Vector3 spawnLocation;
		[SerializeField] private Transform lookPoint;
		private NavMeshAgent agent;
		private Transform target;
		private Collider targetCollider;
		private SkinnedMeshRenderer skinnedMeshRenderer;
		private AudioSource audioSource;

		public delegate void CaughtPlayer();

		public event CaughtPlayer caughtPlayer;

		private void Awake()
		{
			agent = GetComponent<NavMeshAgent>();
			agent.speed = speed;
			targetCollider = GetComponent<Collider>();
			audioSource = GetComponent<AudioSource>();
			target = GameObject.FindGameObjectWithTag("Player").transform;
			//skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
		}

		private void OnEnable()
		{
			agent.SetDestination(transform.position);
			targetCollider.enabled = false;
			//skinnedMeshRenderer.enabled = false;
			//anim.idle
		}

		protected override void anomalyChange()
		{
			base.anomalyChange();
			agent.SetDestination(transform.position);
			targetCollider.enabled = true;
			//skinnedMeshRenderer.enabled = true;
		}

		protected override void resetAnomaly()
		{
			base.anomalyChange();
			changed = false;

			chasePlayer().Forget();
		}

		private async UniTask chasePlayer()
		{
			cts?.Cancel();
			cts?.Dispose();
			cts = new CancellationTokenSource();
			float _elapsedTime = 0;

			transform.LookAt(target.position, Vector3.up);
			try
			{
				while (_elapsedTime < lifeTime)
				{
					agent.SetDestination(target.position);
					_elapsedTime += Time.deltaTime;
					await UniTask.Yield(cancellationToken: cts.Token);
				}
			}
			catch (OperationCanceledException) { }
			//anim.chase
			if (!targetCollider) { return; }
			targetCollider.enabled = false;
			agent.isStopped = true;
			audioSource.Stop();
			//skinnedMeshRenderer.enabled = false;
		}

		private void OnCollisionEnter(Collision _other)
		{
			if (_other.gameObject.CompareTag("Player"))
			{
				agent.isStopped = true;
				audioSource.Stop();
				caughtPlayer?.Invoke();
				_other.gameObject.GetComponent<PlayerMovement>().lockPlayer(lookPoint, 3f);//will be 3f till anims added
				//anim.playkillanim
			}
		}

		private void OnDestroy()
		{
			Debug.Log("destroyed");
			if (cts == null) { return; }
			cts?.Cancel();
			cts?.Dispose();
			cts = null;
		}

		#region Editor

		public void SetSpawnLocation()
		{
			spawnLocation = transform.position;
		}

		#endregion
	}
}