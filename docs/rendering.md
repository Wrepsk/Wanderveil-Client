# Optional rendering

Status: candidate investigated in source only; no renderer dependency installed.

[CrystalRenderer](https://github.com/Wrepsk/crystal-renderer/tree/f9ccc565a313c23eaa713aacf4e197782b9d3a04) is the intended candidate for optional presentation experiments. Its current API submits textured screen surfaces through a frame/backend lifecycle. Do not assume arbitrary book meshes, raised stamp geometry, or general VFX are provided.

A host-owned adapter, provisionally `IPresentationRenderer`, would isolate advanced presentation. Core content, text, navigation, consent, collection, and storage remain complete through ordinary ImGui. Reduced motion skips effects; renderer failures return to ordinary UI.

Before integration, choose pinned dependency delivery and retain applicable notices. Demonstrate one scoped effect in the actual plugin, including supported callback/thread usage, texture ownership, failure polling, pending/canceled frame behavior, output switches, resize, logout, reload, and disposal. Measure frame/memory cost. Record Windows/Wine evidence and known driver limitations separately.

The upstream README describes a submodule/project-reference consumption path; this scaffold does not adopt it yet. No private project reference may become a public build prerequisite. See the renderer's [host README](https://github.com/Wrepsk/crystal-renderer/blob/f9ccc565a313c23eaa713aacf4e197782b9d3a04/README.md) and its retained provenance before implementing the adapter.
