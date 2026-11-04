using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameServer
{
    public class ScreenVignetteManager : MonoBehaviour
    {
        [SerializeField] private Volume postProcessVolume;

        private Vignette vignetteEffect;
        private Coroutine vignetteCoroutine;

        public static ScreenVignetteManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (postProcessVolume == null)
            {
                postProcessVolume = FindAnyObjectByType<Volume>();
            }

            if (postProcessVolume != null && postProcessVolume.profile.TryGet(out vignetteEffect))
            {
                vignetteEffect.active = false;
            }
        }

        private void Reset()
        {
            if (postProcessVolume == null)
            {
                postProcessVolume = FindAnyObjectByType<Volume>();
            }
        }

        public void PlayHitEffect(Color targetColor, float maxIntensity = 0.45f, float duration = 0.3f)
        {
            if (vignetteEffect == null) return;

            if (vignetteCoroutine != null)
            {
                StopCoroutine(vignetteCoroutine);
            }

            vignetteCoroutine = StartCoroutine(HitVignetteRoutine(targetColor, maxIntensity, duration));
        }

        private IEnumerator HitVignetteRoutine(Color targetColor, float maxIntensity, float duration)
        {
            vignetteEffect.active = true;

            vignetteEffect.color.overrideState = true;
            vignetteEffect.color.value = targetColor;

            vignetteEffect.intensity.overrideState = true;

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                vignetteEffect.intensity.value = Mathf.Lerp(maxIntensity, 0f, t);
                yield return null;
            }

            vignetteEffect.intensity.value = 0f;
            vignetteEffect.active = false;
        }
    }
}