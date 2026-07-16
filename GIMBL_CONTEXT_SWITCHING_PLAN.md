# Gimbl ↔ behaviorMate — Mid-Session VR Context Switching (Planning Doc)

*Purpose: brief an implementer (fresh Claude Code session or a labmate) to build behaviorMate-driven mid-session VR context switching in Gimbl. Written 2026-07-16 after inspecting behaviorMate's Java source, Gimbl's `BehaviorMateReceiver`, and the config format. **Update 2026-07-16: v1 is IMPLEMENTED and switching live on the rig** (all 7 steps — see the "v1 status" box in §4). §7 is the current forward plan.*

---

## 0. Guiding principle

**behaviorMate is the single source of truth for *which* VR context is active and *when* it switches. Gimbl renders the context and toggles it.** The goal is to make it structurally impossible for an experimentalist to run the wrong environment against a given behaviorMate script, and to record the environment history in the behaviorMate output.

The same design must serve **two run modes** with one shared renderer (so the mouse and the RNN see a provably identical environment — this equivalence is core to the science):
- **Live mouse:** behaviorMate drives the switch.
- **ML-Agents / RNN training:** a Gimbl-side schedule drives the switch (no behaviorMate).

---

## 1. The scene model (important mental shift)

**Old model:** each context is a baked `.unity` scene; opening `4m_ctxA.unity` and pressing Play *is* how you pick the context. Scene selection = context selection.

**New model:** you open **one fixed "rig" scene** (actor, controllers, display, `BehaviorMateReceiver`, `ContextManager`) with **no corridor geometry baked in**. On Play it waits; when behaviorMate (or the training schedule) says "load context A", `VRContextBuilder` builds A into the scene at runtime. Context is **injected at runtime**, never chosen by scene.

Consequences:
- There is exactly **one** scene to open for experiments → no wrong one to pick.
- The baked `.unity` files (e.g. `4m_ctxA.unity`) become **authoring/inspection artifacts**, not run targets. No mass-baking of 65 scenes.
- Open decision for v1: what the rig scene shows before a context loads (blank/skybox-only vs a "waiting for behaviorMate…" placeholder).

---

## 2. What already exists (starting point)

- **`GimblEnv/Assets/Editor/VRContextImporter.cs`** — Editor tool that (a) imports a single `.vr` into a cloned scene (`Import Single .vr (test)`) and (b) builds a `TunnelPath` from a `.vr`'s PathSegment centerline (`Add Path from .vr`). **This is the code to refactor into the runtime `VRContextBuilder`.** Its core logic (verified correct on `4m_ctxA`):
  - **Axis swap:** JSON index `0→UnityX, 2→UnityY, 1→UnityZ` for Position/Rotation/Scale.
  - **Scale:** position AND localScale × `GeometryScale = 0.3333` (= `positionScale`; cm→Unity). Rotation never scaled.
  - **Material:** child(0)'s renderer if the prefab has children, else its own; use `sharedMaterial`.
  - **Path:** PathSegment centers (axis-swapped + scaled), ordered by Z, `new BezierPath(anchors, false, PathSpace.xz)` under a `Paths` group.
- **`GimblEnv/Assets/Resources/`** — ported vrMate `Prefabs/`, `Materials/`, `Skybox/` (+ `vrMatePrefab.cs`), `.meta` GUIDs preserved. Gimbl uses the Built-in Render Pipeline (matches vrMate) — no material conversion needed.
- **`GimblFork/Scripts/BehaviorMate/BehaviorMateReceiver.cs`** — receive-only UDP (port 4020). Currently consumes only `{"position":{"y":..}}` and **ignores** `vr_config`/`action`/`view`/`fog`. **This is the hook point.** Editing the GimblFork package is approved for this feature.
- The `.vr` format is flat JSON: `{"objects":[{type,id,Position,Rotation,Scale,material?}...], "skybox"?, "apply_filter"?}`. `apply_filter` is a v1 non-goal (log & skip).

Related memory (local): `gimbl-context-switching-design`, `behaviormate-protocol-and-tdml`, `vrmate-context-import`.

---

## 3. How behaviorMate talks today (protocol facts)

behaviorMate source: `C:\Users\megha\Documents\GitHub\behaviorMate` (Java, `src/*.java`).

- **Channel:** UDP to `display_1` `ip:send_port` (config: `127.0.0.1:4020`); reply channel `receive_port` `4021` (currently unused by Gimbl).
- **Geometry (to be removed under Option B):** `VrContextList2.java::setupVr(vr_file)` reads the `.vr` itself and sends the whole thing as one `{"vr_config":{objects,skybox,...}}` message (`VrContext.java::setupVr` lines ~67–72; `VrContextList2.java` line ~148). `vr_config` is used ONLY for sending — nothing internal consumes it, so removing the send is safe.
- **Control:** on activate/deactivate, sends `{"action":"start","context":"<id>"}` / `"stop"` / `"clear"`.
- **TDML output:** `FileWriter.java` — newline-delimited JSON, one event per line, appended live, at `<datapath>/<mouse>/<mouse>_<yyyyMMddHHmmss>.tdml`. Context on/off is **already logged**: `{"context":{"id":"<id>","action":"start"|"stop"}}` via `tc.writeLog`, timestamped.
- **Lap/time switching already exists as config decorators:** `ScheduledContextDecorator` (`lap_list` explicit laps or ranges, `repeat`, `keep_on`), `AlternatingContextDecorator` (`n_lap`/`offset_lap`), `TimedContextDecorator`/`DelayedContextDecorator`. All emit the same start/stop messages. behaviorMate owns lap counting; `lap` is threaded into every context `check(position,time,lap,msg_buffer)`.
- **Naming gotcha:** "context" is overloaded — the **VR** context (`class:"vr2"`, has `vr_file`) vs **operant/reward** contexts (reward zones, `display_color`). Both emit `action:start/stop`; Gimbl must act on VR contexts **only**.

### Design decision — Option B (chosen): Gimbl reads the `.vr` files; behaviorMate is control-only
Rejected the alternative (Gimbl builds from behaviorMate's streamed `vr_config`) because live and training would then render through different code paths and could drift. Under Option B there is ONE builder reading the SAME `.vr` file in both modes → provable equivalence.
- behaviorMate **stops** streaming `vr_config`; its start message instead carries the filename:
  `{"action":"start","context":"4m_ctxD","vr_file":"vr_contexts/4m_ctxD.vr"}`.
- The `.vr` file is the shared source both sides point at.

---

## 4. v1 — Live-mouse lap switching (FIRST DELIVERABLE)

**Definition of done:** on the rig, the environment switches from context A to B at a lap boundary (e.g., ends lap 20 in A, starts lap 21 in B), driven entirely by the behaviorMate config, recorded in the TDML. Session stays continuous; actor/rig never resets.

### Switch mechanism
**Option C — preload contexts as disabled subtrees, toggle `SetActive`.** Instant, no scene reload, ML-Agents/parallel-area safe. behaviorMate announces both contexts at trial start → Gimbl preloads both (disabled) → start/stop toggles the active one.

### Itemized steps
1. **`VRContextBuilder`** (GimblEnv, runtime class) — refactor the importer core so it builds a full context subtree from a parsed `.vr`: **geometry + that context's `TunnelPath`** (path gen moves in here; the Editor `Add Path` command calls the same shared method). Output parented under `VRContext_<id>`.
2. **`ContextManager`** (GimblEnv) — preload the session's contexts as disabled subtrees; `SwitchTo(id)` toggles active/inactive and repoints the treadmill controller at the active context's path. **Load-time guardrail:** all contexts in a switch set must share `track_length` (validate; warn/refuse on mismatch).
3. **Receiver hook** (GimblFork package) — `BehaviorMateReceiver` stops discarding context messages; forwards `{action, context, vr_file}` to `ContextManager` via an event. **Filter to VR contexts only** (ignore reward-context start/stops).
4. **behaviorMate change (minimal)** — make the VR context message control-only: add `vr_file` to the start message; stop reading the `.vr` and sending `vr_config` (`VrContext.java` / `VrContextList2.java`).
5. **behaviorMate config** — define VR contexts A & B, wrap in `ScheduledContextDecorator`s by lap (A: laps 1–20, B: 21+). Likely no behaviorMate code beyond step 4.
6. **TDML enrichment** — add `lap#` + `vr_file` to the context log line.
7. **Verify (live)** — run the rig; confirm the switch fires at the lap boundary, geometry/skybox correct, actor continuous, TDML records the switch. Add a dev-only hook (menu/inspector button "load context X") to Play-test a context without behaviorMate.

### v1 status — DONE, switching live on the rig (2026-07-16)
All 7 steps implemented; A→B switch at lap 5 verified live (behaviorMate start/stop drives the Gimbl re-render).
- **GimblEnv** (`acumpelik/GimblEnv`, branch `mouse_VR`): `Assets/Scripts/VRContextBuilder.cs` (runtime .vr→subtree), `VRContext.cs` (handle), `ContextManager.cs` (preload + `SwitchTo` toggle + skybox + repoint `LinearTreadmill.path` + track-length guardrail + builds at world origin), `VRContextLoader.cs` (solo dev tester); `Assets/Editor/ContextManagerEditor.cs` (inspector Switch buttons) + `VRContextImporter.cs` (thin Editor caller of the builder). Rig test scene `Assets/empty_corridor.unity`.
- **GimblFork** (`acumpelik/GimblFork`, branch `behavior-mate`): `Scripts/BehaviorMate/BehaviorMateReceiver.cs` raises `event ContextMessage` for any `action` datagram; `ContextManager` subscribes and filters VR vs reward.
- **behaviorMate** (`mcum96/behaviorMate_015_USB`, branch `main` — the custom serial-capable USB build, now on GitHub): `VrContextList2` (`class:"vr2"`) made control-only (start msg carries `vr_file`; no `vr_config`/cue streaming) + TDML logs `lap`+`vr_file`. Config `settings_vr_switch_AB.json` (A laps 0–4, B lap 5+, 0-indexed). Rebuild jar via `behaviorMate_src/build.ps1` (Windows) or `make all` (unix).
- **Known v1 gotcha (→ motivates §7.1):** `ContextManager`'s `Preload On Start` ids + `vr_file` paths must EXACTLY match behaviorMate's config, or the switch silently no-ops (behaviorMate GUI shows it, Gimbl doesn't re-render).

---

## 5. CLAUDE.md ×4 (after v1)

**When:** right after v1 lands — design is stable and integration seams are real, so per-repo docs are accurate. One `CLAUDE.md` each for **behaviorMate, GimblFork, GimblEnv, vrMate** (build/run steps, layout, conventions, gotchas). Bootstrap with `/init`, then fill in. These are committed/team-facing (bonus: onboarding). Cross-repo integration knowledge stays in memory + these handoff docs, not in a single repo's CLAUDE.md.

---

## 6. SOPs ×2 (after the CLAUDE.md files)

*Concrete button names / config line numbers get finalized here, once the system is built. Outlines below.*

### SOP 6a — From-scratch setup (Gimbl + behaviorMate on a new PC)
- **Hardware:** wheel/rotary encoder, Arduino Due (valves + lickport), monitors, wiring.
- **Unity:** install Hub + Editor **2023.1.22f1**; clone `GimblEnv` (`mouse_VR`) + `GimblFork` (`behavior-mate`); confirm `"gimbl":"file:../../GimblFork"` package link.
- **behaviorMate:** Java runtime + the **custom serial-capable** `BehaviorMate.jar` (not the public GitHub jar); flash `behavior_controller_usb_due.ino`; find the Due's COM port.
- **Network:** static IPs (`position_controller` 192.168.1.102); ports **4020/4021** free on the rig PC.
- **Config + assets:** place `settings_vr_reward.json` and the `vr_contexts/` folder.
- **Calibration:** belt / `position_scale`, `track_length`, and Gimbl `GeometryScale`/`positionScale` = 0.3333.
- **Smoke test:** wheel → position → actor moves; one reward fires.

### SOP 6b — Day-to-day operation (everything installed)
- **Power on / check:** hardware connected, encoder + Due alive (see the `behaviormate-rig-recovery-checklist` memory when not).
- **Open behaviorMate:** which jar, load the session config, enter mouse ID.
- **Open Unity:** open the **rig scene**, press **Play** (Unity-first).
- **Run:** start the trial; what to watch (position, lap count, licks, reward).
- **End:** stop; TDML lands at `<datapath>/<mouse>/<mouse>_<timestamp>.tdml`.
- **Subsection — context switching (which lines to edit):**
  - the `contexts[]` VR entries (`id`, `vr_file`) for A and B,
  - the `ScheduledContextDecorator` `lap_list` that sets the boundary (e.g., A laps 1–20, B 21+),
  - the same-`track_length` requirement across switched contexts,
  - copy-paste config snippet (added once the format is locked in v1).

---

## 7. v2 — Auto-announce, enforcement, trigger variants

The v1 seam most in need of fixing: Gimbl's `ContextManager` requires the operator to mirror behaviorMate's context ids + `vr_file` paths into its `Preload On Start` list. That duplication is a **v1 crutch** with a **silent** failure mode — hit live 2026-07-16: stale paths / mismatched ids on the Gimbl side meant behaviorMate's start/stop arrived but `HandleContextMessage` ignored them (unknown id), so behaviorMate's GUI showed the switch while Gimbl kept rendering the old context. That is exactly the "wrong environment" error class this project exists to eliminate, so it leads v2.

### 7.1 Auto-announce the context set (removes the Gimbl-side list)
behaviorMate already knows the full set (ids + `vr_file`s) from its config; Gimbl shouldn't need them re-entered.
- **behaviorMate:** in `VrContextList2.trialStart()`, emit one announce per VR context — `{"action":"preload","context":"<id>","vr_file":"<path>"}` (replaces the `vr_config` broadcast removed in v1 step 4). Each `vr2` context announces itself.
- **Gimbl:** add a `"preload"` case to `ContextManager.HandleContextMessage` → `Preload(id, vr_file)` (the method already exists). Contexts build upfront, disabled → instant switching, no build hitch, **zero inspector config**.
- After this lands, empty the `Preload On Start` list. The only Gimbl-side setting left is `Vr Context Root` (where the `.vr`s live on the Gimbl disk); it too can go away if behaviorMate sends a path both machines resolve (shared/UNC/absolute).
- Note: v1 step 4 already carries `vr_file` on the **start** message, so a context lazy-preloads on first activation even without announce — announce just makes preloading **upfront, complete, and hitch-free**.

### 7.2 Ready-ack handshake (`4021`) — alignment from convention to enforcement
After preloading, Gimbl replies on `4021`: "loaded A, B". behaviorMate checks that against its config; on mismatch (missing file / wrong id / wrong set) it **warns or blocks the trial** rather than run the wrong world. This removes the *silent* property of the 7.1 / v1 failure: a mismatch becomes a loud refusal, not a no-op. Bonus: the TDML becomes **Gimbl-confirmed** — it records what actually rendered, not just what was commanded. (See the v2.3 appendix walkthrough.)

### 7.3 Trigger variants (behaviorMate config only; Gimbl unchanged)
1. **Time-based** — switch at elapsed time via behaviorMate's `TimedContextDecorator` / `DelayedContextDecorator`.
2. **First-of** — lap# *or* time, whichever comes first.
Both reuse the same start/stop messages, so there's no Gimbl-side work.

---

## 8. v3 — RNN / ML-Agents training path
1. **Gimbl-side schedule trigger** — self-driven switch rule reusing `ContextManager.SwitchTo` + `VRContextBuilder` (no behaviorMate in the loop).
2. **ML-Agents integration** — parallel training areas, headless runs, switch-cost/perf validation.
3. Confirm training-mode rendering is byte-identical to live-mode (same builder → should be free).

---

## Appendix — Operational walkthrough at v2.3 (handshake in place)

**Running (all on the rig PC unless noted):** rig hardware (encoder → `position_controller` Ethernet; Due → `behavior_controller` serial); behaviorMate (config loaded, sends on 4020, listens on 4021); Gimbl/Unity (rig scene, `BehaviorMateReceiver` + `ContextManager`, listens 4020, replies 4021); shared `vr_contexts/` folder.

1. Experimentalist picks the behaviorMate config (names contexts A/B, `vr_file`s, lap schedule, `track_length`, reward, mouse ID).
2. Opens the Gimbl rig scene, presses **Play** (Unity-first). Empty, listening.
3. Enters mouse ID, starts the trial.
4. **Handshake/preload:** behaviorMate announces the VR contexts (`{context, vr_file}`); Gimbl reads those `.vr`s, preloads both (disabled), **acks on 4021** ("ready — A, B"). behaviorMate checks the ack; on mismatch (missing file / wrong set) it **warns/blocks** — this kills the "wrong environment" error class.
5. **Start A:** `{action:start, context:A}` → Gimbl toggles A active, acks. behaviorMate logs `context A start @ lap 0`.
6. **Run:** encoder → behaviorMate → position broadcast → Gimbl moves the actor. behaviorMate counts laps.
7. **Lap 21 — switch:** `ScheduledContextDecorator` flips; behaviorMate sends `stop A` then `start B`; Gimbl toggles instantly (both preloaded), repoints the path, acks "showing B". behaviorMate logs `context B start @ lap 21`.
8. **End:** `stop`/`clear`; Gimbl deactivates; TDML closed. Record = which contexts, at which lap/time, each Gimbl-confirmed.

**What v2.3 adds over v1:** v1 steps 4–7 are fire-and-forget; v2.3 adds return acks so behaviorMate knows the right world loaded, can refuse a mismatch, and writes Gimbl-confirmed switch events. Same operator actions. Fully removing "press Play first" (auto-launch / behaviorMate waiting on the ack) is a later polish.
