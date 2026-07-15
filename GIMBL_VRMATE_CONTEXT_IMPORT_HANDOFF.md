
# vrMate `.vr` Context Import — Handoff Brief

*Purpose: brief a Claude Code agent so it can resume implementing a batch importer that converts vrMate's `.vr` visual-context files into ready-to-run Gimbl `.unity` scenes. Written after researching vrMate's `.vr` format/loader, GimblFork's package structure, and GimblEnv's scene layout. No implementation has started yet — this is a fully-scoped, approved plan ready to execute.*

---

## Current status (2026-07-14)

- ✅ **Fully planned, approved by the user, not yet implemented.** Everything below is ready to build against.
- The canonical plan file lives at `C:\Users\megha\.claude\plans\stateless-nibbling-metcalfe.md` (Claude Code plan-mode artifact) — this document is the same content made durable/repo-visible so a fresh agent/session can resume without that plan file.
- **Next action:** start Phase 1 (port vrMate's Resources into GimblEnv) — see Roadmap below.

---

## 1. The problem / goal

The lab has a large archive of vrMate `.vr` context files — pre-authored corridor visuals (wall/floor textures, cue objects, skybox) built for the old vrMate rig. The ongoing Gimbl↔behaviorMate integration (see `GIMBL_BEHAVIORMATE_INTEGRATION_HANDOFF.md`) already replaced vrMate's live position/scene streaming with Gimbl-native, Unity-authored scenes — meaning every one of those existing corridor visuals would otherwise have to be **manually rebuilt** in Unity, one wall/floor piece at a time, to be usable in Gimbl.

**Goal:** batch-convert every `.vr` file into a ready-to-run Gimbl `.unity` scene, reusing vrMate's existing visual content, so a session can just open e.g. `ctxA.unity` instead of hand-placing 50+ objects per corridor. The corridor path itself (the `PathCreator` spline that drives the treadmill controller) is drawn in **by hand afterward** — not part of this automation.

---

## 2. What a `.vr` file actually is

Confirmed by direct inspection of real files (e.g. `behaviorMate/vr_contexts/4m_ctxA.vr`, `Desktop/BehaviorMate_0.1.5_USB/vr_contexts/ctx1.vr`, `cactus_land.vr`):

Plain JSON, flat list of objects, no embedded geometry — everything is a reference by name to a prefab/material that must already exist in the target Unity project's `Resources/` folder:

```json
{
  "objects": [
    { "type": "HighWall", "id": "HighWall_3_left",
      "Position": [-20, 125, 0], "Rotation": [0,0,0], "Scale": [1, 1.43, 0.75],
      "material": "Wall_Materials/green_checkerboard_wall" },
    { "type": "PathSegment", "id": "path_3_yellow_lines",
      "Position": [0, 250, 0], "Rotation": [0,0,0], "Scale": [4, 10, 1],
      "material": "Track_Materials/blue_eggs" }
  ],
  "skybox": "black"
}
```

Object `type`s seen: `PathSegment`, `HighWall`, `LowWall`, `EndTunnel`, `Arch`, `Cylinder`, `Sphere`, `Light`, plus dressing objects (`Mixed_Cactus_01/02`, `Mixed_Palm_tree_01`, `Mixed_Well_01`, `Photographer`, etc.). Some files also have a top-level `apply_filter` field (vrMate's red/green/dimming visual filter shaders) — **out of scope for v1**, log and skip.

### vrMate's own loader (the algorithm we're porting)
- `vrMate/Assets/Editor/ControllerEditor.cs::ReadFile()` — parses the file, iterates `objects`, calls `Controller.cs::EditContext()`/`CreateCue()` per object, then handles `skybox`.
- `Controller.cs::CreateCue()` — the critical transform logic:
  ```csharp
  GameObject new_obj = Instantiate(Resources.Load("Prefabs/"+object_info["type"])) as GameObject;
  Vector3 position = new Vector3(
      object_info["Position"][0].AsFloat,   // → Unity X
      object_info["Position"][2].AsFloat,   // → Unity Y
      object_info["Position"][1].AsFloat);  // → Unity Z
  cue_transform.position = position;
  // Same index swap (0→X, 2→Y, 1→Z) applied to Rotation (via Quaternion.Euler) and Scale (localScale).
  // NO scale multiplier — .vr coordinates are already vrMate's Unity world-space units.
  ```
  Material: `Resources.Load("Materials/"+object_info["material"])` applied to child(0)'s renderer if the prefab has children, else the object's own renderer.
- Skybox: `Resources.Load("Skybox/"+json["skybox"])` → assigned to `RenderSettings.skybox`.

**Important distinction:** this axis swap (JSON index 0/1/2 → Unity X/Z/Y, no scale) is for **static `.vr` geometry import**. It is a *different* code path from `BehaviorMateReceiver.cs`'s **live actor position** conversion (`msg.y`→Z, `msg.z`→Y, `msg.x`→X, multiplied by a tuned `positionScale ≈ 0.3333`) — both happen to use the same axis convention (consistent with vrMate's original design), but the position importer must NOT apply `positionScale` — `.vr` files are already in the right units.

---

## 3. What vrMate's prefabs actually are (import risk assessment)

Inspected real `.prefab` YAML in `C:\Users\megha\Documents\GitHub\vrMate` (`Assets/Resources/Prefabs/`):

- **HighWall**: parent GameObject + 1 child cube (MeshFilter=built-in cube, MeshRenderer, BoxCollider).
- **Arch**: parent + 3 cube children (pillars), same component pattern.
- **PathSegment**: single GameObject, MeshFilter=built-in plane, MeshRenderer, MeshCollider.
- All are built from **Unity's built-in primitive meshes** — no custom modeled geometry.
- Every prefab's root carries one custom component: `vrMatePrefab.cs` (`Assets/Scripts/vrMatePrefab.cs`):
  ```csharp
  public class vrMatePrefab : MonoBehaviour { public string prefab_name; }
  ```
  This is the **only** vrMate script dependency the prefabs have — port this one file and everything resolves cleanly.
- Materials (`Resources/Materials/{Track_Materials,Wall_Materials,Pillar_Materials,Colors,DesertMaterials}/*.mat`) use the **Standard/VertexLit shaders** (built-in render pipeline), backed by plain `.png`/`.jpg` textures in `Materials/Textures/`. Three custom shaders exist (`dimming_filter`, `green_filter`, `red_filter`) but aren't referenced by object materials — only relevant if `apply_filter` support is added later.
- **Confirmed render-pipeline compatibility:** GimblFork/GimblEnv use the **Built-in Render Pipeline** (`Packages/manifest.json` has no `com.unity.render-pipelines.*`; `ProjectSettings/GraphicsSettings.asset` has `m_CustomRenderPipeline: {fileID: 0}`) — same as vrMate. **No shader/material conversion pass needed.**
- Unity version gap: vrMate = 2019.2.10f1, GimblFork/GimblEnv = 2023.1.22f1 (4 years). Expect a one-time reserialization on first import; not expected to break anything given the assets are simple primitives + built-in shaders.
- vrMate git remotes (for reference, not used): `origin` → `https://github.com/losonczylab/vrMate.git`, `fork` → `https://github.com/mcum96/vrMate.git`.

---

## 4. Where things live (confirmed on disk)

| What | Path |
|---|---|
| vrMate source (local checkout) | `C:\Users\megha\Documents\GitHub\vrMate` (branch/remotes above) |
| vrMate prefabs to port | `vrMate\Assets\Resources\Prefabs\**` |
| vrMate materials to port | `vrMate\Assets\Resources\Materials\**` |
| vrMate skyboxes to port | `vrMate\Assets\Resources\Skybox\**` |
| vrMate custom script to port | `vrMate\Assets\Scripts\vrMatePrefab.cs` |
| Known `.vr` files (small set, 4) | `C:\Users\megha\Documents\GitHub\behaviorMate\vr_contexts\4m_ctxA.vr` … `ctxD.vr` |
| **Target `.vr` archive (large set, ~65 files — use this one)** | `C:\Users\megha\Desktop\BehaviorMate_0.1.5_USB\vr_contexts\` (includes `ctxA–G`, `ctx1–8`, `ctx_A1-3`…`E1-3`, `base_context*`, `new_context_*_jo`, `cactus_land*`, `compression_*`, `training_ctx`, `empty_soil`, `a_context`/`b_context`, `2m_ctx1_kg.vr`/`2m_ctx2_kg.vr`) |
| GimblFork (Gimbl package, **do not modify** for this feature) | `C:\Users\megha\Documents\GitHub\GimblFork` — confirmed a pure Unity **package** (`package.json` at root; `Scripts/`, `Editor/`, `Resources/` at package root; **no** `Assets/Scenes`, **no** committed `.unity` files) |
| **GimblEnv (target project — all new work goes here)** | `C:\Users\megha\Documents\GitHub\GimblEnv`, branch **`mouse_VR`** (confirmed current branch), `Packages/manifest.json` already points `"gimbl": "file:../../GimblFork"` (local live link) |
| GimblEnv template scene to clone | `GimblEnv\Assets\InfiniteTrack.unity` — has a working, calibrated actor/controller/`BehaviorMateReceiver`/display rig already |
| GimblEnv scene groups to preserve when cloning | Top-level objects confirmed via YAML scan: `Controllers`, `Mouse` (actor), `Logger`, `MQTT Client`, `VR_setup` (contains `LeftMonitor`/`CenterMonitor`/`RightMonitor` + their cameras), `Actors`, `Paths`, `BehaviorMate` |
| GimblEnv scene content to strip/replace per new scene | `TunnelSegment0/1/2` + `Floor` instances (repeated ~8+ times) and the `TunnelPath` `PathCreator` object — **confirm exact top-level parent name by opening `InfiniteTrack.unity` in the Editor before writing the strip logic**; static YAML grep found the object names but not an unambiguous single container to delete as a group |

---

## 5. Approved plan (do this, in order)

**Everything is additive inside GimblEnv. No changes to the GimblFork package.** This matches the existing precedent that `InfiniteCorridorTask` (a specific task/environment, not a core engine feature) lives entirely in `GimblEnv/Assets/`, not in the shared package.

### Step 1 — Port vrMate's visual assets (one-time copy)
Copy into `GimblEnv/Assets/Resources/`, preserving the same relative subfolder structure so `Resources.Load("Prefabs/"+type)` / `Resources.Load("Materials/"+material)` paths resolve unchanged:
- `Resources/Prefabs/**`
- `Resources/Materials/**` (incl. `Materials/Textures/`)
- `Resources/Skybox/**`
- `Assets/Scripts/vrMatePrefab.cs` → `GimblEnv/Assets/Scripts/vrMatePrefab.cs` (unchanged)

Skip: vrMate's `Controller.cs`/`ControllerEditor.cs`/`SimpleJSON.cs` (Gimbl's importer needs its own small JSON parse, not vrMate's live-context machinery) and the 3 filter shaders (v1 non-goal).

### Step 2 — New Editor importer: `GimblEnv/Assets/Editor/VRContextImporter.cs`
A new `EditorWindow` or `[MenuItem]` static method that batch-converts a folder of `.vr` files into scenes.

**Inputs** (window fields or `EditorUtility.OpenFolderPanel`):
- Source folder of `.vr` files — default to `Desktop/BehaviorMate_0.1.5_USB/vr_contexts/`
- Template scene to clone — default `Assets/InfiniteTrack.unity`
- Output folder — default `Assets/VRContexts/`

**Per-file algorithm:**
1. Duplicate the template scene (`AssetDatabase.CopyAsset` the `.unity` file to the new path, then `EditorSceneManager.OpenScene` the copy) — never mutate `InfiniteTrack.unity` itself.
2. Strip old corridor geometry: destroy the existing `TunnelSegment0/1/2`/`Floor` instances and the `TunnelPath` object (confirm exact container name in-Editor first, per §4 above).
3. Parse the `.vr` JSON (`JsonUtility` or a small hand-rolled parser is sufficient for this flat schema — no need to port `SimpleJSON.cs`).
4. For each object: `Resources.Load<GameObject>("Prefabs/"+type)`, `Resources.Load<Material>("Materials/"+material)`; instantiate under a new empty `"VRContext"` parent; apply the exact vrMate axis swap (position/rotation/scale, index 0→X, 2→Y, 1→Z, **no scale multiplier**); assign material to child(0)'s renderer if present else the object's own. If a prefab/material name doesn't resolve, log a warning (`.vr` filename + object id) and skip — expect some misses across ~65 varied files.
5. If `json["skybox"]` present: `RenderSettings.skybox = Resources.Load<Material>("Skybox/"+skybox)`.
6. Add a fresh empty `PathCreator` under the scene's `Paths` group (`AddComponent<PathCreator>()`, `bezierPath.Space = PathSpace.xz`, `ControlPointMode = Automatic` — matches `GimblFork/Editor/ActorWindow.cs::CreatePath`) for manual path-drawing afterward.
7. `EditorSceneManager.SaveScene` to `Assets/VRContexts/<sanitized .vr filename>.unity`.
8. Loop across every `.vr` file in the source folder; log an import summary at the end.

### Non-goals for v1
- No automatic path-drawing (confirmed manual, per-scene, post-import).
- No porting of vrMate's red/green/dimming visual filters (`apply_filter`) — log and skip if present.
- No live/runtime `.vr` loading — Editor-time batch conversion producing static `.unity` assets only.

---

## 6. Verification plan
1. Run the importer against the **known small set first** (`behaviorMate/vr_contexts/`, 4 files) before pointing it at the full ~65-file archive.
2. Open one generated scene (e.g. `Assets/VRContexts/4m_ctxA.unity`) — confirm no missing-script warnings, no pink/magenta materials, geometry roughly matches the expected corridor layout, skybox applied.
3. Enter Play mode on that scene, manually move the actor (or feed test position data as done in prior `BehaviorMateReceiver` testing) to confirm the actor/controller/display rig still functions unchanged.
4. Spot-check 2–3 more scenes from the full batch before treating the whole run as done.
5. Draw in a `PathCreator` path by hand on at least one scene to confirm the empty stub from step 6 is usable exactly like `ActorWindow`'s "Create Path" button output.

---

## 7. Open questions / things a fresh agent should confirm, not guess

- Exact top-level GameObject name(s) in `InfiniteTrack.unity` that parent `TunnelSegment0/1/2`/`Floor` (open in-Editor to confirm before writing the strip logic).
- Whether `Directional Light` in `InfiniteTrack.unity` is generic scene lighting (leave alone) or tied to the corridor content (strip) — current assumption is "leave alone."
- Whether any `.vr` files in the ~65-file archive use object `type`s not covered by vrMate's `Resources/Prefabs/` list found so far — the importer's per-object warning log (step 4) will surface these; decide case-by-case whether to add the missing prefab or accept the gap.
