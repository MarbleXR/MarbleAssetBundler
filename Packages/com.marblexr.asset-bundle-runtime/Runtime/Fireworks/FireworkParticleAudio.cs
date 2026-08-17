using System;
using Unity.Profiling;
using UnityEngine;

namespace Marble.AssetBundleRuntime.Fireworks
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class FireworkParticleAudio : MonoBehaviour
    {
        internal const string LateUpdateProfilerMarkerName =
            "Marble.AssetBundleRuntime.Fireworks.FireworkParticleAudio.LateUpdate";

        private static readonly ProfilerMarker LateUpdateProfilerMarker =
            new ProfilerMarker(LateUpdateProfilerMarkerName);

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

        private ParticleSystem cachedParticleSystem;
        private ParticleSystem.Particle[] particleBuffer = Array.Empty<ParticleSystem.Particle>();
        private AudioSource[] explosionSources = Array.Empty<AudioSource>();
        private float[] explosionReleaseTimes = Array.Empty<float>();
        private AudioSource[] shotSources = Array.Empty<AudioSource>();
        private float[] shotReleaseTimes = Array.Empty<float>();
        private int explosionCursor;
        private int shotCursor;
        private bool initialized;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                Initialize();
            }
        }

        private void LateUpdate()
        {
            ProcessFrame(Time.time, Time.deltaTime);
        }

        internal void ProcessFrame(float now, float deltaTime)
        {
            using (LateUpdateProfilerMarker.Auto())
            {
                if (!initialized)
                {
                    Initialize();
                }

                if (cachedParticleSystem == null)
                {
                    return;
                }

                ReleaseExpired(explosionSources, explosionReleaseTimes, now);
                ReleaseExpired(shotSources, shotReleaseTimes, now);
                EnsureParticleCapacity();

                int particleCount = cachedParticleSystem.GetParticles(particleBuffer);
                for (int index = 0; index < particleCount; index++)
                {
                    ParticleSystem.Particle particle = particleBuffer[index];
                    Vector3 worldPosition = GetWorldPosition(particle.position);

                    if (particle.remainingLifetime > 0f && particle.remainingLifetime <= deltaTime)
                    {
                        SpawnExplosion(worldPosition, now);
                    }

                    if (particle.startLifetime > 0f &&
                        particle.remainingLifetime >= particle.startLifetime - deltaTime)
                    {
                        SpawnShot(worldPosition, now);
                    }
                }
            }
        }

        private void OnDisable()
        {
            StopAllSources(explosionSources, explosionReleaseTimes);
            StopAllSources(shotSources, shotReleaseTimes);
        }

        private void OnDestroy()
        {
            StopAllSources(explosionSources, explosionReleaseTimes);
            StopAllSources(shotSources, shotReleaseTimes);
            cachedParticleSystem = null;
            particleBuffer = Array.Empty<ParticleSystem.Particle>();
            explosionSources = Array.Empty<AudioSource>();
            explosionReleaseTimes = Array.Empty<float>();
            shotSources = Array.Empty<AudioSource>();
            shotReleaseTimes = Array.Empty<float>();
            initialized = false;
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            cachedParticleSystem = GetComponent<ParticleSystem>();
            EnsureParticleCapacity();
            CreatePool(
                explosionAudioPrefab,
                explosionPoolSize,
                "Explosion Audio",
                out explosionSources,
                out explosionReleaseTimes);
            CreatePool(
                shotAudioPrefab,
                shotPoolSize,
                "Shot Audio",
                out shotSources,
                out shotReleaseTimes);
            explosionCursor = 0;
            shotCursor = 0;
            initialized = true;
        }

        private void EnsureParticleCapacity()
        {
            if (cachedParticleSystem == null)
            {
                return;
            }

            int requiredCapacity = Mathf.Max(1, cachedParticleSystem.main.maxParticles);
            if (particleBuffer.Length < requiredCapacity)
            {
                particleBuffer = new ParticleSystem.Particle[requiredCapacity];
            }
        }

        private void CreatePool(
            GameObject prefab,
            int requestedSize,
            string itemLabel,
            out AudioSource[] sources,
            out float[] releaseTimes)
        {
            int size = prefab == null ? 0 : Mathf.Max(0, requestedSize);
            sources = size == 0 ? Array.Empty<AudioSource>() : new AudioSource[size];
            releaseTimes = size == 0 ? Array.Empty<float>() : new float[size];

            for (int index = 0; index < size; index++)
            {
                GameObject instance = Instantiate(prefab, transform, false);
                instance.name = $"{prefab.name} ({itemLabel} {index + 1})";
                instance.SetActive(false);
                AudioSource source = instance.GetComponent<AudioSource>();
                if (source == null)
                {
                    Destroy(instance);
                    continue;
                }

                source.playOnAwake = false;
                sources[index] = source;
            }
        }

        private static void ReleaseExpired(AudioSource[] sources, float[] releaseTimes, float now)
        {
            for (int index = 0; index < sources.Length; index++)
            {
                AudioSource source = sources[index];
                if (source == null || !source.gameObject.activeSelf || now < releaseTimes[index])
                {
                    continue;
                }

                source.Stop();
                source.gameObject.SetActive(false);
            }
        }

        private static void StopAllSources(AudioSource[] sources, float[] releaseTimes)
        {
            for (int index = 0; index < sources.Length; index++)
            {
                AudioSource source = sources[index];
                if (source != null)
                {
                    source.Stop();
                    source.gameObject.SetActive(false);
                }

                releaseTimes[index] = 0f;
            }
        }

        private void SpawnExplosion(Vector3 worldPosition, float now)
        {
            Spawn(
                explosionSources,
                explosionReleaseTimes,
                ref explosionCursor,
                audioExplosion,
                explosionMinVolume,
                explosionMaxVolume,
                explosionPitchMin,
                explosionPitchMax,
                worldPosition,
                now);
        }

        private void SpawnShot(Vector3 worldPosition, float now)
        {
            Spawn(
                shotSources,
                shotReleaseTimes,
                ref shotCursor,
                audioShot,
                shootMinVolume,
                shootMaxVolume,
                shootPitchMin,
                shootPitchMax,
                worldPosition,
                now);
        }

        private void Spawn(
            AudioSource[] sources,
            float[] releaseTimes,
            ref int cursor,
            AudioClip[] clips,
            float minimumVolume,
            float maximumVolume,
            float minimumPitch,
            float maximumPitch,
            Vector3 worldPosition,
            float now)
        {
            if (sources.Length == 0 || clips == null || clips.Length == 0)
            {
                return;
            }

            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip == null)
            {
                return;
            }

            for (int attempt = 0; attempt < sources.Length; attempt++)
            {
                int index = cursor;
                cursor = cursor + 1 == sources.Length ? 0 : cursor + 1;
                AudioSource source = sources[index];
                if (source == null)
                {
                    continue;
                }

                source.Stop();
                source.transform.position = worldPosition;
                source.clip = clip;
                source.volume = RandomInRange(minimumVolume, maximumVolume);
                source.pitch = RandomInRange(minimumPitch, maximumPitch);
                source.gameObject.SetActive(true);
                source.Play();
                releaseTimes[index] = now + Mathf.Max(0f, poolReturnTimer);
                return;
            }
        }

        private Vector3 GetWorldPosition(Vector3 particlePosition)
        {
            ParticleSystem.MainModule main = cachedParticleSystem.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return particlePosition;
                case ParticleSystemSimulationSpace.Custom:
                    Transform customSpace = main.customSimulationSpace;
                    return customSpace == null
                        ? cachedParticleSystem.transform.TransformPoint(particlePosition)
                        : customSpace.TransformPoint(particlePosition);
                default:
                    return cachedParticleSystem.transform.TransformPoint(particlePosition);
            }
        }

        private static float RandomInRange(float first, float second)
        {
            float minimum = Mathf.Min(first, second);
            float maximum = Mathf.Max(first, second);
            return Mathf.Approximately(minimum, maximum)
                ? minimum
                : UnityEngine.Random.Range(minimum, maximum);
        }
    }
}
