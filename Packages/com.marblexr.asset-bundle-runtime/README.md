# Marble AssetBundle Runtime

`com.marblexr.asset-bundle-runtime` contains runtime C# types that are serialized into Marble AssetBundle prefabs. The AssetBundle authoring project and the Marble application must compile the same assembly and type contracts because an AssetBundle contains serialized component data, not the component's managed assembly.

## Compatibility contract

The following names are part of the serialized compatibility contract:

- Package: `com.marblexr.asset-bundle-runtime`
- Assembly: `Marble.AssetBundleRuntime`
- Fireworks type: `Marble.AssetBundleRuntime.Fireworks.FireworkParticleAudio`
- Serialized field names in runtime components

Both the authoring project and Marble application must reference the same Git commit or release tag. Do not track a moving branch in either project. A content release must record the Package revision used to build its AssetBundles.

Renaming an assembly, namespace, type, or serialized field is a breaking change. If a field rename is unavoidable, retain compatibility with `FormerlySerializedAs` and release a new major Package version. Removing a serialized type or changing a field to an incompatible type also requires a major version.

## Installation

This repository uses the Package as an embedded Package under `Packages/com.marblexr.asset-bundle-runtime`.

In the Marble application, add the repository URL and an immutable revision to `Packages/manifest.json`. Replace the example repository and revision with the published values:

```json
{
  "dependencies": {
    "com.marblexr.asset-bundle-runtime": "https://github.com/MarbleXR/MarbleAssetBundler.git?path=/Packages/com.marblexr.asset-bundle-runtime#<immutable-commit-sha-or-release-tag>"
  }
}
```

A commit SHA can be used instead of a release tag. The authoring project and application must resolve to the same revision before AssetBundles are published or loaded.

## Authoring AssetBundle prefabs

1. Add a component from this Package to a regular prefab.
2. Configure only runtime Asset references that will be included in the same AssetBundle or an explicitly declared dependency bundle.
3. Build the AssetBundle with the same Package revision that the Marble application uses.
4. Validate loading on every supported Player target before publishing the content.

Do not place a managed DLL from this Package inside an AssetBundle. The Marble application supplies `Marble.AssetBundleRuntime` in its Player build. `Runtime/link.xml` preserves serialized runtime types from IL2CPP stripping.

## Fireworks particle audio

`FireworkParticleAudio` plays pooled spatial audio when particles are born and expire. It supports Local, World, and Custom Particle System simulation spaces. The component owns its pools per instance and does not use coroutines or collections that allocate in its steady-state update.

The component expects separate explosion and shot audio prefabs. Each prefab must have an `AudioSource` on its root GameObject. Pool size zero, missing prefabs, empty clip arrays, and null clips are treated as no-op configurations.

Audio clips, volume, and pitch are selected for every playback. Reusing a pool entry replaces its previous return deadline. Increasing `ParticleSystem.main.maxParticles` can resize the cached particle array once; steady state does not create a new particle array per frame.

## Adding runtime types

Only types that AssetBundle prefabs directly serialize belong in this Package.

- Put each feature under `Runtime/<Feature>` and use `Marble.AssetBundleRuntime.<Feature>` as its namespace.
- Keep runtime code independent from `UnityEditor`, Marble application implementation assemblies, third-party Asset packages, and the AssetBundle Builder editor Package.
- Add only the narrow Unity module dependencies required by runtime code.
- Add Player-compatible tests under `Tests/Runtime` and update `Runtime/link.xml` for every serialized component type.
- Avoid shared mutable static state. AssetBundle instances must remain independent.
- Treat public types and serialized fields as a versioned content API.

Use Semantic Versioning for Package releases:

- Patch: compatible fixes with unchanged serialized contracts.
- Minor: new backward-compatible runtime types or optional fields.
- Major: incompatible assembly, type, serialized field, or behavior contract changes.

## Tests and performance gate

The host project's `Packages/manifest.json` includes this Package in `testables`, so its PlayMode and performance tests are discoverable in the Unity Test Runner.

Before publishing a Package revision, run the runtime behavior suite and the performance gate with Unity `6000.3.11f1`. The Fireworks gate uses four instances, 200 live particles, and 300 measured frames. After warm-up, the target is 0 B managed GC allocation per frame and a p95 component update time no greater than 1.0 ms in the Editor.

The component update marker is:

```text
Marble.AssetBundleRuntime.Fireworks.FireworkParticleAudio.LateUpdate
```
