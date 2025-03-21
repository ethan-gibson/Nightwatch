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
		[SerializeField] AudioClip walkingSound;
		[SerializeField] private AudioClip alertedSound;
		[SerializeField] private AudioSource breathingSound;
		private NavMeshAgent agent;
		private Transform target;
		private Collider targetCollider;
		private SkinnedMeshRenderer skinnedMeshRenderer;
		private AudioSource audioSource;
		private Animator animator;
		private bool activated;

		private void Awake()
		{
			agent = GetComponent<NavMeshAgent>();
			agent.speed = speed;
			targetCollider = GetComponent<Collider>();
			audioSource = GetComponent<AudioSource>();
			target = GameObject.FindGameObjectWithTag("Player").transform;
			animator = GetComponent<Animator>();
			skinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
			skinnedMeshRenderer.enabled = false;
			activated = false;
			breathingSound.enabled = false;
		}

		private void Update()
		{
			animator.SetFloat("Velocity", agent.velocity.magnitude);
		}

		private void OnEnable()
		{
			agent.SetDestination(transform.position);
			targetCollider.enabled = false;
			skinnedMeshRenderer.enabled = false;
			transform.position = spawnLocation;
			breathingSound.enabled = true;
		}
		protected override void anomalyChange()
		{
			base.anomalyChange();
			agent.isStopped = false;
			transform.position = spawnLocation;
			agent.SetDestination(transform.position);
			targetCollider.enabled = true;
			activated = true;
			skinnedMeshRenderer.enabled = true;
		}


		protected override void resetAnomaly()
		{
			base.resetAnomaly();
			changed = false;
			activated = true;
			chasePlayer().Forget();
		}

		private async UniTask chasePlayer()
		{
			cts?.Cancel();
			cts?.Dispose();
			cts = new CancellationTokenSource();
			float _elapsedTime = 0;
			if (!audioSource.isPlaying)
			{
				audioSource.PlayOneShot(alertedSound);
				audioSource.clip = walkingSound;
				audioSource.Play();
				audioSource.loop = true;
			}

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
			targetCollider.enabled = false;
			agent.isStopped = true;
			audioSource.Stop();
			skinnedMeshRenderer.enabled = false;
			breathingSound.enabled = false;
		}

		private void OnCollisionEnter(Collision _other)
		{
			if (_other.gameObject.CompareTag("Player") && activated)
			{
				lifeTime = 10f;
				agent.isStopped = true;
				audioSource.Stop();
				_other.gameObject.GetComponent<PlayerMovement>().lockPlayer(lookPoint, 3f);
				animator.SetBool("CaughtPlayer", true);
			}
		}

		public void CallMenuOnAnimEnd()
		{
			target.GetComponent<PlayerMovement>().InvokePlayerDeath();
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

		public void TestSetActive()
		{
			CallChangeAnomaly();
		}

		public void TestReset()
		{
			CallAnomalyReset();
		}

		#endregion
	}
}