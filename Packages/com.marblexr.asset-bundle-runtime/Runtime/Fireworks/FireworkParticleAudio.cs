using System;
using Unity.Profiling;
using UnityEngine;

namespace Marble.AssetBundleRuntime.Fireworks
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class FireworkParticleAudio : MonoBehaviour
    {
        private const byte ShotPlayedFlag = 1;
        private const byte ExplosionPlayedFlag = 2;

        internal const string LateUpdateProfilerMarkerName =
            "Marble.AssetBundleRuntime.Fireworks.FireworkParticleAudio.LateUpdate";

        private readonly ProfilerMarker lateUpdateProfilerMarker =
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
        private uint[] particleStateSeeds = Array.Empty<uint>();
        private byte[] particleStateFlags = Array.Empty<byte>();
        private bool[] particleStatesSeen = Array.Empty<bool>();
        private float[] particleStateStartLifetimes = Array.Empty<float>();
        private float[] particleStateRemainingLifetimes = Array.Empty<float>();
        private Vector3[] particleStateWorldPositions = Array.Empty<Vector3>();
        private AudioSource[] explosionSources = Array.Empty<AudioSource>();
        private float[] explosionReleaseTimes = Array.Empty<float>();
        private AudioSource[] shotSources = Array.Empty<AudioSource>();
        private float[] shotReleaseTimes = Array.Empty<float>();
        private GameObject explosionPoolRoot;
        private GameObject shotPoolRoot;
        private int particleStateCount;
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
            if (!initialized)
            {
                Initialize();
            }

            ProcessFrame(Time.unscaledTime, GetParticleDeltaTime());
        }

        internal float GetParticleDeltaTime()
        {
            if (cachedParticleSystem == null || cachedParticleSystem.isPaused)
            {
                return 0f;
            }

            ParticleSystem.MainModule main = cachedParticleSystem.main;
            float frameDeltaTime = main.useUnscaledTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            return Mathf.Max(0f, frameDeltaTime * main.simulationSpeed);
        }

        internal void ProcessFrame(float now, float deltaTime)
        {
            using (lateUpdateProfilerMarker.Auto())
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
                if (deltaTime <= 0f)
                {
                    return;
                }

                EnsureParticleCapacity();

                int particleCount = cachedParticleSystem.GetParticles(particleBuffer);
                ClearParticleStateSeenFlags();
                for (int index = 0; index < particleCount; index++)
                {
                    ParticleSystem.Particle particle = particleBuffer[index];
                    int stateIndex = GetOrAddParticleState(particle);
                    particleStatesSeen[stateIndex] = true;
                    byte stateFlags = particleStateFlags[stateIndex];
                    bool playExplosion =
                        (stateFlags & ExplosionPlayedFlag) == 0 &&
                        particle.remainingLifetime > 0f &&
                        particle.remainingLifetime <= deltaTime;
                    bool playShot =
                        (stateFlags & ShotPlayedFlag) == 0 &&
                        particle.startLifetime > 0f &&
                        particle.remainingLifetime >= particle.startLifetime - deltaTime;

                    Vector3 worldPosition = GetWorldPosition(particle.position);
                    particleStateWorldPositions[stateIndex] = worldPosition;
                    particleStateStartLifetimes[stateIndex] = particle.startLifetime;
                    particleStateRemainingLifetimes[stateIndex] = particle.remainingLifetime;
                    if (playExplosion || playShot)
                    {
                        if (playExplosion)
                        {
                            SpawnExplosion(worldPosition, now);
                            stateFlags |= ExplosionPlayedFlag;
                        }

                        if (playShot)
                        {
                            SpawnShot(worldPosition, now);
                            stateFlags |= ShotPlayedFlag;
                        }
                    }

                    particleStateFlags[stateIndex] = stateFlags;
                }

                RemoveMissingParticleStates(now, deltaTime);
            }
        }

        private void OnDisable()
        {
            StopAllSources(explosionSources, explosionReleaseTimes);
            StopAllSources(shotSources, shotReleaseTimes);
            particleStateCount = 0;
        }

        private void OnDestroy()
        {
            StopAllSources(explosionSources, explosionReleaseTimes);
            StopAllSources(shotSources, shotReleaseTimes);
            DestroyPoolRoot(ref explosionPoolRoot);
            DestroyPoolRoot(ref shotPoolRoot);
            cachedParticleSystem = null;
            particleBuffer = Array.Empty<ParticleSystem.Particle>();
            particleStateSeeds = Array.Empty<uint>();
            particleStateFlags = Array.Empty<byte>();
            particleStatesSeen = Array.Empty<bool>();
            particleStateStartLifetimes = Array.Empty<float>();
            particleStateRemainingLifetimes = Array.Empty<float>();
            particleStateWorldPositions = Array.Empty<Vector3>();
            explosionSources = Array.Empty<AudioSource>();
            explosionReleaseTimes = Array.Empty<float>();
            shotSources = Array.Empty<AudioSource>();
            shotReleaseTimes = Array.Empty<float>();
            particleStateCount = 0;
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
                out explosionReleaseTimes,
                out explosionPoolRoot);
            CreatePool(
                shotAudioPrefab,
                shotPoolSize,
                "Shot Audio",
                out shotSources,
                out shotReleaseTimes,
                out shotPoolRoot);
            particleStateCount = 0;
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

            int requiredStateCapacity = requiredCapacity * 2;
            if (particleStateSeeds.Length < requiredStateCapacity)
            {
                uint[] expandedSeeds = new uint[requiredStateCapacity];
                byte[] expandedFlags = new byte[requiredStateCapacity];
                bool[] expandedSeen = new bool[requiredStateCapacity];
                float[] expandedStartLifetimes = new float[requiredStateCapacity];
                float[] expandedRemainingLifetimes = new float[requiredStateCapacity];
                Vector3[] expandedWorldPositions = new Vector3[requiredStateCapacity];
                Array.Copy(particleStateSeeds, expandedSeeds, particleStateCount);
                Array.Copy(particleStateFlags, expandedFlags, particleStateCount);
                Array.Copy(particleStatesSeen, expandedSeen, particleStateCount);
                Array.Copy(
                    particleStateStartLifetimes,
                    expandedStartLifetimes,
                    particleStateCount);
                Array.Copy(
                    particleStateRemainingLifetimes,
                    expandedRemainingLifetimes,
                    particleStateCount);
                Array.Copy(
                    particleStateWorldPositions,
                    expandedWorldPositions,
                    particleStateCount);
                particleStateSeeds = expandedSeeds;
                particleStateFlags = expandedFlags;
                particleStatesSeen = expandedSeen;
                particleStateStartLifetimes = expandedStartLifetimes;
                particleStateRemainingLifetimes = expandedRemainingLifetimes;
                particleStateWorldPositions = expandedWorldPositions;
            }
        }

        private void CreatePool(
            GameObject prefab,
            int requestedSize,
            string itemLabel,
            out AudioSource[] sources,
            out float[] releaseTimes,
            out GameObject poolRoot)
        {
            int size = prefab == null || prefab.GetComponent<AudioSource>() == null
                ? 0
                : Mathf.Max(0, requestedSize);
            sources = size == 0 ? Array.Empty<AudioSource>() : new AudioSource[size];
            releaseTimes = size == 0 ? Array.Empty<float>() : new float[size];
            poolRoot = null;
            if (size == 0)
            {
                return;
            }

            poolRoot = new GameObject($"{itemLabel} Pool");
            poolRoot.SetActive(false);
            poolRoot.transform.SetParent(transform, false);

            for (int index = 0; index < size; index++)
            {
                GameObject instance = Instantiate(prefab, poolRoot.transform, false);
                instance.name = $"{prefab.name} ({itemLabel} {index + 1})";
                instance.SetActive(false);
                AudioSource source = instance.GetComponent<AudioSource>();
                source.playOnAwake = false;
                sources[index] = source;
            }

            poolRoot.SetActive(true);
        }

        private void DestroyPoolRoot(ref GameObject poolRoot)
        {
            if (poolRoot != null)
            {
                Destroy(poolRoot);
                poolRoot = null;
            }
        }

        private void ClearParticleStateSeenFlags()
        {
            for (int index = 0; index < particleStateCount; index++)
            {
                particleStatesSeen[index] = false;
            }
        }

        private int GetOrAddParticleState(ParticleSystem.Particle particle)
        {
            int closestStateIndex = -1;
            float smallestLifetimeDecrease = float.MaxValue;
            for (int index = 0; index < particleStateCount; index++)
            {
                if (particleStateSeeds[index] == particle.randomSeed &&
                    !particleStatesSeen[index] &&
                    Mathf.Approximately(
                        particle.startLifetime,
                        particleStateStartLifetimes[index]))
                {
                    float lifetimeDecrease =
                        particleStateRemainingLifetimes[index] - particle.remainingLifetime;
                    if (lifetimeDecrease >= -0.0001f &&
                        lifetimeDecrease < smallestLifetimeDecrease)
                    {
                        closestStateIndex = index;
                        smallestLifetimeDecrease = lifetimeDecrease;
                    }
                }
            }

            if (closestStateIndex >= 0)
            {
                return closestStateIndex;
            }

            int newStateIndex = particleStateCount;
            particleStateCount++;
            particleStateSeeds[newStateIndex] = particle.randomSeed;
            particleStateFlags[newStateIndex] = 0;
            return newStateIndex;
        }

        private void RemoveMissingParticleStates(float now, float deltaTime)
        {
            int nextStateIndex = 0;
            for (int currentStateIndex = 0;
                 currentStateIndex < particleStateCount;
                 currentStateIndex++)
            {
                if (!particleStatesSeen[currentStateIndex])
                {
                    if ((particleStateFlags[currentStateIndex] & ExplosionPlayedFlag) == 0 &&
                        particleStateRemainingLifetimes[currentStateIndex] > 0f &&
                        particleStateRemainingLifetimes[currentStateIndex] <=
                        deltaTime + 0.0001f)
                    {
                        SpawnExplosion(particleStateWorldPositions[currentStateIndex], now);
                    }

                    continue;
                }

                if (nextStateIndex != currentStateIndex)
                {
                    particleStateSeeds[nextStateIndex] = particleStateSeeds[currentStateIndex];
                    particleStateFlags[nextStateIndex] = particleStateFlags[currentStateIndex];
                    particleStatesSeen[nextStateIndex] = true;
                    particleStateStartLifetimes[nextStateIndex] =
                        particleStateStartLifetimes[currentStateIndex];
                    particleStateRemainingLifetimes[nextStateIndex] =
                        particleStateRemainingLifetimes[currentStateIndex];
                    particleStateWorldPositions[nextStateIndex] =
                        particleStateWorldPositions[currentStateIndex];
                }

                nextStateIndex++;
            }

            particleStateCount = nextStateIndex;
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
