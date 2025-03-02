using UnityEngine;

namespace Game.Entities
{
    public class DuplicatedAnomalyControl : AnomalyMain
    {
        private GameObject originalObject;
        private DuplicatingAnomaly duplicatingAnomaly;

        public void OnCreate(GameObject _original)
        {
            originalObject = _original;
            duplicatingAnomaly = _original.GetComponent<DuplicatingAnomaly>();
            if(!duplicatingAnomaly){Debug.LogError("No Script On Original Object");}
        }

        protected override void anomalyChange()
        {
            changed = true;
            Debug.Log("copy changes is: " + changed);
        }

        protected override void resetAnomaly()
        {
            Debug.Log(("Copy Reported"));
            duplicatingAnomaly.CopyReported();
        }
    }
}
