using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Marble.AssetBundleRuntime.Fireworks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Marble.AssetBundleRuntime.Tests.Fireworks
{
    public sealed class FireworkParticleAudioTests
    {
        private const float PositionTolerance = 0.001f;

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
        public IEnumerator ShotAndExplosionUseTheirOwnSettingsAndReturnToPools()
        {
            AudioClip explosionClip = CreateClip("ExplosionClip");
            AudioClip shotClip = CreateClip("ShotClip");
            TestRig rig = CreateRig(
                audioExplosion: new[] { explosionClip },
                audioShot: new[] { shotClip },
                poolReturnTimer: 0.05f,
                explosionPoolSize: 1,
                shotPoolSize: 1);

            SetField(rig.Component, "explosionMinVolume", 0.35f);
            SetField(rig.Component, "explosionMaxVolume", 0.35f);
            SetField(rig.Component, "explosionPitchMin", 0.8f);
            SetField(rig.Component, "explosionPitchMax", 0.8f);
            SetField(rig.Component, "shootMinVolume", 0.08f);
            SetField(rig.Component, "shootMaxVolume", 0.08f);
            SetField(rig.Component, "shootPitchMin", 1.2f);
            SetField(rig.Component, "shootPitchMax", 1.2f);
            Activate(rig);

            yield return ProcessParticle(rig, Vector3.one, 10f, 10f);

            AudioSource shotSource = FindPooledSource(rig, "ShotAudioPrefab");
            Assert.That(shotSource.gameObject.activeSelf, Is.True);
            Assert.That(shotSource.clip, Is.SameAs(shotClip));
            Assert.That(shotSource.volume, Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(shotSource.pitch, Is.EqualTo(1.2f).Within(0.0001f));

            yield return ProcessParticle(rig, Vector3.zero, 1f, 0.001f);

            AudioSource explosionSource = FindPooledSource(rig, "ExplosionAudioPrefab");
            Assert.That(explosionSource.gameObject.activeSelf, Is.True);
            Assert.That(explosionSource.clip, Is.SameAs(explosionClip));
            Assert.That(explosionSource.volume, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(explosionSource.pitch, Is.EqualTo(0.8f).Within(0.0001f));

            yield return new WaitForSeconds(0.07f);

            Assert.That(shotSource.gameObject.activeSelf, Is.False);
            Assert.That(explosionSource.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ShotClipSelectionUsesShotArrayLength()
        {
            AudioClip firstExplosionClip = CreateClip("ExplosionClipA");
            AudioClip secondExplosionClip = CreateClip("ExplosionClipB");
            AudioClip shotClip = CreateClip("OnlyShotClip");
            TestRig rig = CreateRig(
                audioExplosion: new[] { firstExplosionClip, secondExplosionClip },
                audioShot: new[] { shotClip },
                shotPoolSize: 1);
            Activate(rig);

            for (int index = 0; index < 8; index++)
            {
                yield return ProcessParticle(rig, new Vector3(index, 0f, 0f), 10f, 10f);
                Assert.That(FindPooledSource(rig, "ShotAudioPrefab").clip, Is.SameAs(shotClip));
            }
        }

        [UnityTest]
        public IEnumerator InstancesKeepPoolsAndPlaybackStateSeparate()
        {
            AudioClip firstClip = CreateClip("FirstShot");
            AudioClip secondClip = CreateClip("SecondShot");
            TestRig first = CreateRig(audioShot: new[] { firstClip }, shotPoolSize: 1);
            TestRig second = CreateRig(audioShot: new[] { secondClip }, shotPoolSize: 2);
            first.Root.name = "FirstFirework";
            second.Root.name = "SecondFirework";
            Activate(first);
            Activate(second);

            yield return ProcessParticle(first, new Vector3(1f, 2f, 3f), 10f, 10f);
            yield return ProcessParticle(second, new Vector3(-1f, -2f, -3f), 10f, 10f);

            AudioSource firstSource = FindPooledSource(first, "ShotAudioPrefab");
            AudioSource secondSource = FindPooledSource(second, "ShotAudioPrefab");
            Assert.That(firstSource, Is.Not.SameAs(secondSource));
            Assert.That(firstSource.clip, Is.SameAs(firstClip));
            Assert.That(secondSource.clip, Is.SameAs(secondClip));
            Assert.That(first.Component.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(1));
            Assert.That(second.Component.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ReusingSinglePoolItemOverwritesItsOldReturnDeadline()
        {
            AudioClip shotClip = CreateClip("ShotClip");
            TestRig rig = CreateRig(
                audioShot: new[] { shotClip },
                poolReturnTimer: 0.15f,
                shotPoolSize: 1);
            Activate(rig);

            yield return ProcessParticle(rig, Vector3.zero, 10f, 10f);
            AudioSource source = FindPooledSource(rig, "ShotAudioPrefab");
            yield return new WaitForSeconds(0.1f);
            yield return ProcessParticle(rig, new Vector3(4f, 5f, 6f), 10f, 10f);
            yield return new WaitForSeconds(0.08f);

            Assert.That(source.gameObject.activeSelf, Is.True, "The first deadline must not stop reused audio.");
            AssertVector(source.transform.position, new Vector3(4f, 5f, 6f));

            yield return new WaitForSeconds(0.09f);

            Assert.That(source.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator EmptyClipsAndZeroSizedPoolsAreNoOps()
        {
            TestRig noClips = CreateRig(
                audioExplosion: Array.Empty<AudioClip>(),
                audioShot: Array.Empty<AudioClip>(),
                explosionPoolSize: 1,
                shotPoolSize: 1);
            TestRig noPool = CreateRig(
                audioExplosion: new[] { CreateClip("Explosion") },
                audioShot: new[] { CreateClip("Shot") },
                explosionPoolSize: 0,
                shotPoolSize: 0);
            Activate(noClips);
            Activate(noPool);

            yield return ProcessParticle(noClips, Vector3.zero, 10f, 10f);
            yield return ProcessParticle(noPool, Vector3.zero, 1f, 0.001f);

            AudioSource[] sourcesWithoutClips = noClips.Component.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sourcesWithoutClips, Has.Length.EqualTo(2));
            Assert.That(sourcesWithoutClips.All(source => !source.gameObject.activeSelf), Is.True);
            Assert.That(noPool.Component.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator LocalSimulationSpaceUsesParticleSystemTransform()
        {
            TestRig rig = CreateRig(audioShot: new[] { CreateClip("Shot") }, shotPoolSize: 1);
            rig.Root.transform.SetPositionAndRotation(
                new Vector3(3f, -2f, 7f),
                Quaternion.Euler(15f, 40f, -20f));
            rig.Root.transform.localScale = new Vector3(2f, 0.5f, 1.5f);
            SetSimulationSpace(rig, ParticleSystemSimulationSpace.Local, null);
            Activate(rig);
            Vector3 particlePosition = new Vector3(1.25f, -0.5f, 2f);

            yield return ProcessParticle(rig, particlePosition, 10f, 10f);

            AssertVector(
                FindPooledSource(rig, "ShotAudioPrefab").transform.position,
                rig.ParticleSystem.transform.TransformPoint(particlePosition));
        }

        [UnityTest]
        public IEnumerator WorldSimulationSpaceKeepsParticleWorldPosition()
        {
            TestRig rig = CreateRig(audioShot: new[] { CreateClip("Shot") }, shotPoolSize: 1);
            rig.Root.transform.SetPositionAndRotation(
                new Vector3(10f, 20f, 30f),
                Quaternion.Euler(30f, 60f, 90f));
            rig.Root.transform.localScale = new Vector3(3f, 2f, 4f);
            SetSimulationSpace(rig, ParticleSystemSimulationSpace.World, null);
            Activate(rig);
            Vector3 particlePosition = new Vector3(-2f, 5f, 8f);

            yield return ProcessParticle(rig, particlePosition, 10f, 10f);

            AssertVector(FindPooledSource(rig, "ShotAudioPrefab").transform.position, particlePosition);
        }

        [UnityTest]
        public IEnumerator CustomSimulationSpaceUsesCustomTransform()
        {
            TestRig rig = CreateRig(audioShot: new[] { CreateClip("Shot") }, shotPoolSize: 1);
            GameObject customSpace = Track(new GameObject("CustomSimulationSpace"));
            customSpace.transform.SetPositionAndRotation(
                new Vector3(-6f, 4f, 2f),
                Quaternion.Euler(-10f, 25f, 70f));
            customSpace.transform.localScale = new Vector3(0.5f, 3f, 2f);
            SetSimulationSpace(rig, ParticleSystemSimulationSpace.Custom, customSpace.transform);
            Activate(rig);
            Vector3 particlePosition = new Vector3(3f, 2f, -1f);

            yield return ProcessParticle(rig, particlePosition, 10f, 10f);

            AssertVector(
                FindPooledSource(rig, "ShotAudioPrefab").transform.position,
                customSpace.transform.TransformPoint(particlePosition));
        }

        private TestRig CreateRig(
            AudioClip[] audioExplosion = null,
            AudioClip[] audioShot = null,
            float poolReturnTimer = 1.5f,
            int explosionPoolSize = 0,
            int shotPoolSize = 0)
        {
            GameObject root = Track(new GameObject("Firework"));
            root.SetActive(false);
            ParticleSystem particleSystem = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particleSystem.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 64;

            FireworkParticleAudio component = root.AddComponent<FireworkParticleAudio>();
            GameObject explosionPrefab = Track(CreateAudioPrefab("ExplosionAudioPrefab"));
            GameObject shotPrefab = Track(CreateAudioPrefab("ShotAudioPrefab"));
            SetField(component, "poolReturnTimer", poolReturnTimer);
            SetField(component, "audioExplosion", audioExplosion ?? Array.Empty<AudioClip>());
            SetField(component, "explosionAudioPrefab", explosionPrefab);
            SetField(component, "explosionPoolSize", explosionPoolSize);
            SetField(component, "audioShot", audioShot ?? Array.Empty<AudioClip>());
            SetField(component, "shotAudioPrefab", shotPrefab);
            SetField(component, "shotPoolSize", shotPoolSize);
            return new TestRig(root, particleSystem, component);
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

        private static void Activate(TestRig rig)
        {
            rig.Root.SetActive(true);
            rig.ParticleSystem.Pause(true);
        }

        private static IEnumerator ProcessParticle(
            TestRig rig,
            Vector3 position,
            float startLifetime,
            float remainingLifetime)
        {
            ParticleSystem.Particle particle = new ParticleSystem.Particle
            {
                position = position,
                startLifetime = startLifetime,
                remainingLifetime = remainingLifetime,
                startSize = 1f,
            };
            rig.ParticleSystem.SetParticles(new[] { particle }, 1);
            yield return null;
            rig.ParticleSystem.Clear(true);
        }

        private static void SetSimulationSpace(
            TestRig rig,
            ParticleSystemSimulationSpace simulationSpace,
            Transform customSpace)
        {
            ParticleSystem.MainModule main = rig.ParticleSystem.main;
            main.simulationSpace = simulationSpace;
            if (simulationSpace == ParticleSystemSimulationSpace.Custom)
            {
                main.customSimulationSpace = customSpace;
            }
        }

        private static AudioSource FindPooledSource(TestRig rig, string prefabName)
        {
            return rig.Component
                .GetComponentsInChildren<AudioSource>(true)
                .Single(source => source.gameObject.name.StartsWith(prefabName, StringComparison.Ordinal));
        }

        private static void SetField<T>(FireworkParticleAudio component, string fieldName, T value)
        {
            FieldInfo field = typeof(FireworkParticleAudio).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing serialized field: {fieldName}");
            field.SetValue(component, value);
        }

        private AudioClip CreateClip(string name)
        {
            return Track(AudioClip.Create(name, 44100, 1, 44100, false));
        }

        private T Track<T>(T instance) where T : Object
        {
            objectsToDestroy.Add(instance);
            return instance;
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(PositionTolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(PositionTolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(PositionTolerance));
        }

        private sealed class TestRig
        {
            internal TestRig(
                GameObject root,
                ParticleSystem particleSystem,
                FireworkParticleAudio component)
            {
                Root = root;
                ParticleSystem = particleSystem;
                Component = component;
            }

            internal GameObject Root { get; }

            internal ParticleSystem ParticleSystem { get; }

            internal FireworkParticleAudio Component { get; }
        }
    }
}
