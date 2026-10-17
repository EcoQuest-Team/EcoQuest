# EcoQuest — Game Overview

## 1. Game Summary
- **Genre:** 3D first-person educational adventure & waste-segregation puzzle game.
- **Target Audience:** Children (elementary/middle school, approx. Grades 4–6).
- **Goal:** Learn the difference between **Biodegradable**, **Recyclable**, and **Non-Biodegradable** waste through a guided tutorial, a Pre-Test, infographic lessons, a 3D forest cleanup quest (sorting 9 pieces of trash into the proper bins while minimizing mistakes to earn Gold), and a Post-Test that measures learning improvement.
- **Full Player Flow (Main Menu to Ending):**
  1. **Main Menu (`Assets/EcoQuest/Main Menu.unity`):** Player clicks `Play Button` (`Start Game`), triggering the `SceneTransition` leaf-wipe and loading screen with random kid-friendly eco tips.
  2. **Onboarding Control Tips (`SequentialWASDTutorial`):** In `Forest Scene`, a 5-step interactive card sequence (`Control Tip Banner`) teaches movement (`WASD`), jumping (`Space`), sprinting (`Shift`), unlocking the mouse (`Ctrl`), and following the 3D overhead gold arrow to the Guardian NPC.
  3. **Guardian Dialogue (`NPCInteraction`):** Player presses `E` near `GuardianNPC`. Camera smoothly blends to `DialogueCameraPoint` while the Guardian explains the three waste types, mistake penalties, and the 9-item cleanup quest. Player clicks `Accept Mission`.
  4. **Pre-Test (`QuizManager.StartPreQuiz`):** A 6-question baseline quiz (`UI_Quiz`) runs without per-question answer reveals to measure prior knowledge.
  5. **Lesson Infographic Cards (`TutorialManager`):** 3 full-screen educational cards (`Tutorial_Card (1)` to `(3)` in `UI_TutorialCards`) teach what belongs in Recyclable, Biodegradable, and Non-Biodegradable bins.
  6. **Quest HUD & Tab Tip (`GameplayControlTipsManager`):** The 9 trash items (`TrashItemsGroup`), minimap trash icons, and Quest/Coins HUD activate; a short tip teaches pressing `Tab` to hide/show the Quest panel.
  7. **Waste-Sorting Quest (`QuestManager` & `DragAndDrop`):** Player explores the forest, picks up the 9 scattered trash items, and drops them into matching category bins (`5` Biodegradable, `3` Recyclable, `1` Non-Biodegradable). Completing all 9 items awards `100–149` Gold.
  8. **Post-Test & Ending Results (`QuizManager.StartPostQuiz`):** Automatically launches after the 9th item is sorted. Displays immediate per-question feedback (`CORRECT!` / `NOT QUITE!` + explanation) and awards `+5` Gold per correct answer, then ends on the Comparative Results screen (`ResultsPanel`) showing Pre-Quiz vs. Post-Quiz scores, point improvement, animated reward gift, and a `Continue Playing` button to return to free-roam.

---

## 2. Scenes
- **`[0] Assets/EcoQuest/Main Menu.unity` (Enabled in Build Settings):**
  - Displays the title screen (`Main Menu` Canvas, `MenuBackgroundCanvas`), ambient parallax/particles/critters (`Main Menu/MenuFX`), `Menu Manager` (`MainMenuManager`), and persistent `SceneTransition` manager.
  - Clicking `Main Menu/Play Button` calls `SceneTransition.LoadScene("Forest Scene")` (and `MainMenuManager.PlayGame()` also calls `SceneTransition.Instance.LoadScene("Forest Scene")` with fallback to `SceneManager.LoadScene`).
- **`[1] Assets/EcoQuest/Forest Scene.unity` (Enabled in Build Settings):**
  - Main gameplay scene organized under five root groups: `--- ENVIRONMENT ---`, `--- GAMEPLAY ---`, `--- MANAGERS ---`, `--- CAMERAS ---`, and `--- UI ---`.
  - Contains the full onboarding, NPC dialogue, Pre-Quiz, Lesson cards, 9-item waste-sorting quest, and Post-Quiz flow. Has no scene-exit transition back to `Main Menu.unity`.
- **`[2] Assets/EcoQuest/Beach.unity` (Disabled in Build Settings):**
  - Prototype/unfinished sandbox scene containing `Terrain`, pirate-themed props (`house-pirate-*`, `dock-wood`, `raft`, `ship-pirate-flags`), and a basic first-person player setup (`PlayerCapsule`, `MainCamera`, `PlayerFollowCamera`). Not connected to any scene transition.
- **Additional Non-Build Demo Scenes (Third-Party Assets):**
  - `Assets/EcoQuest/Asset/LowPolyMegapolis/Scenes/LowPolyMegapolis_Demo.unity` (referenced by editor scripts in `Assets/EcoQuest/Script/Editor/`), plus demo scenes under `Assets/LowPoly Environment Park/`, `Assets/Trash_Pack/`, `Assets/Loading Screen/`, and `Assets/StarterAssets/`.

---

## 3. Core Mechanics
- **Player Controller & Movement:**
  - Controlled by `StarterAssets.FirstPersonController` (`Assets/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs`) and `StarterAssetsInputs` on `--- GAMEPLAY ---/Player/PlayerCapsule`.
  - Inspector settings in `Forest Scene`: `MoveSpeed = 4`, `SprintSpeed = 6` (overridden at runtime to `4 * 1.6 = 6.4`), `RotationSpeed = 1`, `JumpHeight = 1.2`, `Gravity = -15`.
  - `PlayerSprintController` (`Assets/EcoQuest/Script/PlayerSprintController.cs`) multiplies `MoveSpeed` by `1.6x` while holding `Shift` + moving grounded, and smoothly bumps `MainCamera` FOV by `+5` (`fovTransitionSpeed = 8`).
- **Camera:**
  - `--- GAMEPLAY ---/Player/MainCamera` has a `CinemachineBrain` driven by `PlayerFollowCamera` (`CinemachineCamera`) parented to `PlayerCapsule/PlayerCameraRoot`.
  - During NPC dialogue, `NPCInteraction` activates a temporary `CinemachineCamera` at `DialogueCameraPoint` (`Priority = 30`, `FOV = 55`, `0.7s` ease-in-out blend) and hides the player mesh.
  - Top-down minimap camera at `--- CAMERAS ---/MinimapCamera` uses `MinimapController` (`Assets/EcoQuest/Script/MinimapController.cs`) at `cameraHeight = 40f`, rotating with player yaw (`player.eulerAngles.y`).
- **Interaction & Cursor:**
  - `CenterScreenCursor` (`Assets/EcoQuest/Script/CenterScreenCursor.cs`) procedurally builds a `DontDestroyOnLoad` overlay canvas (`CenterCursorCanvas`, `sortingOrder = 999`) with a center dot that scales from `12px` white to `18px` green (`#33FF66`) when raycasting within `8m` at an NPC or trash item.
  - `TrashHoverUI` (`Assets/EcoQuest/Script/TrashHoverUI.cs`) on `MainCamera` raycasts up to `5m` and shows the hovered trash item's name in `--- UI ---/UI_TrashTooltip`.
  - `DragAndDrop` (`Assets/EcoQuest/Script/DragAndDrop.cs`) on each trash item raycasts from the screen center `(0.5, 0.5)` on Left Mouse Button down (`pickupRange = 5m`), disables gravity on the item's `Rigidbody`, and moves it via `rb.MovePosition` to `holdDistance = 2m` in front of `Camera.main` until mouse release.
  - `CursorQuickAccessController` (`Assets/EcoQuest/Script/CursorQuickAccessController.cs`) unlocks the cursor while holding `LeftControl`/`RightControl` during gameplay so players can click UI elements.
- **Waste-Sorting System, Bins & Items:**
  - **9 Trash Items (`--- GAMEPLAY ---/TrashItemsGroup`):** Each has `TrashItem` (`Assets/EcoQuest/Script/TrashItem.cs`), `DragAndDrop`, `Rigidbody`, collider, and a runtime `MinimapIcon` (`IconType.Trash`):
    - **Biodegradable (5 items, tag `Biodegradable`):** `Banana Peel`, `Fish Bone`, `Sandwich`, `Sliced Cake`, `Cupcake`.
    - **Recyclable (3 items, tag `Recyclable`):** `Old Paper`, `Plastic Cup`, `Toiler Paper`.
    - **Non-Biodegradable (1 item, tag `NonBiodegradable`):** `Can `.
  - **9 Trash Bins (`--- GAMEPLAY ---/TrashBinsGroup`):** 3 sets of category bins placed around the forest (`Biodegradable`, `Biodegradable 2`, `Biodegradable 3`; `Recyclable`, `Recyclable 2`, `Recyclable 3`; `NonBiodegradable`, `NonBiodegradable 2`, `NonBiodegradable 3`). Each bin has `QuestManager` (`Assets/EcoQuest/Script/Quest Manager.cs`) and `TrashBin` (`Assets/EcoQuest/Script/TrashBin.cs`).
  - `QuestManager` shares static dictionaries across all bins (`currentCounts`, `targets`, `progressBars`, `countLabels`, `collectedTrash`) so dropping an item into any bin of that category increments the shared category total (`Biodegradable: 5`, `Recyclable: 3`, `NonBiodegradable: 1`).
  - Category matching checks both Unity tag and item-name substring fallbacks (`IsItemMatchingCategory` in `Quest Manager.cs`).
- **Mistake Counter, Gold & Rewards:**
  - **Correct drop:** Item is deactivated (`SetActive(false)`), added to `collectedTrash`, category progress bar and `CountText` (`current/target`) update.
  - **Wrong drop:** `WrongTrashPopup.Instance.Show()` displays an animated warning banner (`--- UI ---/UI_Notification/WrongTrashPopup`), forces `DragAndDrop.ForceRelease()`, ejects the item away from the bin via `Rigidbody.AddForce`, and increments static `QuestManager.mistakes` (with a `0.5s` cooldown).
  - **Mistake Penalty:** Every `3` mistakes (`mistakes >= 3`), `PlayerCurrency.DeductGold(10)` subtracts `10` Gold (clamped at `0` minimum) and resets `mistakes` to `0`.
  - **Quest Completion & Quiz Rewards:** Sorting all `9` items sets `isQuestComplete = true`, hides `UI_Quest`, awards `Random.Range(100, 150)` Gold via `PlayerCurrency.AddGold`, and starts the Post-Quiz (which awards an additional `+5` Gold per correct answer).
- **Quest Flow Coordinator (`IntroSequenceController`):**
  - Attached to `--- MANAGERS ---/GameManager` (`Assets/EcoQuest/Script/IntroSequenceController.cs`).
  - Tracks states (`Gameplay`, `Dialogue`, `BinIntro`), hides `TrashItemsGroup` and `UI_Quest` until `TutorialManager.OnTutorialClosed` fires, and locks/unlocks `FirstPersonController` and cursor state across transitions.

---

## 4. Quiz System
- **Where Questions Are Stored:**
  - Stored in the `QuizData` ScriptableObject (`Assets/EcoQuest/Script/QuizData.cs`) asset at `Assets/EcoQuest/QuizData.asset`.
  - Contains **6 questions** (`QuizQuestion` structs with `questionText`, 3–4 `options`, `correctOptionIndex`, and 1-line `explanation`):
    1. *Which bin does a Banana Peel belong in?* (`Biodegradable`)
    2. *Which bin should you place Old Paper into?* (`Recyclable`)
    3. *According to the Guardian, what is biodegradable waste?* (`Stuff that can rot and turn back into soil`)
    4. *What are non-biodegradable materials according to the info cards?* (`Synthetic materials that cannot break down naturally in the environment`)
    5. *Which of these items belongs in the Biodegradable bin?* (`Leftover sandwich and food scraps`)
    6. *According to the Guardian, what happens if you make 3 sorting mistakes?* (`Some Gold is deducted to pay for the cleanup`)
- **Pre-Test, Lessons, and Post-Test Flow (`QuizManager`):**
  - `QuizManager` (`Assets/EcoQuest/Script/QuizManager.cs` on `--- MANAGERS ---/QuizManager`) shuffles answer options on every run via Fisher-Yates (`PrepareShuffledQuestions`) while keeping question order fixed.
  - **Pre-Test (`StartPreQuiz`):** Triggered immediately after Guardian NPC dialogue ends. Player selects an answer and clicks `Confirm Answer`. No correctness colors or explanations are shown; advances after `0.3s`. Upon finishing all 6 questions, opens `TutorialManager` (the 3 Lesson infographic cards).
  - **Lessons (`TutorialManager`):** `Assets/EcoQuest/Script/TutorialManager.cs` on `--- MANAGERS ---/GameManager` displays `Tutorial_Card (1)`, `(2)`, and `(3)` with `Time.timeScale = 0f`, advancing on Left Click, `Space`, `E`, or `Enter`.
  - **Post-Test (`StartPostQuiz`):** Triggered automatically by `QuestManager.CheckForWin()` when all 9 trash items are sorted. Confirming an answer locks buttons, highlights the chosen option green (correct) or red + horizontal shake (wrong) while highlighting the true answer green, awards `+5` Gold if correct, and slides in `FeedbackCard` (`CORRECT!` / `NOT QUITE!` + 1-line `explanation`) with a `Next Question ->` button.
- **Scoring & Improvement Logic:**
  - `preQuizScore` and `postQuizScore` each range from `0` to `6`.
  - `improvement = postQuizScore - preQuizScore` is displayed on `ResultsPanel`:
    - `improvement > 0`: Green text (`+{improvement} Points Improved!`) + `"Awesome job! You learned how to protect the forest!"`
    - `improvement == 0 && postQuizScore == total`: Green text (`Perfect Score Maintained!`) + `"Incredible! You are a true Master Forest Guardian!"`
    - `improvement == 0`: Gold text (`0 Points Change — Keep Practicing!`) + `"Good effort! Remember what each bin is for!"`
    - `improvement < 0`: Gold text (`{improvement} Points — Review the Info Cards!`) + `"Don't give up! Check the bin rules and try again!"`

---

## 5. UI
- **Main Menu Scene (`Assets/EcoQuest/Main Menu.unity`):**
  - `Main Menu` (`Canvas`, `ScreenSpaceOverlay`, `sortOrder = 0`): Title logo (`LOGO (1)`), buttons (`Play Button`, `settings`, `Quit` with `ButtonHoverFX.cs`), and ambient layers/particles/critters (`Main Menu/MenuFX` running `MenuParallax.cs`, `MenuAmbientFX.cs`, `MenuCritter.cs`).
  - `MenuBackgroundCanvas` (`Canvas`, `ScreenSpaceOverlay`, `sortOrder = 0`): Background canvas.
  - `SceneTransition` (`Canvas`, `ScreenSpaceOverlay`, `sortOrder = 2000`, `DontDestroyOnLoad`): Leaf-wipe transition, loading screen, progress bar, and eco tips (`SceneTransition.cs`).
- **Forest Scene (`Assets/EcoQuest/Forest Scene.unity` under `--- UI ---`):**
  - `UI_Quest` (`sortOrder = 0`): Top-right waste-sorting checklist (`QuestPanel`) with category counters (`CountText`), progress bars, `HudVisibility.cs`, and `QuestPanelToggleController.cs` (`Tab` toggle).
  - `UI_Coins` (`sortOrder = 0`): Gold counter (`coinText`) and floating `+`/`-` popup (`popupText`) controlled by `PlayerCurrency` (`Assets/EcoQuest/Script/Coins Reward.cs`) and `HudVisibility.cs`.
  - `UI_Minimap` (`sortOrder = 0`): Top-left circular/square minimap RawImage fed by `MinimapCamera`.
  - `UI_Notification` (`sortOrder = 20`) & child `WASDTutorial` (`sortOrder = 20`): Hosts `Control Tip Banner` (`ControlTipCardUI.cs`), `WASDTutorial` (`SequentialWASDTutorial.cs`), and `WrongTrashPopup` (`WrongTrashPopup.cs` + `CloseButtonFeedback.cs`).
  - `UI_InteractPrompt` (`sortOrder = 0`, initially inactive): World/screen prompt (`"[E] Talk to Guardian"` / `"Finish the tips first!"`) managed by `NPCInteraction.cs`.
  - `UI_Dialogue` (`sortOrder = 0`, initially inactive): Typewriter dialogue box, speaker name, `[SPACE] Continue` hint, and `Accept Mission` button managed by `NPCInteraction.cs`.
  - `UI_TutorialCards` (`sortOrder = 0`): Full-screen container (`TutorialOverlay`) with `Tutorial_Card (1)`, `(2)`, `(3)` managed by `TutorialManager.cs`.
  - `UI_Quiz` (`sortOrder = 45`): Pre-Quiz, Post-Quiz, per-question `FeedbackCard`, and Comparative `ResultsPanel` managed by `QuizUIController.cs` and animated by `QuizUIAnimator.cs`.
  - `UI_TrashTooltip` (`sortOrder = 0`, initially inactive): Hover name label for trash items managed by `TrashHoverUI.cs`.
  - `UI_Crosshair` (`sortOrder = 0`, initially inactive): Static crosshair canvas (superseded at runtime by `CenterScreenCursor`'s dynamic `CenterCursorCanvas` at `sortOrder = 999`).
- **Pause Menu:** None exists in the project.

---

## 6. Systems and Managers
- **`SceneTransition.Instance` (`Assets/EcoQuest/Script/SceneTransition.cs`):** Persistent `DontDestroyOnLoad` singleton handling async scene loading (`SceneManager.LoadSceneAsync`), leaf-wipe animations, and loading screen tips.
- **`MainMenuManager` (`Assets/EcoQuest/Script/Main Menu.cs` on `Menu Manager`):** Handles Main Menu button callbacks (`PlayGame`, `OpenSettings`, `CloseSettings`, `QuitGame`).
- **`IntroSequenceController.Instance` (`Assets/EcoQuest/Script/IntroSequenceController.cs` on `--- MANAGERS ---/GameManager`):** Central state coordinator (`Gameplay`, `Dialogue`, `BinIntro`). Subscribes to `TutorialManager.OnLastCardReached` and `OnTutorialClosed`, and is called directly by `NPCInteraction` (`BeginDialogueLock`, `HandleDialogueEnded`).
- **`SequentialWASDTutorial.Instance` (`Assets/EcoQuest/Script/SequentialWASDTutorial.cs` on `--- UI ---/UI_Notification/WASDTutorial`):** Runs the 5-step initial control onboarding, polling `StarterAssetsInputs`, `PlayerSprintController.Instance`, and `CursorQuickAccessController.Instance`, and unlocking `NPCInteraction` on Step 5.
- **`GameplayControlTipsManager.Instance` (`Assets/EcoQuest/Script/GameplayControlTipsManager.cs` on `--- MANAGERS ---/GameManager`):** Subscribes to `IntroSequenceController.OnIntroCompleted` and runs the post-tutorial `Tab` quest-panel toggle tip via `QuestPanelToggleController.Instance`.
- **`TutorialManager` (`Assets/EcoQuest/Script/TutorialManager.cs` on `--- MANAGERS ---/GameManager`):** Manages the 3 bin lesson cards and fires `OnLastCardReached` and `OnTutorialClosed` C# events.
- **`QuizManager.Instance` (`Assets/EcoQuest/Script/QuizManager.cs` on `--- MANAGERS ---/QuizManager`):** Orchestrates Pre-Quiz and Post-Quiz state machines, scores, and `QuizUIController` (`--- UI ---/UI_Quiz`).
- **`QuestManager` (`Assets/EcoQuest/Script/Quest Manager.cs` on all 9 bins in `--- GAMEPLAY ---/TrashBinsGroup`):** Multi-instance component sharing `static` state dictionaries for category counts, targets, UI bars, mistake count, and win detection; calls `PlayerCurrency`, `WrongTrashPopup.Instance`, and `QuizManager.Instance.StartPostQuiz()`.
- **`WrongTrashPopup.Instance` (`Assets/EcoQuest/Script/WrongTrashPopup.cs` on `--- UI ---/UI_Notification/WrongTrashPopup`):** Singleton notification controller for wrong-bin drops.
- **`CenterScreenCursor.Instance` (`Assets/EcoQuest/Script/CenterScreenCursor.cs`, auto-attached to `GuardianNPC` at runtime):** Singleton crosshair dot overlay.
- **AudioManager / SaveSystem:** Neither exists in the project.

---

## 7. Audio
- **AudioMixers & Mixer Groups:** None exist in the project (`0` `AudioMixer` assets found).
- **AudioClips in `Assets/EcoQuest`:** None (`0` `AudioClip` assets under `Assets/EcoQuest`; only third-party footstep/landing placeholder clips exist under `Assets/StarterAssets/`).
- **AudioSources in Scenes:**
  - `Main Menu.unity`:
    - `SceneTransition/StartSoundHook` (`AudioSource`, `clip = null`, `playOnAwake = false`, `loop = false`, `mixerGroup = null`).
    - `SceneTransition/WhooshSoundHook` (`AudioSource`, `clip = null`, `playOnAwake = false`, `loop = false`, `mixerGroup = null`).
    - `ButtonHoverFX` components on `Play Button`, `settings`, and `Quit` expose `[SerializeField] private AudioSource audioSource` (`null`) and `hoverSound`/`clickSound` (`null`) hooks.
  - `Forest Scene.unity`: `0` `AudioSource` components exist in the scene. (`FirstPersonController` references `FootstepAudioClips` and `LandingAudioClip` via `AudioSource.PlayClipAtPoint` when assigned).
- **Volume Controls:** No master, music, or SFX volume controls exist anywhere in the codebase.

---

## 8. Input
- **Input Handling Mode:** Project Settings (`ProjectSettings/ProjectSettings.asset`) sets `activeInputHandler: 2` (**Both** the new Input System package `com.unity.inputsystem` and the legacy `UnityEngine.Input` Manager are active simultaneously).
- **How Input Is Read:**
  - `PlayerCapsule` uses the **New Input System** via `PlayerInput` (`Assets/StarterAssets/InputSystem/StarterAssets.inputactions`, Action Map `Player`, behavior `SendMessages`) feeding `StarterAssetsInputs` (`Move`, `Look`, `Jump`, `Sprint`).
  - EcoQuest gameplay/UI scripts read **Legacy `UnityEngine.Input`** directly (`Input.GetKey`, `Input.GetKeyDown`, `Input.GetMouseButtonDown`, `Input.mousePosition`) alongside `StarterAssetsInputs`.
- **Key Bindings:**
  - `W / A / S / D` (or Arrow keys / Gamepad Left Stick): Move (`StarterAssets.inputactions` + `SequentialWASDTutorial.cs` + `ControlTipCardUI.cs`).
  - `Mouse Delta` (or Gamepad Right Stick): Camera look (`StarterAssets.inputactions`).
  - `Space`: Jump in gameplay (`StarterAssets.inputactions`); advance/skip typewriter dialogue in `NPCInteraction.cs`; advance lesson card in `TutorialManager.cs`.
  - `Left Shift` / `Right Shift`: Hold while moving to sprint (`StarterAssets.inputactions` + `PlayerSprintController.cs`).
  - `Left Ctrl` / `Right Ctrl`: Hold to unlock mouse cursor and pause camera look (`CursorQuickAccessController.cs`).
  - `E`: Talk to Guardian NPC (`NPCInteraction.cs`); advance lesson card (`TutorialManager.cs`).
  - `Left Mouse Button (Mouse 0)`: Grab and hold trash items (`DragAndDrop.cs`); advance lesson card (`TutorialManager.cs`); click UI buttons.
  - `Tab`: Hide/show the Quest panel (`QuestPanelToggleController.cs`).
  - `Enter / Return`: Advance lesson card (`TutorialManager.cs`).
- **Rebinding:** Not supported. No runtime key-rebinding UI or overrides exist, and many keys (`E`, `Tab`, `Ctrl`, `Shift`, `Space`, `W/A/S/D`) are hardcoded with `KeyCode` checks in scripts.

---

## 9. Saved Data
- **Storage Mechanism:** Unity `PlayerPrefs` only (no JSON, binary, or custom `SaveSystem` files are used by EcoQuest).
- **Where Saved & Keys Used:**
  - Written in `Assets/EcoQuest/Script/QuizManager.cs` via `PlayerPrefs.SetInt` and `PlayerPrefs.Save()` (stored in Windows Registry under `HKCU\Software\Unity\UnityEditor\<CompanyName>\<ProductName>` in Editor or `HKCU\Software\<CompanyName>\<ProductName>` in Standalone builds):
    - `"EcoQuest_PreQuizScore"` (`QuizManager.cs`, line 172): Stores `preQuizScore` (`int`) at the end of the Pre-Quiz.
    - `"EcoQuest_PostQuizScore"` (`QuizManager.cs`, line 300): Stores `postQuizScore` (`int`) at the end of the Post-Quiz.
- **Note on Persistence:** Neither `PlayerPrefs` key is ever read back via `PlayerPrefs.GetInt`. Gold (`PlayerCurrency.currentGold`), sorted trash progress (`QuestManager`), and tutorial completion states reset in memory every session.

---

## 10. Settings That Already Exist
- **Main Menu Settings Button (`Assets/EcoQuest/Script/Main Menu.cs`):**
  - `MainMenuManager` defines `public GameObject settingsPanel;` with `OpenSettings()` and `CloseSettings()` methods, and `Main Menu/settings` button invokes `MainMenuManager.OpenSettings()`.
  - However, `settingsPanel` is **unassigned (`null`)** on `Menu Manager` in `Assets/EcoQuest/Main Menu.unity` and no Settings UI panel exists in the scene.
- **Volume / Quality / Fullscreen / Language Settings:**
  - None are implemented in runtime UI or scripts.
  - Project-level defaults in `ProjectSettings/ProjectSettings.asset`: `FullScreenWindow` mode, default resolution `1024x768`.

---

## 11. Packages and Tools
- **Unity Version:** `6000.3.11f1 (3000ef702840)` (Unity 6.3 LTS) per `ProjectSettings/ProjectVersion.txt`.
- **Target Platform:** `StandaloneWindows64` (PC) per `EditorUserBuildSettings.activeBuildTarget`.
- **Render Pipeline:** **Universal Render Pipeline (URP)** (`com.unity.render-pipelines.universal: 17.6.0`).
- **Installed Packages (`Packages/manifest.json`):**
  - `com.unity.ugui: 2.6.0` (includes **TextMeshPro** `TMPro`).
  - `com.unity.inputsystem: 1.20.0` (Unity Input System).
  - `com.unity.cinemachine: 3.1.6` (Unity Cinemachine 3).
  - `com.unity.ai.navigation: 2.0.14` (NavMesh Navigation).
  - `com.unity.timeline: 1.8.11` & `com.unity.visualscripting: 1.9.12`.
  - `com.unity.ai.assistant: 2.0.0-pre.3` & `com.unity.mcp.server: 1.0.0`.
- **Tweening Library:** **No DOTween** or third-party tween package is installed; all UI and camera transitions use custom unscaled-time coroutines and `AnimationCurve` evaluation (`QuizUIAnimator.cs`, `ControlTipCardUI.cs`, `WrongTrashPopup.cs`, `HudVisibility.cs`, `SceneTransition.cs`).
- **Custom Editor Tools (`Assets/EcoQuest/Script/Editor/`):**
  - `AnimateEnvironmentTool.cs`, `FixVehiclesTool.cs`, `MovePlayerTool.cs`, `SpawnNPCsTool.cs` (utility menu items under `Tools/*` built for `LowPolyMegapolis_Demo.unity`).

---

## 12. Known Problems
- **Unassigned `settingsPanel` in Main Menu:**
  - On `Menu Manager` (`MainMenuManager` in `Assets/EcoQuest/Main Menu.unity`), `settingsPanel` is `null`. Clicking the `settings` button logs: `"You forgot to drag the Settings Panel into the MenuManager!"`.
- **Duplicate Bin Scripts (`QuestManager` + `TrashBin`) on All 9 Bins in `Forest Scene.unity`:**
  - Every bin under `--- GAMEPLAY ---/TrashBinsGroup` has both `QuestManager.cs` and legacy `TrashBin.cs` (`Assets/EcoQuest/Script/TrashBin.cs`) attached. If `TrashBin.acceptedTrashTag` matches a dropped item, `TrashBin.OnTriggerEnter` calls `Destroy(other.gameObject)` while `QuestManager.OnTriggerEnter` deactivates and stores the item in `collectedTrash` for `ResetQuest()`, risking destroyed references on reset.
- **Wrong Folder Path in `MinimapIcon.LoadIconTexture()`:**
  - `Assets/EcoQuest/Script/MinimapIcon.cs` (line 72) looks for minimap sprites at `Path.Combine(Application.dataPath, "EcoScenes", "Sprites", fileName)`, but the folder is actually `Assets/EcoQuest/Sprites/` (`npc_minimap_icon.png`, `trash_minimap_icon.png`). As a result, `File.Exists` always returns `false` and falls back to procedural textures (`GenerateNPCIconTexture()` / `GenerateTrashIconTexture()`).
- **Console Warnings:**
  - **Terrain Tree Shader Warnings (5 warnings on scene load):** `The tree PT_Fruit_Tree_01_green_cut / PT_Pine_Tree_03_green_cut / PT_Pine_Tree_03_stump / PT_Grass_02_v2 must use the Nature/Soft Occlusion shader. Otherwise billboarding/lighting will not work correctly.`
  - **`OnValidate` Animator Warning (`NPC Interaction.cs`):** `SendMessage cannot be called during Awake, CheckConsistency, or OnValidate (GuardianNPC: OnAnimatorMoveBuiltin)` triggered by `animator.Update(0f)` inside `NPCInteraction.EnsureAnimatorSetup()` (`Assets/EcoQuest/Script/NPC Interaction.cs:300`) when called from `OnValidate()` (`line 126`).
  - **Deprecated API Compiler Warnings (`CS0618`):** `FindObjectOfType` / `FindObjectsOfType` warnings in `Assets/EcoQuest/Script/Editor/AnimateEnvironmentTool.cs` (`line 22`), `Assets/EcoQuest/Script/Editor/FixVehiclesTool.cs` (`line 14`), and third-party `Assets/Loading Screen/` scripts.
- **Unused Scripts in Active Scenes:**
  - `Assets/EcoQuest/Script/Face Camera.cs` (`FaceCamera`), `Assets/EcoQuest/Script/The Forest Intro.cs` (`TitleFade`), `Assets/EcoQuest/Script/URPMaterialFixer.cs` (`URPMaterialFixer`), and `Assets/EcoQuest/Script/EnvironmentAnimations/` (`SimpleDrive.cs`, `SimpleFloat.cs`, `SimpleRotate.cs`) are not attached to any GameObject in `Main Menu.unity`, `Forest Scene.unity`, or `Beach.unity`.
- **Unfinished / Disconnected Features:**
  - `Assets/EcoQuest/Beach.unity` is disabled in Build Settings and has no quest, UI, or scene transition hooks.
  - `Forest Scene.unity` has no pause menu or "Return to Main Menu" button on the Post-Quiz `ResultsPanel` (`Continue Playing` only closes the quiz overlay to resume walking around the already-cleaned forest).
  - Typo in hierarchy object name `--- GAMEPLAY ---/TrashItemsGroup/Toiler Paper` ("Toiler Paper" instead of "Toilet Paper"), which is visible to players via `TrashHoverUI`.
