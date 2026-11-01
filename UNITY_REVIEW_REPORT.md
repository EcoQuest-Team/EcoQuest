# Unity Project Review Report: EcoQuest

**Review Date**: November 1, 2026  
**Auditor**: Senior Unity Developer & Technical Reviewer  
**Project Path**: `C:\Users\ssopb\OneDrive\Documents\GitHub\EcoQuest-v1.1.4`  
**Report File**: `UNITY_REVIEW_REPORT.md` (Project Root)  

---

## Game Context

- **Game Name / Genre**: EcoQuest / 3D Educational First-Person Environmental Quest Game
- **Unity Version & Render Pipeline**: Unity 6 (`6000.3.11f1`), Universal Render Pipeline (URP `17.6.0`)
- **Target Platform(s)**: PC Standalone (Windows x86_64)
- **What the Game is Supposed to Do**: Educate players on environmental waste management through an interactive 3D world (Forest and City environments). Players complete basic tutorial movement, interact with NPC Guardians/Rangers, learn proper waste categorization (Biodegradable, Recyclable, Non-Biodegradable) via info cards and quizzes, and collect and sort scattered litter into designated bins with feedback and rewards.

---

## 1. Overall Summary

| Category | Score (/10) | Summary Assessment |
| :--- | :---: | :--- |
| **1. Project Structure** | **6.5 / 10** | Well-organized core under `Assets/EcoQuest`, but littered with root-level asset packs, singular/plural naming discrepancies (`Script` vs `Scripts`), leftover sample folders, and unneeded packages (`Multiplayer Center`, `Adaptive Performance`). |
| **2. Scripts (C#)** | **6.0 / 10** | Robust UI orchestration architecture (`UIOrchestrator`, `ModalManager`), but plagued by severe performance anti-patterns: `GameObject.FindGameObjectWithTag("Player")` and `FindFirstObjectByType` executing inside per-frame `Update()` methods across multiple gameplay scripts. |
| **3. Scenes** | **7.0 / 10** | Rich visual environments (Forest and City), but scenes have heavy hierarchies (>30k YAML lines), duplicate volumes and objects, and disabled scenes (`Beach.unity`) in Build Settings. |
| **4. Prefabs** | **7.0 / 10** | Solid UI prefab foundations (`KeyCapPrompt.prefab`, `PopupFrame`), but character prefabs (`Player`, `RangerVisual`) have unassigned animators, duplicate variations from asset store packs, and unpacked sub-hierarchies. |
| **5. Animation** | **6.0 / 10** | Frequent console warnings (`Animator is not playing an AnimatorController`) caused by scripts attempting to trigger parameters on GameObjects without valid runtime controllers. Procedural bone easing and Animator states compete directly. |
| **6. Assets (Textures/Audio/Models)** | **5.5 / 10** | Severe texture bloat in trash props (over 50 MB uncompressed for simple litter textures alone). Large audio clips configured as `DecompressOnLoad` (RAM spikes). Sound effects and background music are silent in `AudioManager` due to missing clip references. |
| **7. UI & Canvas** | **7.5 / 10** | High-quality responsive UI framework, custom font assets (`Fredoka SDF`, `Nunito SDF`), and kid-friendly design. Weaknesses include unnecessary `raycastTarget` on non-interactive visual text/frames and lack of uniform Canvas Scaler reference resolution across disparate canvases. |
| **8. Physics & Gameplay** | **7.0 / 10** | Good responsive first-person movement and pick-and-drop mechanics. Layer collision matrix is largely unoptimized (default 32x32 check-all), and carried props lack continuous collision sweeps to prevent wall clipping. |
| **9. Performance** | **6.0 / 10** | Major frame-time vulnerabilities: tag-search and type-search inside `Update()`, lack of texture compression on litter materials, unbatched trash objects, and non-pooled spawn/drop lifecycles generating GC garbage every frame. |
| **10. Settings** | **6.5 / 10** | `companyName` is left as `DefaultCompany`. Dual input backend (`activeInputHandler: 2`) runs both legacy Input Manager and new Input System simultaneously, increasing memory and CPU input polling overhead. |
| **11. Gameplay & Design Flow** | **8.0 / 10** | Clear, charming educational loop with sequential WASD onboarding, NPC interaction, quiz reinforcement, and gated quest progression. Recent UI flow fixes successfully eliminated softlocks and modal fighting. |
| **12. Security & Stability** | **5.0 / 10** | Save state (tutorial completion, seen cards, gold count, tab tip completion) relies entirely on unencrypted `PlayerPrefs` stored in the Windows Registry in plaintext. Easily manipulated; lacks atomic backup save files. |

---

## 2. Findings Table

| Severity | Category | File / Location | Problem | Suggested Fix |
| :--- | :--- | :--- | :--- | :--- |
| **Critical** | Scripts (C#) | `Assets/EcoQuest/Script/NPC Interaction.cs` (L177) | `GameObject.FindGameObjectWithTag("Player")` runs inside `Update()` every frame for distance checking. Causes major per-frame CPU overhead and string allocations. | Cache `playerTransform` in `Start()` or retrieve once via `Player` singleton. |
| **Critical** | Scripts (C#) | `Assets/EcoQuest/Script/TrashItem.cs` (L85) | `GameObject.FindGameObjectWithTag("Player")` executed in `Update()` across *every* active trash object on the map. | Cache player reference once globally or pass player transform during initialization. |
| **Critical** | Scripts (C#) | `Assets/EcoQuest/Script/NPCWaypointPointer.cs` (L41, L52) | Both `FindGameObjectWithTag("Player")` and `FindFirstObjectByType<SequentialWASDTutorial>()` executed every frame in `Update()`. | Cache references in `Awake()`/`Start()` and subscribe to tutorial completion events. |
| **High** | Assets | `Assets/EcoQuest/Asset/Trash Bin/Textures/` | `Pile_of_Trash_*.png` textures (Albedo, Normal, Metallic, Roughness) consume over 50 MB uncompressed on disk. | Downscale max resolution to 1024/2048 and enable URP BC7/DXT5 texture compression in texture import settings. |
| **High** | Animation | `Assets/EcoQuest/Script/NpcDialogueSpeakerAnimator.cs` (L210-L214) | Calls `animator.SetBool("IsTalking", false)` and `animator.ResetTrigger(...)` without checking `animator.runtimeAnimatorController != null`, flooding console with errors. | Add null guard: `if (animator != null && animator.runtimeAnimatorController != null)`. |
| **High** | Scripts (C#) | `Assets/EcoQuest/Script/CenterScreenCursor.cs` (L69) | `hit.collider.GetComponent<NPCInteraction>()` and `GetComponent<TrashItem>()` called every frame on raycast hit in `Update()`. | Use `hit.collider.TryGetComponent(...)` and filter by layer mask before querying components. |
| **High** | Scripts (C#) | `Assets/EcoQuest/Script/DragAndDrop.cs` (L231) | `FindFirstObjectByType<NPCInteraction>()` called inside `Update()` whenever dragging objects. | Cache `NPCInteraction` or query distance only on interaction input. |
| **High** | Settings | `ProjectSettings/ProjectSettings.asset` (L880) | `activeInputHandler: 2` (Both legacy Input and new Input System active). Double input overhead and memory usage. | Migrate remaining legacy `Input.GetKeyDown` calls to the new Input System actions, then switch `activeInputHandler` to `1` (Input System Package only). |
| **Medium** | Assets | `Assets/Loading Screen/Content/Art/Audio/ambience.wav` | 1.44 MB audio clip set to `loadType: 0 (DecompressOnLoad)`, loading full raw PCM into RAM. | Change Load Type in `.meta` to `Compressed In Memory` or `Streaming`. |
| **Medium** | Scripts (C#) | `Assets/EcoQuest/Script/PlayerSprintController.cs` (L29, L52, L74) | Uses deprecated `CinemachineVirtualCamera` (Cinemachine 3.x deprecation CS0618). | Upgrade type references to `Unity.Cinemachine.CinemachineCamera`. |
| **Medium** | Scripts (C#) | `Assets/EcoQuest/Script/AudioManager.cs` (L27-L31) | `defaultMusicClip`, `buttonClickClip`, `buttonHoverClip`, `sliderChangeClip` are unassigned; game UI is completely silent. | Assign audio assets in the AudioManager inspector or prefab. |
| **Medium** | Project Structure | `Packages/manifest.json` | Unused packages installed: `com.unity.multiplayer.center` (multiplayer tool for singleplayer game) and `com.unity.modules.adaptiveperformance` (no provider configured). | Remove unused package entries from `Packages/manifest.json`. |
| **Medium** | Project Structure | `Assets/` root directory | Inconsistent folder naming: `Script/` vs `Assets/EcoQuest/Script/`, `Prefab/` vs `Prefabs/`. Stray test folders: `SampleScene/`, `TutorialInfo/`. | Consolidate loose root scripts/prefabs into `Assets/EcoQuest/` or standard folders. Archive test folders. |
| **Medium** | Scenes | `ProjectSettings/EditorBuildSettings.asset` (L13-L15) | `Assets/EcoQuest/Beach.unity` is included in Build Settings but disabled (`enabled: 0`). | Remove disabled scene from build settings list or finish and enable it. |
| **Medium** | Settings | `ProjectSettings/ProjectSettings.asset` (L15) | `companyName: DefaultCompany` left unchanged. | Update company name to actual studio/developer identifier (e.g. `EcoQuest Studio`). |
| **Medium** | Security | `Assets/EcoQuest/Script/GameplayControlTipsManager.cs` / `TutorialManager.cs` | Game progress (`EcoQuest_TabTipDone`, `EcoQuest_SeenInfoCards`, gold) saved in unencrypted Windows Registry `PlayerPrefs`. | Implement simple JSON file persistence in `Application.persistentDataPath` with XOR/AES encryption or checksum hash. |
| **Low** | UI | `Assets/EcoQuest/Script/TutorialManager.cs` & `DialoguePresenter.cs` | Decorative frames, solid backings, and static text have `raycastTarget = true` enabled. | Disable `raycastTarget` on all non-interactive images and text to reduce GraphicRaycaster overhead. |
| **Low** | Scripts (C#) | `Assets/EcoQuest/Script/DialoguePresenter.cs` (L325) | Uses deprecated `TMP_Text.enableWordWrapping` property (CS0618). | Replace with `TMP_Text.textWrappingMode = TextWrappingModes.Normal;`. |
| **Low** | Physics | `ProjectSettings/DynamicsManager.asset` | Layer Collision Matrix has all layers colliding with all layers by default. | Uncheck collision pairs between static scenery, UI elements, and non-colliding triggers. |

---

## 3. Top 10 Priority Fixes

1. **Eliminate `GameObject.FindGameObjectWithTag("Player")` from `Update()`**  
   - *Impact*: Eliminates multiple expensive hierarchy scans every frame across `NPC Interaction.cs` (L177), `TrashItem.cs` (L85), and `NPCWaypointPointer.cs` (L41).
2. **Eliminate `FindFirstObjectByType` from `Update()`**  
   - *Impact*: Stops O(N) scene object searches per frame in `NPCWaypointPointer.cs` (L52), `DragAndDrop.cs` (L231), `ControlTipCardUI.cs` (L557), and `LitterbugQuestManager.cs` (L492).
3. **Fix `Animator is not playing an AnimatorController` Warning**  
   - *Impact*: In `NpcDialogueSpeakerAnimator.cs` (L210), guard calls with `if (animator != null && animator.runtimeAnimatorController != null)` to eliminate recurring console warning spam.
4. **Compress and Clamp Trash Textures**  
   - *Impact*: Reduces build and VRAM footprint by ~45 MB across `Pile_of_Trash_*` textures by clamping resolution to 1024x1024 and setting URP texture compression to Normal Quality (BC7/DXT5).
5. **Optimize `CenterScreenCursor.cs` Component Lookups**  
   - *Impact*: Replace `GetComponent<NPCInteraction>() != null` and `GetComponent<TrashItem>() != null` in `CenterScreenCursor.cs` (L69) with `hit.collider.TryGetComponent(...)` and tag pre-filtering to prevent per-frame garbage.
6. **Switch to Pure Input System Package (`activeInputHandler: 1`)**  
   - *Impact*: Disables the legacy Input Manager backend in `ProjectSettings.asset`, removing duplicate input polling overhead.
7. **Change Large Audio Load Types to Compressed/Streaming**  
   - *Impact*: In `Assets/Loading Screen/Content/Art/Audio/ambience.wav`, change load type from `DecompressOnLoad` to `CompressedInMemory` to prevent an unnecessary 10+ MB uncompressed memory spike at launch.
8. **Assign Core Audio Clips in `AudioManager`**  
   - *Impact*: Assign audio clips to `defaultMusicClip`, `buttonClickClip`, and `buttonHoverClip` in `AudioManager.cs` to restore missing game audio feedback.
9. **Upgrade Deprecated Cinemachine 3.x References**  
   - *Impact*: In `PlayerSprintController.cs` (L29, L52, L74), replace `CinemachineVirtualCamera` with `CinemachineCamera` to clear CS0618 warnings in Unity 6.
10. **Clean Up Build Settings & Unused Packages**  
    - *Impact*: Remove disabled `Beach.unity` from `EditorBuildSettings.asset` and remove `com.unity.multiplayer.center` from `Packages/manifest.json`.

---

## 4. Quick Wins (< 10 Minutes)

1. **Fix `NpcDialogueSpeakerAnimator` Controller Guard**  
   - Wrap lines 210–214 of `Assets/EcoQuest/Script/NpcDialogueSpeakerAnimator.cs` with `if (animator != null && animator.runtimeAnimatorController != null)`.
2. **Update Company Name in ProjectSettings**  
   - In `ProjectSettings/ProjectSettings.asset`, replace `companyName: DefaultCompany` with your studio name.
3. **Clean Up `Packages/manifest.json`**  
   - Remove `"com.unity.multiplayer.center": "2.0.1",` from `Packages/manifest.json`.
4. **Remove Disabled Beach Scene from Build Settings**  
   - Open File > Build Profiles (or `EditorBuildSettings.asset`) and remove the unneeded `Beach.unity` entry.
5. **Fix Deprecated TMP Wrapping Property**  
   - In `Assets/EcoQuest/Script/DialoguePresenter.cs` line 325, replace `enableWordWrapping = true` with `textWrappingMode = TextWrappingModes.Normal`.
6. **Set Texture Max Size on Large Litter PNGs**  
   - In the Project window, select `Assets/EcoQuest/Asset/Trash Bin/Textures/` PNGs, set Max Size to 1024 or 2048, and hit Apply.
7. **Change Ambience Audio Load Type**  
   - Select `Assets/Loading Screen/Content/Art/Audio/ambience.wav`, change Load Type to `Compressed In Memory`, and hit Apply.

---

## 5. What's Done Well

1. **Centralized UI Orchestration**: `UIOrchestrator.cs` and `ModalManager.cs` provide a clean, single-authority model for managing HUD profiles, eliminating modal overlap and conflicting UI states.
2. **Kid-Friendly Visual Feedback**: Custom 3D keycaps (`KeyCapPrompt`), gold step banners, checkmark animations, and distinct color-coded waste bins (Green Compost, Blue Paper, Red Plastic/Metal) create strong educational affordances.
3. **Automated Diagnostic & Matrix Test Infrastructure**: Tools like `ApplyUIFixes.cs`, `UIPhaseMatrixTestRunner.cs`, and `BuildStampDiagnostics.cs` demonstrate great testing discipline and allow rapid verification of all 15 UI flow requirements.
4. **Clean Presentation Architecture**: `DialoguePresenter.cs` provides typewriter effects, speaker tabs, and robust auto-sizing without clipping body text.
5. **Gated Progression Sequence**: Ensuring trash on the map remains inactive until the player completes the TAB tutorial prevents overwhelm and guides players naturally through the game mechanics.

---

## 6. Questions for Me

1. **Input System Strategy**: Are there remaining dependencies requiring the legacy Input Manager (`Input.GetKeyDown`), or can we transition `activeInputHandler` entirely to the new Input System (`activeInputHandler: 1`)?
2. **Beach Scene Status**: Is `Assets/EcoQuest/Beach.unity` an upcoming level to be enabled, or is it a retired prototype that can be safely deleted or archived?
3. **Audio Strategy**: Do you have custom BGM and UI sound assets ready to plug into `AudioManager`, or should a lightweight procedural audio/fallback synthesizer be configured?
4. **Save Data Requirements**: Is simple local progress sufficient for your target deployment (e.g. school lab computers / standalone demo), or do you require multi-profile save files and cloud/JSON persistence?
5. **Target Performance Specs**: What is the target minimum hardware specification (integrated graphics laptops, school desktops, or gaming PCs)? This will dictate whether we need aggressive LODs and mesh combining on the forest vegetation and city buildings.
