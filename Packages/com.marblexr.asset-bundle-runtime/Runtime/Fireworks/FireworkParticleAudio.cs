using UnityEngine;

namespace Marble.AssetBundleRuntime.Fireworks
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class FireworkParticleAudio : MonoBehaviour
    {
        [Header("Lifetime")]
        [SerializeField, Min(0f)]
        private float poolReturnTimer = 1.5f;

        [Header("Explosion")]
        [SerializeField]
        private AudioClip[] audioExplosion = { };

        [SerializeField]
        private GameObject explosionAudioPrefab;

        [SerializeField, Min(0)]
        private int explosionPoolSize;

        [SerializeField, Range(0f, 1f)]
        private float explosionMinVolume = 0.3f;

        [SerializeField, Range(0f, 1f)]
        private float explosionMaxVolume = 0.7f;

        [SerializeField]
        private float explosionPitchMin = 0.75f;

        [SerializeField]
        private float explosionPitchMax = 1.25f;

        [Header("Shot")]
        [SerializeField]
        private AudioClip[] audioShot = { };

        [SerializeField]
        private GameObject shotAudioPrefab;

        [SerializeField, Min(0)]
        private int shotPoolSize;

        [SerializeField, Range(0f, 1f)]
        private float shootMinVolume = 0.05f;

        [SerializeField, Range(0f, 1f)]
        private float shootMaxVolume = 0.1f;

        [SerializeField]
        private float shootPitchMin = 0.75f;

        [SerializeField]
        private float shootPitchMax = 1.25f;
    }
}
