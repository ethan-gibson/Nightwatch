using System.Collections;
using Game.Entities;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public enum EnemyState
{
	Patrolling,
	Chasing,
	Searching,
	Leaving
}

public class EnemyAI : MonoBehaviour
{
	[SerializeField] private float sightRange = 10f;
	[SerializeField] private float patrolSpeed = 2f;
	[SerializeField] private float chaseSpeed = 5f;
	[SerializeField] private float searchDuration = 5f;
	[SerializeField] private float visionWidth = 45f;
	[SerializeField] private float patrolRadius = 10f;
	[SerializeField] private float searchRadius = 5f;
	[SerializeField] private float searchDelay = 2f;
	[SerializeField] private float lifeTime = 20f;
	[SerializeField] private Transform highLookPoint;
	[SerializeField] private Transform lowLookPoint;
	[SerializeField] private AudioClip alertedNoise;
	[SerializeField] private AudioClip searchStarted;
	[SerializeField] private AudioClip searchEnded;

	private NavMeshAgent agent;
	private Transform player;
	private PlayerMovement playerMovment;
	private Vector3 lastKnownPlayerPosition;
	private EnemyState currentState;
	private float searchTimer = 0f;
	private float delayTimer = 0f;
	private bool playerWasSeenHiding; //in case the stalker happens to get close to the player without seeing him
	private float timeActive;
	private bool isLeaving;
	private GameObject[] exitPoints;
	private Animator animator;
	private bool canMove;
	private float currentFOV;
	private AudioSource audioSource;
	private AudioSource walkingAudioSource;

	private void Awake()
	{
		agent = GetComponent<NavMeshAgent>();
		currentState = EnemyState.Patrolling; // Start in the patrolling state
		player = GameObject.FindGameObjectWithTag("Player").transform;
		playerMovment = player.GetComponent<PlayerMovement>();
		exitPoints = GameObject.FindGameObjectsWithTag("StalkerEnterExit");
		animator = GetComponent<Animator>();
		audioSource = GetComponent<AudioSource>();
		walkingAudioSource = lowLookPoint.GetComponent<AudioSource>();
	}

	private void Update()
	{
		animator.SetFloat("Velocity", agent.velocity.magnitude);
		if (isLeaving && agent.remainingDistance <= agent.stoppingDistance)
		{
			agent.speed = 0;
			animator.SetBool("IsLeaving", true);
		}
		if (!isLeaving)
		{
			timeActive += Time.deltaTime;
			if (timeActive >= lifeTime)
			{
				isLeaving = true;
				leaveMap();
				return;
			}
			switch (currentState)
			{
				case EnemyState.Patrolling:
					patrol();
					checkForPlayer();
					break;

				case EnemyState.Chasing:
					chasePlayer();
					if (!canSeePlayer()) { transitionToState(EnemyState.Searching); }
					break;

				case EnemyState.Searching:
					searchForPlayer();
					break;
			}
		}
	}

	private void LateUpdate()
	{
		if (agent.velocity.magnitude > 0.1f)
		{
			if (!walkingAudioSource.isPlaying) { walkingAudioSource.Play(); }
		}
		else
		{
			if (walkingAudioSource.isPlaying) { walkingAudioSource.Stop(); }
		}
	}

	private void transitionToState(EnemyState _newState)
	{
		currentState = _newState;
		delayTimer = 0f; // Reset the delay timer

		switch (_newState)
		{
			case EnemyState.Patrolling:
				agent.speed = patrolSpeed;
				patrol();
				audioSource.PlayOneShot(searchEnded);
				break;

			case EnemyState.Chasing:
				agent.speed = chaseSpeed;
				audioSource.PlayOneShot(alertedNoise);
				break;

			case EnemyState.Searching:
				searchTimer = 0f;
				agent.SetDestination(lastKnownPlayerPosition);
				audioSource.PlayOneShot(searchStarted);
				break;
		}
	}

	private void patrol()
	{
		if (agent.remainingDistance <= agent.stoppingDistance)
		{
			// Start the delay timer
			delayTimer += Time.deltaTime;

			if (delayTimer >= searchDelay)
			{
				delayTimer = 0f;
				Vector3 _randomDirection = Random.insideUnitSphere * patrolRadius;
				_randomDirection += transform.position;
				NavMeshHit _hit;
				NavMesh.SamplePosition(_randomDirection, out _hit, patrolRadius, NavMesh.AllAreas);
				agent.SetDestination(_hit.position);
			}
		}
	}

	private void checkForPlayer()
	{
		if (!playerMovment.CheckIfHiding()) { playerWasSeenHiding = false; }
		if (canSeePlayer())
		{
			lastKnownPlayerPosition = player.gameObject.transform.position;
			transitionToState(EnemyState.Chasing);
		}
	}

	private void chasePlayer()
	{
		lastKnownPlayerPosition = player.gameObject.transform.position;
		agent.SetDestination(player.gameObject.transform.position);
	}

	private void searchForPlayer()
	{
		if (agent.remainingDistance <= agent.stoppingDistance)
		{
			if (playerWasSeenHiding && playerMovment.CheckIfHiding())
			{
				if (playerMovment.CheckIfInCloset()) { killPlayer(highLookPoint, 1); }
				if (playerMovment.CheckIfUnderBed()) { killPlayer(lowLookPoint, 2); }
				return;
			}
			delayTimer += Time.deltaTime;

			if (delayTimer >= searchDelay)
			{
				delayTimer = 0f;
				searchTimer += Time.deltaTime;


				if (searchTimer >= searchDuration) { transitionToState(EnemyState.Patrolling); }
				else
				{
					// Optionally, look around or move to a new nearby position
					Vector3 _randomDirection = Random.insideUnitSphere * searchRadius;
					_randomDirection += lastKnownPlayerPosition;
					NavMeshHit hit;
					NavMesh.SamplePosition(_randomDirection, out hit, searchRadius, NavMesh.AllAreas);
					agent.SetDestination(hit.position);
				}
			}
		}
	}

	private void leaveMap()
	{
		Transform _closestExit = null;
		float _closestDistance = Mathf.Infinity;

		foreach (GameObject _exit in exitPoints)
		{
			Debug.Log("Check Path");
			float _dist = Vector3.Distance(transform.position, _exit.transform.position);
			if (_dist < _closestDistance)
			{
				Debug.Log(_dist);
				_closestDistance = _dist;
				_closestExit = _exit.transform;
			}
		}
		if (_closestExit) { agent.SetDestination(_closestExit.position); }

		else { Debug.LogError("No Valid Path"); }
	}

	private bool canSeePlayer()
	{
		Vector3 _directionToPlayer = player.position - transform.position;
		float _angleToPlayer = Vector3.Angle(transform.forward, _directionToPlayer);
		if (_angleToPlayer <= currentFOV)
		{
			if (_directionToPlayer.magnitude <= sightRange)
			{
				RaycastHit _raycastHit;
				if (Physics.Raycast(transform.position, _directionToPlayer.normalized, out _raycastHit, sightRange))
				{
					if (_raycastHit.transform == player)
					{
						playerWasSeenHiding = true;
						return true;
					}
				}
			}
		}
		return false;
	}

	public void OnAnimationCompleted()
	{
		if (isLeaving) { Destroy(gameObject); }
		agent.speed = patrolSpeed;
		currentFOV = visionWidth;
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (collision.gameObject.CompareTag("Player")) { killPlayer(highLookPoint, 0); }
	}

	private void killPlayer(Transform _lookPoint, int _killType)
	{
		//0 for front, 1 for closet, 2 for bed
		if (_killType > 0)
		{
			GetComponent<Collider>().enabled = false;
			GetComponent<Rigidbody>().isKinematic = true;
		}
		agent.velocity = Vector3.zero;
		agent.speed = 0f;
		agent.isStopped = true;
		transform.LookAt(player);
		Vector3 _eulerAngles = transform.eulerAngles;
		_eulerAngles.x = 0;
		_eulerAngles.z = 0;
		transform.eulerAngles = _eulerAngles;
		animator.SetBool("IsKilling", true);
		animator.SetInteger("KillType", _killType);
		playerMovment.lockPlayer(_lookPoint, 4f);
	}

	private void CallMenuOnAnimEnd()
	{
		playerMovment.InvokePlayerDeath();
	}

	private void OnDrawGizmos()
	{
		// Draw the FOV cone
		float halfFOV = 45f / 2f;
		Quaternion leftRayRotation = Quaternion.AngleAxis(-halfFOV, Vector3.up);
		Quaternion rightRayRotation = Quaternion.AngleAxis(halfFOV, Vector3.up);
		Vector3 leftRayDirection = leftRayRotation * transform.forward;
		Vector3 rightRayDirection = rightRayRotation * transform.forward;

		Gizmos.color = Color.yellow;
		Gizmos.DrawRay(transform.position + Vector3.up, leftRayDirection * sightRange);
		Gizmos.DrawRay(transform.position + Vector3.up, rightRayDirection * sightRange);
	}
}