using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.Entities
{
    public class ScalingAnomaly : AnomalyMain
    {
        private Vector3 normalScale;
        private Vector3 shiftedScale;
        [SerializeField] private float growTime = 3f;

        private void Awake()
        {
            transform.localScale = normalScale;
        }
        protected override void anomalyChange()
        {
            base.anomalyChange();
            scaling().Forget();
        }

        protected override void resetAnomaly()
        {
            base.resetAnomaly();
            transform.localScale = normalScale;
        }

        private async UniTask scaling()
        {
            float _elapsedTime = 0;
            while (_elapsedTime < growTime)
            {
                transform.localScale = Vector3.Lerp(normalScale, shiftedScale, _elapsedTime / growTime);
                _elapsedTime += Time.deltaTime;
                await UniTask.Yield();
            }
            transform.localScale = shiftedScale;
        }
        
        #region Editor

        public void SetNormalScale ()
        {
            normalScale = transform.localScale;
        }

        public void SetShiftedScale()
        {
            shiftedScale = transform.localScale;
        }

        #endregion
    }
}
