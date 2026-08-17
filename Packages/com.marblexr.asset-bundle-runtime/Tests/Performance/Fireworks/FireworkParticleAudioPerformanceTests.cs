using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Marble.AssetBundleRuntime.Fireworks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Marble.AssetBundleRuntime.PerformanceTests.Fireworks
{
    public sealed class FireworkParticleAudioPerformanceTests
    {
        private const int InstanceCount = 4;
        private const int ParticlesPerInstance = 50;
        private const int WarmupFrames = 64;
        private const int MeasuredFrames = 300;
        private const int PoolSizePerKind = 4;
        private const double MaximumP95Milliseconds = 1.0d;

        private readonly List<Object> objectsToDestroy = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = objectsToDestroy.Count - 1; index >= 0; index--)
            {
                if (objectsToDestroy[index] != null)
                {
                    Object.DestroyImmediate(objectsToDestroy[index]);
                }
            }

            objectsToDestroy.Clear();
        }

        [UnityTest]
        public IEnumerator FourInstancesAndTwoHundredParticlesMeetEditorPerformanceGate()
        {
            AudioClip clip = Track(AudioClip.Create("PerformanceClip", 44100, 1, 44100, false));
            GameObject explosionPrefab = Track(CreateAudioPrefab("PerformanceExplosionAudio"));
            GameObject shotPrefab = Track(CreateAudioPrefab("PerformanceShotAudio"));
            FireworkParticleAudio[] components = new FireworkParticleAudio[InstanceCount];

            for (int index = 0; index < InstanceCount; index++)
            {
                components[index] = CreateRig(index, clip, explosionPrefab, shotPrefab);
            }

            yield return null;

            const float simulatedDeltaTime = 1f / 60f;
            float simulatedTime = 10f;
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                ProcessFrame(components, simulatedTime, simulatedDeltaTime);
                simulatedTime += simulatedDeltaTime;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long[] elapsedTicks = new long[MeasuredFrames];
            GC.GetAllocatedBytesForCurrentThread();
            long allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();

            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                ProcessFrame(components, simulatedTime, simulatedDeltaTime);
                elapsedTicks[frame] = Stopwatch.GetTimestamp() - startTimestamp;
                simulatedTime += simulatedDeltaTime;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;
            Array.Sort(elapsedTicks);
            int p95Index = (int)Math.Ceiling(MeasuredFrames * 0.95d) - 1;
            double p95Milliseconds =
                elapsedTicks[p95Index] * 1000d / Stopwatch.Frequency;

            int expectedAudioSourceCount = InstanceCount * PoolSizePerKind * 2;
            int actualAudioSourceCount = 0;
            for (int index = 0; index < components.Length; index++)
            {
                actualAudioSourceCount +=
                    components[index].GetComponentsInChildren<AudioSource>(true).Length;
            }

            TestContext.WriteLine(
                $"Unity {Application.unityVersion}; " +
                $"instances={InstanceCount}; particles={InstanceCount * ParticlesPerInstance}; " +
                $"frames={MeasuredFrames}; managedGC={allocatedBytes} B; " +
                $"CPU p95={p95Milliseconds:F4} ms; AudioSources={actualAudioSourceCount}");
            Assert.That(allocatedBytes, Is.EqualTo(0), "Steady-state ProcessFrame must not allocate managed memory.");
            Assert.That(
                p95Milliseconds,
                Is.LessThanOrEqualTo(MaximumP95Milliseconds),
                "Four instances and 200 live particles exceeded the Editor CPU p95 gate.");
            Assert.That(actualAudioSourceCount, Is.EqualTo(expectedAudioSourceCount));
        }

        private FireworkParticleAudio CreateRig(
            int instanceIndex,
            AudioClip clip,
            GameObject explosionPrefab,
            GameObject shotPrefab)
        {
            GameObject root = Track(new GameObject($"PerformanceFirework{instanceIndex + 1}"));
            root.SetActive(false);
            root.transform.SetPositionAndRotation(
                new Vector3(instanceIndex * 2f, instanceIndex, -instanceIndex),
                Quaternion.Euler(instanceIndex * 5f, instanceIndex * 10f, instanceIndex * 15f));
            ParticleSystem particleSystem = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particleSystem.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = ParticlesPerInstance;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            FireworkParticleAudio component = root.AddComponent<FireworkParticleAudio>();
            SetField(component, "audioExplosion", new[] { clip });
            SetField(component, "explosionAudioPrefab", explosionPrefab);
            SetField(component, "explosionPoolSize", PoolSizePerKind);
            SetField(component, "audioShot", new[] { clip });
            SetField(component, "shotAudioPrefab", shotPrefab);
            SetField(component, "shotPoolSize", PoolSizePerKind);

            root.SetActive(true);
            particleSystem.Pause(true);
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[ParticlesPerInstance];
            for (int index = 0; index < particles.Length; index++)
            {
                particles[index] = new ParticleSystem.Particle
                {
                    position = new Vector3(index * 0.01f, instanceIndex, -index * 0.02f),
                    startLifetime = 10f,
                    remainingLifetime = 5f,
                    startSize = 1f,
                };
            }

            particleSystem.SetParticles(particles, particles.Length);
            return component;
        }

        private static void ProcessFrame(
            FireworkParticleAudio[] components,
            float simulatedTime,
            float simulatedDeltaTime)
        {
            for (int index = 0; index < components.Length; index++)
            {
                components[index].ProcessFrame(simulatedTime, simulatedDeltaTime);
            }
        }

        private static GameObject CreateAudioPrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            AudioSource source = prefab.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            prefab.SetActive(false);
            return prefab;
        }

        private static void SetField<T>(FireworkParticleAudio component, string fieldName, T value)
        {
            FieldInfo field = typeof(FireworkParticleAudio).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing serialized field: {fieldName}");
            field.SetValue(component, value);
        }

        private T Track<T>(T instance) where T : Object
        {
            objectsToDestroy.Add(instance);
            return instance;
        }
    }
}
