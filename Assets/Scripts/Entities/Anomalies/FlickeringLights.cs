using System.Collections;
using UnityEngine;

public class FlickeringLights : MonoBehaviour
{
	private Light[] lights;

	private void Awake()
	{
		lights = gameObject.GetComponentsInChildren<Light>();
	}

	private IEnumerator flickerLights(float _flickerTime)
	{
		float _elapsedTime = 0;
		while (_elapsedTime < _flickerTime)
		{
			foreach (var _light in lights) { _light.intensity = Random.Range(0f, 1f); }
			float _delayTimer = Random.Range(0f, 0.5f);
			yield return new WaitForSeconds(_delayTimer);
			_elapsedTime += Time.deltaTime;
		}
		foreach (var _light in lights) { _light.intensity = 0.6f; }
	}

	public void StartFlickering(float _flickerTime)
	{
		StartCoroutine(flickerLights(_flickerTime));
	}
}