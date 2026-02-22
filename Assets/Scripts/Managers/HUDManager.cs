using Arti.Utilities;
using UnityEngine;
using TMPro;

namespace Game.Manager
{
	public class HUDManager : InstanceFactory<HUDManager>
	{
		[SerializeField] private TextMeshProUGUI hourText;
		[SerializeField] private TextMeshProUGUI interactText;
		[SerializeField] private TextMeshProUGUI reportText;

		public void SetInteractionText(string _text)
		{
			interactText.text = _text;
		}

		public void SetReportText(string _text, Color _color)
		{
			reportText.text = _text;
			reportText.color = _color;
		}

		public void UpdateGameTime(string _time)
		{
			hourText.text = _time+":00 am";
		}
	}
}