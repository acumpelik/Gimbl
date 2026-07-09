# Gimbl ↔ behaviorMate Integration — Handoff Brief

*Purpose: brief a Claude Code agent so it can help implement a Gimbl-based VR renderer driven by behaviorMate, replacing vrMate. Written after reading the source of behaviorMate, vrMate (mcum96 fork), upstream Gimbl, and Andrea's GimblFork.*

---

## 1. The scientific problem

A head-fixed mouse runs on a treadmill through a **virtual linear track**; we record neural activity while it traverses landmarks. Separately, a **simulated agent (ML-Agents, in Unity)** traverses the *same* virtual track, and its rendered visual stream is fed through a CNN→RNN that produces spatial predictions (a pixel-level world/predictive model). We compare the mouse's neural representation of the space to the RNN's.

**This comparison is only valid if the mouse and the RNN experience the *same* environment at the pixel level** — same engine, same geometry, same materials, same camera. That single requirement drives every design decision below.

## 2. The goal (this project)

Make **Gimbl render the mouse's VR, driven by the existing behaviorMate hardware pipeline, replacing vrMate** — starting with a **single screen** and a single linear track. Success = spinning the treadmill moves the mouse down a Unity/Gimbl corridor, live, driven by the real Arduino→behaviorMate stack, with the mouse landing correctly relative to authored landmarks.

Replacing vrMate is **required, not cosmetic**: the RNN renders in Gimbl (Unity 2023), so the mouse must render in the same project/engine or the pixels aren't comparable.

## 3. Settled architecture

```
  PHYSICAL / REAL MOUSE PATH
  Arduino (encoder, lick, valve)  ── serial/UDP ──▶  behaviorMate (Java)
                                                        │  UDP:4020 JSON  {"position":{"y":<cm>}}
                                                        ▼
                                          Gimbl project (Unity 2023, Play mode)
                                          ├─ NEW: BehaviorMateReceiver (UDP → actor position)
                                          ├─ Shared Unity-authored linear scene  ◀── single source of truth for geometry
                                          ├─ Shared "mouse-eye" flat camera       ◀── the canonical stimulus
                                          └─ Actor (mouse)

  SIMULATED / RNN PATH  (Andrea's existing work — DO NOT BREAK)
  ML-Agents Agent ──▶ same Actor ──▶ same scene ──▶ same eye-camera ──▶ CNN/RNN
```

**Division of responsibility:**

| Layer | Owns |
|---|---|
| **Unity + Gimbl** | Environment geometry, actor, camera rig, rendering, multi-display (later). The **environment is authored once in Unity** and shared by mouse and RNN. |
| **behaviorMate (unchanged)** | Reads Arduino, computes position, delivers reward/lick via Arduino, logs everything in the lab's database format. **It only sends *position* to the renderer now — it does NOT define geometry.** |
| **ML-Agents (Andrea's, in GimblEnv)** | Drives the same actor for simulated runs. Not part of this task; must remain functional. |

The actor is driven by **one of two interchangeable sources**: `BehaviorMateReceiver` (real mouse) or the ML-Agents `Agent` (sim). Same actor, same scene, same camera → environment identical by construction.

## 4. Key assumptions & decisions (treat as fixed unless told otherwise)

- **All tracks are linear.** Position reduces to a single scalar "distance along the corridor."
- **Environment is authored in Unity** (Andrea builds tracks from Gimbl's prefab library), NOT streamed from behaviorMate. The receiver therefore **ignores** behaviorMate's `editContext`/`cue`/`scene`/`skybox`/`fog`/`view` messages — it only consumes `position`. (Optionally log the rest.)
- **RNN stimulus = one flat, un-warped Unity perspective camera** at the mouse's eye position, spanning the mouse's field of view. No cylindrical projection, no off-axis, no split. This single camera is the shared ground-truth render for both mouse and RNN; the physical 3-screen warped rig is a separate display-layer concern that does not touch the RNN pipeline. FOV is a tunable value.
- **No mid-session environment switching.** Environment is chosen (scene loaded) at session start. Different environments across recordings = different Unity scenes.
- **Reward is landmark-triggered, not visual.** For the mouse, reward stays entirely behaviorMate↔Arduino. For the RNN (future), reward is an RL term referencing landmark positions. Nothing about the scene changes visually for reward.
- **Runs in Unity Editor Play mode** (Andrea never produces a standalone build; compiling issues). The receiver must work in Play mode. Gimbl's multi-display is also Editor-based, so "never building" is viable.
- **Landmark positions are the real integration seam.** behaviorMate's reward-zone positions and the Unity scene's landmark positions must come from one shared source of truth so the mouse (and RNN reward) trigger at the right place.

## 5. Technical contract

### behaviorMate → renderer (UDP)
- Transport: UDP, one JSON object per datagram, ASCII. behaviorMate sends to each configured display controller's `ip:send_port`. **Renderer listens on port `4020`** (default). No handshake, no reply expected (vrMate is receive-only).
- Config lives in behaviorMate's `settings.json` under `controllers.display_1 = {ip, send_port:4020, receive_port}`. Point `ip` at the Gimbl machine (`127.0.0.1` if same PC). **No behaviorMate code change needed — settings only.**
- **The only message we consume:**
  ```json
  {"position":{"y":<float>}}
  ```
  `y` = **absolute** position along the track, in **cm**, single scalar (x/z never sent). Emitted every loop iteration the mouse moves (~up to ~1 kHz, movement-gated).
- Messages we ignore (geometry now lives in Unity): `{"action":"editContext",...}` (cue/scene/skybox/filter), `{"action":"start|stop|clear",...}`, `{"view":{...}}`, `{"fog":{...}}`.

### Position → actor (copy vrMate's proven mapping)
vrMate applies an **axis swap**: message `y` (track) → **Unity Z**; message `z` (altitude) → Unity Y; message `x` → Unity X. Raw floats as world units (no scale factor in vrMate; environment scaled to match). Position is **absolute (set), not incremental**.

### Reference implementation to port
vrMate (mcum96 fork), `Assets/Scripts/`:
- `Communicator.cs` — `UdpClient` on background thread → `ConcurrentQueue<string>`; ASCII decode.
- `Controller.cs` — drains queue in `Update()`, `JSON.Parse` (SimpleJSON), dispatches on top-level key. Copy the `position` branch; skip the rest.
- `MouseController.cs` — `SetX/SetY/SetZ` doing the axis swap onto the "Mouse" rig transform.
- `Assets/Plugins/SimpleJSON.cs` — bring this file into Gimbl; Unity's `JsonUtility` handles behaviorMate's nested/dynamic JSON poorly.

### Injecting position into Gimbl
Gimbl has **no UDP layer — MQTT only**. Do NOT route the mouse through MQTT. Add a new `MonoBehaviour` (`BehaviorMateReceiver`) that opens the UDP socket directly and sets the Gimbl actor's transform. Gimbl already has a precedent for direct transform-setting: `GimblObject.MoveTo` sets `transform.position` from `{"array":[x,y,z]}`. Follow that pattern but source the value from UDP. Bypass Gimbl's `LinearTreadmill`/PathCreator machinery (fine for a straight corridor).

### Threading
UDP receive on a background thread → `ConcurrentQueue<string>` → **drain and apply on the main thread in `Update()`** (Unity transform writes must be on the main thread).

## 6. Repositories (all cloned locally)

| Repo | Role | Unity | Notes |
|---|---|---|---|
| **behaviorMate** (losonczylab) | Hardware brain (Java). Unchanged. | — | Meghan runs a **custom USB/serial jar** (public jar lacks serial). Protocol details in §5. |
| **vrMate** (mcum96 fork) | **Reference only** — the renderer we're replacing. | 2019.2.10f1 | Source of the UDP/position pattern to port. Also contains the multi-display fork (`MultiDisplayController.cs`, `displays.json`) for the *later* 3-screen phase. |
| **GimblFork** (acumpelik) | Andrea's Gimbl port; safe to prototype against. | 2023.1.22f1 | = upstream Gimbl + Unity-2023 deprecation fixes + null guards + `#if UNITY_EDITOR` wrappers. **No ML-Agents in it.** |
| **GimblEnv** (acumpelik, PRIVATE) | **The real target.** Has Andrea's ML-Agents setup + authored linear scenes. | 2023.x | Access pending. Build the receiver against GimblFork conventions so it drops in cleanly. |

ML-Agents is **not** part of Gimbl — it's a separate Unity package + Agent code Andrea added, living in GimblEnv. "Preserve simulation ability" = build additively on GimblEnv, don't touch her ML-Agents code.

## 7. Hurdles, limitations & risks

1. **Gimbl wants an MQTT broker on boot.** `MQTTClient`/`LoggerObject` connect to `127.0.0.1:1883` on Play; with no broker, M2MQTT can throw. Our hardware path uses UDP, not MQTT, so the broker is only needed to keep Gimbl's internals happy. **Fix:** either run a local Mosquitto broker (≈10 min, leave it idle) or stub/guard `MQTTClient.Connect`. Andrea's null guards help but may not fully prevent the connect exception.
2. **Coordinate/scale calibration (the main real work).** behaviorMate emits cm; the Unity scene has its own scale. Must align them so "behaviorMate says 150 cm" ⇒ actor sits at the landmark authored at 150 cm. Depends on behaviorMate's `track_length`/`position_scale` and the scene's units. **Needs GimblEnv scene to finalize.**
3. **Landmark source-of-truth.** behaviorMate reward-zone positions and Unity landmark positions must match. No mechanism enforces this yet — define one shared table/config.
4. **Port 4020 is single-owner.** vrMate, a vrMate-running Editor, and Gimbl cannot co-bind 4020. Kill vrMate before running Gimbl.
5. **Launch ordering.** Historically the renderer must be listening before behaviorMate starts a run. Make the receiver tolerant of starting mid-stream.
6. **Bypassing Gimbl's controller/path machinery.** Setting the transform directly means no PathCreator/gain/smoothing. Fine for straight linear tracks; revisit only if tracks ever curve (they won't, per assumptions).
7. **Cross-version asset reuse (mostly avoided now).** Because geometry is Unity-authored (not streamed), we no longer need to port vrMate's 2019 prefabs into 2023 — this deletes a big former risk. Only `SimpleJSON.cs` (pure C#) crosses over.
8. **Editor-only display code / no standalone build.** Fine for single-screen Play-mode prototype. Multi-screen off-axis (the `view` messages + Gimbl `PerspectiveProjection` + `FullScreenViews`) is a **separate later phase** and out of scope here.
9. **Blocking dependency: GimblEnv access.** Coordinate math and camera rig depend on Andrea's actual scene. Prototype on GimblFork meanwhile; mark scene-dependent values clearly.
10. **Multi-screen ≠ RNN concern.** The physical 3-screen warped rig is deferred to the display layer and never affects the RNN/prototype (single flat eye-camera is canonical).

## 8. Roadmap / goals

**Phase 0 — prerequisites (human):** obtain GimblEnv access; confirm the linear scene + actor/camera rig; produce the shared landmark table; resolve the MQTT-broker-on-boot choice.

**Day 1 — prove the pipe.** GimblFork/GimblEnv opens in Unity 2023 and enters Play mode; MQTT-on-boot handled. Add `BehaviorMateReceiver` (UDP:4020 → background thread → queue → `Debug.Log` each message) + `SimpleJSON.cs`. Point behaviorMate `settings.json` `display_1` at the Gimbl PC; kill vrMate. **Goal: real session's `position` messages stream into the Unity console.**

**Day 2 — position → moving camera (headline).** Parse `position.y`, apply to actor with vrMate's axis swap (msg `y` → Unity Z). Bare corridor to visualize motion. Single flat eye-camera, single Game view. **Goal: spin the wheel → mouse runs down the corridor in Gimbl, live, from the real rig.**

**Day 3 — load the real scene + calibrate.** Load one of Andrea's linear scenes; calibrate behaviorMate position → scene coordinates so the mouse lands on the authored landmarks. **Goal: mouse position is correct relative to real landmarks.**

**Day 4 — parity + docs.** Tolerate launch order / port cleanup; test lap reset. Confirm on-screen position matches behaviorMate's log and the landmark layout (proves lab compatibility is untouched). Document what works + the next-milestone punch list (multi-screen off-axis, reward RL, ML-Agents observation camera). **Goal: reproducible single-screen behaviorMate→Gimbl renderer.**

**Later phases (out of scope):** 3-screen off-axis display; RNN reward/RL referencing the landmark table; ensuring the ML-Agents observation camera == the canonical eye-camera.

## 9. Open questions for the human (the agent should ask / not guess)

- **GimblEnv coordinate system & camera rig:** origin, axis convention, world units, and how the actor + eye-camera are set up. (Determines the position mapping and calibration in Days 2–3.)
- **behaviorMate config:** current `track_length`, `position_scale`, and reward-zone positions for the target track (to build the shared landmark table).
- **Canonical camera FOV:** the horizontal FOV the flat eye-camera should use.
- **MQTT-on-boot preference:** run a local Mosquitto broker, or stub Gimbl's MQTT client?
- **Andrea's scenes:** are the linear scenes committed in GimblEnv, and is the actor driven by a component the receiver should write to (so mouse and agent share one injection point)?
