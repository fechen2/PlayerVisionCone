using UnityEngine;

namespace Game
{
    [RequireComponent(typeof(TargetVisionTag))]
    public class VisionTargetBehaviour : MonoBehaviour
    {
        private Renderer[] _renderers;
        private TargetVisionTag _visionTag;

        private void OnEnable()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _visionTag = GetComponent<TargetVisionTag>();
        }

        private void Update()
        {
            if (_visionTag == null) return;

            bool hagTag = _visionTag.ShowVision;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i].enabled != hagTag)
                {
                    _renderers[i].enabled = hagTag;
                }
            }
        }
    }
}
