# EcoQuest: Comprehensive Technical Game Review & Systems Audit

**Project:** EcoQuest (Unity 6, C#)  
**Target Platform:** Windows Standalone / PC (DirectX 11 / 12)  
**Render Pipeline:** Universal Render Pipeline (URP 17.6.0)  
**Review Type:** Complete Read-Only Systems, Mechanics, Architecture, and Quality Audit  
**Date:** 2026-11-10  
**Auditor:** Senior Unity Engineer & Game Systems Analyst  

---

## 1. Executive Summary

**EcoQuest** is an educational 3D first-person environmental adventure game built in **Unity 6 (6000.3.11f1 LTS)** utilizing the **Universal Render Pipeline (URP)**. The primary pedagogical objective of the game is teaching proper waste segregation, environmental stewardship, and ecological consequence across multiple biome stages (**Forest Scene**, **City Scene**, and a planned **Beach Scene**).

### Core Strengths
1. **Strong Educational Loop:** The integration of baseline evaluation (**Pre-Quiz**) followed by active kinesthetic learning (**Drag-and-Drop waste sorting**) and measured post-evaluation (**Post-Quiz** with learning-gain tracking) provides a textbook model for serious educational games.
2. **Robust Kinesthetic Mechanics:** The physical pickup, carrying, and sorting system (`DragAndDrop.cs`, 2,653 lines) is unusually mature, implementing velocity-based physics throws, smooth damping, bobbing, visual trash pile stacking (`BinVisualTrashLayer.cs`), and multi-state settlement.
3. **Advanced City Simulation:** The City level contains a full ambient simulation layer featuring autonomous pedestrian AI (`NpcSpawner.cs`, `CityWalkGraph.cs`), procedural clothing profiles (`OutfitProfile.cs`, `OutfitBuilder.cs`), and a multi-agent traffic network with intersections, cars, trains, planes, and hot-air balloons (`CityTrafficManager.cs`, `TrafficVehicleController.cs`).
4. **Clean UI Layering & State Orchestration:** The user interface features a dedicated `ModalManager`, `UIOrchestrator`, and `HudVisibilityService` ensuring clean priority stacks across modals, HUD vitals, dialogue cameras, and menus.

### Key Architectural & Production Risks
1. **Critical Build Order Misconfiguration:** In `EditorBuildSettings.asset`, `Assets/EcoQuest/Forest Scene.unity` is assigned index `0`, while `Main Menu.unity` is at index `2`. Standalone production builds launch directly into the Forest tutorial rather than the title screen.
2. **Incomplete World Progression (Beach Stub):** While Forest Scene has a complete gating mechanism (`TravelGate.cs`) into City Scene, City Scene has no exit trigger or ending sequence, and `Assets/EcoQuest/Beach.unity` is an empty placeholder stub (4 GameObjects) unreferenced in build settings.
3. **Asset & Performance Density in City Scene:** `City Scene.unity` contains **9,031 GameObjects** and **2,103 MonoBehaviours** (29.12 MB scene file). Several diagnostic and UI scripts perform expensive `GameObject.Find` and `GetComponentInParent` queries within per-frame `Update()` loops.
4. **Missing / Unassigned Audio Pipeline:** Despite a fully coded `AudioManager` singleton, all gameplay audio clip slots (`defaultMusicClip`, `buttonClickClip`, `buttonHoverClip`) are unassigned in scenes; the project contains only 10 audio clips total (all localized to a loading screen demo pack).

---

## 2. Project Inventory

### 2.1 Top-Level Directory Structure
| Top-Level Directory | Primary Purpose & Contents |
| :--- | :--- |
| `Assets/EcoQuest/` | **Core Project Root:** Contains all game scenes, custom scripts, gameplay prefabs, UI graphics, and dialogue assets. |
| `Assets/EcoQuest/CityQuest/` | Detective mini-quest system for City Scene, procedural trash items, checklist UI, and bonus quiz adapter. |
| `Assets/EcoQuest/CityTraffic/` | City vehicle, train, balloon, and plane traffic simulation graphs, nodes, and controllers. |
| `Assets/EcoQuest/Inventory/` | uGUI Eco Shop modal, inventory controller, slot views, item ScriptableObjects, and item databases. |
| `Assets/EcoQuest/Script/` | Central gameplay scripts: `DragAndDrop`, `QuestManager`, `TrashBin`, `NPCInteraction`, `TravelGate`, `QuizManager`. |
| `Assets/EcoQuest/UI/` | Shared UI themes, fonts, loading tips, keycap prompts, and HUD overlays. |
| `Assets/StarterAssets/` | Unity First Person Controller package template, modified inputs, and character controller drivers. |
| `Assets/AdvancedPeopleSystem2/` | Third-party character customization system used for procedural civilian NPCs and outfits. |
| `Assets/polyperfect/` | Low Poly Ultimate Pack 3D asset library (props, buildings, vehicles, nature meshes). |
| `Assets/Polytope Studio/` | Low poly environment assets (trees, rocks, terrain textures for Forest). |
| `Assets/Loading Screen/` | Loading screen framework (`LoadingScreenController.cs`, tips, particle overlays). |
| `Packages/` | Unity Package Manager dependencies manifest (`manifest.json`) and locked package cache. |
| `ProjectSettings/` | Unity project configurations: graphics pipelines, input handlers, tags, layers, and build settings. |
| `Reports/` | Project analysis reports, test results, and validation artifacts (outside `Assets/`). |

### 2.2 Engine, Pipeline, and Package Specifications
* **Unity Version:** `6000.3.11f1 LTS` (`m_EditorVersionWithRevision: 6000.3.11f1 (3000ef702840)` via `ProjectSettings/ProjectVersion.txt`).
* **Render Pipeline:** Universal Render Pipeline (URP 17.6.0 via `com.unity.render-pipelines.universal`).
  * Custom SRP Asset: `Assets/Settings/URP_High.asset` (GUID `c75dd06410b264e8a8a3f027f86c6035`).
  * Quality Presets: `URP_Low.asset`, `URP_Medium.asset`, `URP_High.asset`, `URP_Ultra.asset` in `ProjectSettings/QualitySettings.asset`.
* **Input System:** Both Legacy Input Manager and New Input System enabled (`activeInputHandler: 2` in `ProjectSettings/ProjectSettings.asset`). Primary player movement uses `UnityEngine.InputSystem` actions (`Assets/InputSystem_Actions.inputactions`), while custom UI modals use `UnityEngine.Input.GetKeyDown` for `Tab`, `Escape`, and `B`.
* **Installed Packages (`Packages/manifest.json`):**
  * `com.unity.render-pipelines.universal`: `17.6.0` (Core URP rendering)
  * `com.unity.inputsystem`: `1.20.0` (Modern input handling)
  * `com.unity.cinemachine`: `3.1.6` (Dialogue and cutscene camera rigs)
  * `com.unity.ai.navigation`: `2.0.14` (NavMesh surfaces and NPC walking)
  * `com.unity.ugui`: `2.6.0` (uGUI Canvas and TextMeshPro)
  * `com.unity.visualscripting`: `1.9.12` (State machines and visual flow)
  * `com.unity.timeline`: `1.8.11` (Cinematic sequencing)

### 2.3 Scene Inventory & Build Settings Configuration
Configured in `ProjectSettings/EditorBuildSettings.asset`:

| Build Index | Scene Asset Path | Enabled | Status / Role |
| :---: | :--- | :---: | :--- |
| **0** | `Assets/EcoQuest/Forest Scene.unity` | **True** | **Anomaly:** Set as default build entry point instead of Main Menu. |
| **1** | `Assets/EcoQuest/City Scene.unity` | **True** | World 2: Dense urban district, traffic, detective quest, litterbug. |
| **2** | `Assets/EcoQuest/Main Menu.unity` | **True** | Title screen, settings modal, audio manager initializer. |
| *None* | `Assets/EcoQuest/Beach.unity` | **False (Omitted)** | **Unfinished Stub:** 4 GameObjects, contains only placeholder `Playerr`. |

### 2.4 Complete Script Classification & Responsibilities
The project contains 177 game-specific C# source files across distinct architectural tiers:

#### Player & Kinesthetic Systems
* `Assets/EcoQuest/Script/DragAndDrop.cs` (`DragAndDrop`): Core first-person physics grab, hold socket alignment, drag sway, throw impulse, and bin drop validation.
* `Assets/Script/ItemCarry.cs` (`ItemCarry`): Alternate/legacy kinematic pickup and carry script.
* `Assets/EcoQuest/Script/CenterPointInteract.cs` (`CenterPointInteract`): Center-screen viewport raycaster targeting interactables (NPCs, bins, gates).
* `Assets/EcoQuest/Script/CenterScreenCursor.cs` (`CenterScreenCursor`): Reticle dynamic scaling and contextual color changes upon hovering valid targets.
* `Assets/EcoQuest/Script/CrosshairController.cs` (`CrosshairController`): Manages crosshair visibility during UI modals and camera cuts.
* `Assets/EcoQuest/Script/PlayerSprintController.cs` (`PlayerSprintController`): Manages sprint toggling, checking stamina thresholds from `PlayerSurvivalStats`.
* `Assets/Script/ViewmodelBob.cs` (`ViewmodelBob`): Viewmodel procedural headbobbing, footstep cadence, and sway.

#### Waste Sorting & Economy
* `Assets/EcoQuest/Script/TrashItem.cs` (`TrashItem`): Trash item physics component, tracking start position, respawn logic, and deposit cooldowns.
* `Assets/EcoQuest/Script/TrashBin.cs` (`TrashBin`): Physics trigger detector on bins for category tags; controls rising visual trash layer.
* `Assets/EcoQuest/Script/Quest Manager.cs` (`QuestManager`): Central waste sorting manager on bins; tracks sorted counts against targets, enforces mistake penalties (-10 gold per 3 errors), and triggers Post-Quiz.
* `Assets/EcoQuest/Script/Coins Reward.cs` (`PlayerCurrency`): Singleton tracking player gold, animated HUD counter count-up routines, and persistent storage.
* `Assets/EcoQuest/Script/WrongTrashPopup.cs` (`WrongTrashPopup`): Animated pop-up alert triggered when trash is deposited into the incorrect bin.
* `Assets/EcoQuest/Script/TrashHoverUI.cs` (`TrashHoverUI`): Displays category tooltip pill above targeted waste objects in the world.

#### Quests, NPCs & Dialogue
* `Assets/EcoQuest/Script/NPC Interaction.cs` (`NPCInteraction`): Ranger/Guardian NPC dialogue controller; manages conversation state, overhead icons, and quiz triggers.
* `Assets/EcoQuest/Script/DialoguePresenter.cs` (`DialoguePresenter`): Formats and feeds animated typewritten text into the `UI_Dialogue` panel.
* `Assets/EcoQuest/Script/DialogueData.cs` (`DialogueData`): Static and serialized dialogue lines, intro monologues, and instruction sets.
* `Assets/EcoQuest/Characters/Explorer/NpcExplorerController.cs` (`NpcExplorerController`): Travel gate NPC with procedural idle, eye blinking, and look-at IK.
* `Assets/EcoQuest/Characters/Handyman/NpcConversationAnimator.cs` (`NpcConversationAnimator`): City merchant NPC opening the Eco Shop modal upon interaction.
* `Assets/EcoQuest/Script/Litterbug/LitterbugQuestManager.cs` (`LitterbugQuestManager`): City quest coordinator for pursuing and persuading the Litterbug NPC.
* `Assets/EcoQuest/Script/Litterbug/LitterbugNpc.cs` (`LitterbugNpc`): Autonomous running NPC dropping litter instances across City routes.
* `Assets/EcoQuest/CityQuest/Scripts/CityTrashDetectiveManager.cs` (`CityTrashDetectiveManager`): Robin NPC's checklist quest requiring players to collect specific trash items across city districts.

#### Educational Evaluation (Quiz System)
* `Assets/EcoQuest/Script/QuizManager.cs` (`QuizManager`): Singleton orchestrating baseline Pre-Quiz and learning-gain Post-Quiz evaluation.
* `Assets/EcoQuest/Script/QuizUIController.cs` (`QuizUIController`): uGUI modal displaying question cards, shuffled options, and result scorecards.
* `Assets/EcoQuest/Script/QuizUIAnimator.cs` (`QuizUIAnimator`): Tweening animations for question card transitions, option hover pulses, and star badges.
* `Assets/EcoQuest/Script/QuizData.cs` (`QuizData` / `QuizQuestion`): ScriptableObject containing questions, options, correct indices, and educational explanations.
* `Assets/EcoQuest/CityQuest/Scripts/CityBonusQuizAdapter.cs` (`CityBonusQuizAdapter`): Dynamically builds a 5-question bonus quiz in City Scene based on collected trash items.

#### Eco Shop & Survival HUD
* `Assets/EcoQuest/Inventory/UI_EcoShop.cs` (`UI_EcoShop`): Modal shop interface with double green border, coin capsule pill, and scrollable item grid.
* `Assets/EcoQuest/Inventory/UI_EcoShopSlot.cs` (`UI_EcoShopSlot`): Individual shop card handling available, locked, and cannot-afford states.
* `Assets/EcoQuest/Inventory/InventoryController.cs` (`InventoryController`): Player backpack inventory grid toggled with `B`.
* `Assets/EcoQuest/Script/Survival/PlayerSurvivalStats.cs` (`PlayerSurvivalStats`): Simulates player hunger and stamina depletion, exhaustion, and timed buffs.
* `Assets/EcoQuest/Script/Survival/UI_SurvivalStatsHUD.cs` (`UI_SurvivalStatsHUD`): Top-left HUD rendering hunger and stamina bars and active buff badges.

#### UI Framework & Scene Transitions
* `Assets/EcoQuest/Script/Shared/UIOrchestrator.cs` (`UIOrchestrator`): Central UI coordinator managing canvas group alphas and HUD priority layers.
* `Assets/EcoQuest/Script/Shared/ModalManager.cs` (`ModalManager`): Stack manager for modal windows ensuring single-modal focus and input suppression.
* `Assets/EcoQuest/Script/Shared/HudVisibilityService.cs` (`HudVisibilityService`): Manages contextual visibility of HUD elements during cinematics and dialogues.
* `Assets/EcoQuest/Script/PauseMenuController.cs` (`PauseMenuController`): Handles `Escape` key pause modal, audio mixer volume, and return-to-menu buttons.
* `Assets/EcoQuest/Script/SettingsManager.cs` (`SettingsManager`): Manages graphics quality, resolution, full-screen mode, and audio volume persistence.
* `Assets/EcoQuest/Script/TravelGate.cs` (`TravelGate`): Gating controller verifying all Forest tutorial and quest criteria before loading City Scene.
* `Assets/EcoQuest/Script/Shared/LoadingScreenController.cs` (`LoadingScreenController`): Smooth async scene loader with tips, progress bar, and particle FX.

---

## 3. Game Flow & Scene Walkthrough

```
                      +-----------------------------+
                      |   Main Menu.unity (Idx 2)   |
                      | - Play Game                 |
                      | - Settings Modal            |
                      | - Quit Application          |
                      +--------------+--------------+
                                     |
                         [LoadingScreen.Load]
                                     |
                                     v
                      +-----------------------------+
                      |  Forest Scene.unity (Idx 0) |
                      | 1. Sequential WASD Tutorial |
                      | 2. Ranger / Guardian Intro  |
                      | 3. Pre-Quiz (Baseline)      |
                      | 4. Waste Sorting Quest      |
                      | 5. Post-Quiz & Reward Claim |
                      | 6. Explorer & Travel Gate   |
                      +--------------+--------------+
                                     |
                       [TravelGate.TryTravelToCity]
                                     |
                                     v
                      +-----------------------------+
                      |   City Scene.unity (Idx 1)  |
                      | 1. Ambient Urban Simulation |
                      | 2. Handyman -> Eco Shop     |
                      | 3. Robin Detective Quest    |
                      | 4. City Bonus Quiz          |
                      | 5. Guardian Litterbug Chase |
                      +--------------+--------------+
                                     |
                     [No Exit Trigger Implemented]
                                     |
                                     x
                      +-----------------------------+
                      |     Beach.unity (Omitted)   |
                      | - 4 GameObjects (Stub)      |
                      | - Not in Build Settings     |
                      +-----------------------------+
```

### 3.1 Scene: `Main Menu.unity` (Build Index 2)
* **Purpose:** Initial player entry point, game launch, graphics/audio configuration, and application exit.
* **Entry / Exit:** Loaded via application startup or `ReturnToMainMenuButton` in Pause Menu. Exits via `MainMenuManager.PlayGame()`, which invokes `LoadingScreen.Load("Forest Scene")`.
* **Entities Present:**
  * Root hierarchy: `Main Camera`, `Directional Light`, `MenuBackgroundCanvas`, `Menu Manager`, `AudioManager`, `SettingsManager`, `EventSystem`.
  * Background VFX: `MenuAmbientFX.cs` (floating firefly particles), `MenuParallax.cs` (cursor-driven background layer shifting), `MenuCritter.cs` (animated critters).
* **UI Panels:** Title logo (`EcoQuest Text`), Play Button, Settings Button, Quit Button, and `SettingsPanelUI` modal.

### 3.2 Scene: `Forest Scene.unity` (Build Index 0 - Current Build Default)
* **Purpose:** Educational core level; onboarding tutorial, waste classification mechanics, pre/post evaluation, and world gateway.
* **Entry / Exit:** Loaded from Main Menu. Exits via the physical `TravelGate` archway leading to City Scene.
* **Objects & Entities Present:**
  * **Player:** First-person rig with `CharacterController`, `FirstPersonController`, `DragAndDrop`, `CenterPointInteract`, `PlayerSurvivalStats`.
  * **NPCs:**
    1. *Guardian / Ranger:* Stationed at the camp clearing with `NPCInteraction.cs`, `DialogueCameraRig`, and dialogue trigger zone.
    2. *Explorer:* Guarding the stone travel archway with `NpcExplorerController.cs` and `ExplorerGateController.cs`.
  * **Trash Bins:** Three colored waste receptacles with `TrashBin` and `QuestManager`:
    1. Green Bin: Tagged `Biodegradable` (accepts banana peels, apple cores, leaves).
    2. Blue Bin: Tagged `Recyclable` (accepts plastic bottles, aluminum cans, paper).
    3. Black Bin: Tagged `NonBiodegradable` (accepts styrofoam, plastic wrappers, broken glass).
  * **Trash Items:** 9 physical trash items (`TrashItem.cs`, `Rigidbody`, `MeshCollider`) scattered across the campsite.
* **Progression Sequence:**
  1. *Movement Tutorial:* `SequentialWASDTutorial.cs` prompts forward, backward, left, and right movement.
  2. *Dialogue & Baseline:* Player approaches the Guardian (`E` / Click); dialogue explains forest pollution and triggers the **Pre-Quiz** (3–6 questions in `QuizManager`).
  3. *Active Cleanup Quest:* Player picks up trash with Left Click, carries it, and deposits it into bins. Each correct item advances the progress bar; 3 mistakes deduct 10 gold.
  4. *Evaluation & Reward:* Depositing all 9 items completes the quest, awarding 100–150 gold and launching the **Post-Quiz**.
  5. *Gate Unlock:* Claiming quiz rewards sets `ForestRewardClaimed = 1`, unlocking `TravelGate.cs` and notifying the player to speak with the Explorer to travel to City Scene.

### 3.3 Scene: `City Scene.unity` (Build Index 1)
* **Purpose:** Urban environmental expansion; advanced multi-category waste collection, ambient traffic simulation, and shop commerce.
* **Entry / Exit:** Entered via `TravelGate.TryTravelToCity()`. **Exit Missing:** There is currently no exit portal, travel gate, or victory screen that exits City Scene back to menu or forward to Beach.
* **Objects & Entities Present:**
  * **Player:** Spawned at `CityPlayerSpawnPoint` with survival HUD enabled.
  * **NPCs & Quests:**
    1. *Handyman (`NpcConversationAnimator`):* Stands by the mobile shop kiosk; opens the `UI_EcoShop` modal.
    2. *Robin (`CityTrashDetectiveManager`):* Hands the player a 5-item detective checklist (`UI_CityTrashDetective`), tracking district-specific litter.
    3. *Guardian (`LitterbugQuestManager`):* Stationed at the central plaza fountain, directing the player to confront the Litterbug.
    4. *Litterbug (`LitterbugNpc`):* Dynamic running NPC that spawns litter props along sidewalks until cornered and persuaded through dialogue.
    5. *City Civilians:* 30+ ambient pedestrians moving between waypoints via `CityWalkGraph.cs` and `NpcSpawner.cs`.
  * **Traffic Network:** Automated traffic loops (`CityTrafficManager.cs`) managing 4-way signalized intersections, driving cars, moving commuter trains, hot-air balloons, and circling passenger airplanes.
  * **Educational Quiz:** `CityBonusQuizAdapter.cs` generates a dynamic 5-question bonus quiz using the specific items collected during the detective quest.

### 3.4 Scene: `Beach.unity` (Omitted from Build Settings)
* **Purpose:** Intended World 3 coastal cleanup level (marine debris, plastics, beach bins).
* **Current Status:** **Unfinished Stub.** Contains exactly 4 GameObjects: `Directional Light`, `Main Camera`, `EventSystem`, and `Playerr`. There are no terrain meshes, trash props, bins, NPCs, or scripts attached.

---

## 4. Gameplay Mechanics (In Detail)

### 4.1 Player Controller & Interaction
* **Movement:** `StarterAssets.FirstPersonController` driven by Unity CharacterController.
  * Base walk speed: `4.0 m/s`; sprint speed: `6.5 m/s` (modulated by `PlayerSprintController`).
  * Jump height: `1.2 m`; gravity: `-15.0 m/s²`.
  * Sprinting drains stamina at `22.0 units/sec` via `PlayerSurvivalStats`. When stamina drops below `0.5`, exhaustion triggers, preventing sprint until stamina regenerates past `20%`.
* **Camera Rig:** Virtual camera driven by Cinemachine / First Person Controller. Mouse sensitivity is configurable via `SettingsManager`.
* **Interaction System:**
  * `CenterPointInteract.cs` casts a continuous `ViewportPointToRay(0.5, 0.5)` raycast up to `3.5 meters`.
  * `CenterScreenCursor.cs` dynamically scales the crosshair from `12px` to `24px` when targeting interactables (`TrashItem`, `NPCInteraction`, `TrashBin`, `TravelGate`).

### 4.2 Kinesthetic Drag-and-Drop Waste Sorting
Implemented in `Assets/EcoQuest/Script/DragAndDrop.cs` (`2,653 lines`):
* **Pickup Mechanics:** When targeting a `TrashItem` within grab range (`2.8m`), pressing Left Mouse Button grabs the object. The item's `Rigidbody.isKinematic` is set to `true`, colliders are moved to a temporary trigger layer to prevent pushing the player, and it lerps smoothly to `HoldSocket` (`Vector3(0.35, -0.25, 0.75)` relative to camera).
* **Physics & Throwing:** Releasing the button drops the object. If the player is moving or flicking the mouse, momentum is applied:
  $$ec{V}_{throw} = 	ext{clamp}(ec{V}_{camera\_angular} 	imes R + ec{V}_{player}, 	ext{maxThrowSpeed}=12.0)$$
* **Bin Detection & Placement:**
  * Bins have a trigger volume with `TrashBin.cs` and `QuestManager.cs`.
  * When a trash item enters the trigger volume, `QuestManager.OnTriggerEnter(Collider other)` reads `other.GetComponent<TrashItem>()` and compares `acceptedTag`:
    * **Correct Match:** The item is snapped to a calculated slot offset inside the bin (`GetDepositSlotPosition(slotIndex)`), plays a shrink/stack animation, increments `currentCounts[acceptedTag]`, updates `fillImage.fillAmount`, and records the object in `collectedTrash`.
    * **Incorrect Match:** `HandleMistake()` increments the static `mistakes` counter, displays `WrongTrashPopup.Instance.Show()`, and applies an impulse ejecting the trash out of the bin. At 3 mistakes (`mistakes >= 3`), `PlayerCurrency.DeductGold(10)` is called, and `mistakes` resets to `0`.

### 4.3 Educational Evaluation & Quiz Loop
Implemented in `QuizManager.cs`, `QuizUIController.cs`, and `QuizData.cs`:
* **Pre-Quiz (Baseline Assessment):** Triggered during the initial Guardian dialogue in Forest Scene. Questions from `QuizData.asset` test the player's prior knowledge of biodegradable, recyclable, and hazardous waste. The baseline score is saved to `EcoQuest_PreQuizScore`. No penalties are applied for wrong answers.
* **Post-Quiz (Learning Evaluation):** Triggered immediately upon completing waste sorting (`QuestManager.CheckForWin()`). Presents identical or parallel questions.
  * Correct answers grant `+5 Gold` per question (`goldBonusPerCorrect = 5`).
  * Learning gain is computed as:
    $$\Delta_{learning} = 	ext{Score}_{post} - 	ext{Score}_{pre}$$
  * Scorecard UI displays total score, percentage improvement, star rating, and bonus coins awarded before unlocking the gate.

### 4.4 Eco Shop & Survival Economy
Implemented in `UI_EcoShop.cs`, `UI_EcoShopSlot.cs`, `InventoryController.cs`, and `PlayerSurvivalStats.cs`:
* **Currency:** Gold coins stored in PlayerPrefs (`"PlayerGold"`) and managed by `PlayerCurrency.cs`.
* **Shop Catalog Items (`Assets/EcoQuest/Inventory/Data/Items/`):**

| Item Name | Item ID | Price | Category | Gameplay Effect / Stat Modification |
| :--- | :--- | :---: | :---: | :--- |
| **Fresh Apple** | `food_apple` | 15 Gold | Food | `+20 Hunger` immediately restored. |
| **Artisan Bread** | `food_bread` | 25 Gold | Food | `+35 Hunger` immediately restored. |
| **Club Sandwich** | `food_sandwich` | 30 Gold | Food | `+50 Hunger` immediately restored. |
| **Packed Bento** | `food_lunchbox` | 55 Gold | Food | `+85 Hunger` (full meal restore). |
| **Energy Drink** | `energy_drink` | 25 Gold | Drink | `+30 Stamina` + 5% stamina regen boost for 120s. |
| **Energy 6-Pack** | `energy_drink_pack` | 50 Gold | Drink | `+60 Stamina` + 10% stamina regen boost for 300s. |
| **Swift Sneakers** | `boost_speed_short` | 25 Gold | Potion | `+15% Movement Speed` for 120s. |
| **Winged Boots** | `boost_speed_long` | 50 Gold | Potion | `+25% Movement Speed` for 300s. |
| **Locked Slots (x8)** | `locked_slot_XX` | `--` | Locked | Padlock icon; clicking plays locked rattle animation. |

* **Purchase & Equip Pipeline:**
  * When `BUY` is clicked in `UI_EcoShopSlot`, `UI_EcoShop.OnSlotBuyClicked(item)` verifies `PlayerCurrency.CurrentGold >= item.price`.
  * Currency is deducted via `currency.DeductGold(item.price)`.
  * The item instance is instantiated into `InventoryController.Instance.AddItem(item.id)`, serializing the slot state into `EcoQuest_Inventory_Slots`.
  * Opening the Inventory (`B`) allows clicking an item to consume it, which calls `PlayerSurvivalStats.AddHunger()`, `AddStamina()`, or applies timed buffs.

### 4.5 Audio & Environmental Feedback
* **Audio Manager:** `AudioManager.cs` is a persistent `DontDestroyOnLoad` singleton exposing 3 AudioMixer groups (`Master`, `Music`, `SFX`) and volume sliders.
* **Deficiency Finding:** As identified in the inventory scan, all 4 clip fields in `AudioManager` (`defaultMusicClip`, `buttonClickClip`, `buttonHoverClip`, `sliderChangeClip`) are unassigned in scenes. The game runs in near-total silence aside from UI button clicks in specific sub-panels.

---

## 5. Data Architecture & State Persistence

### 5.1 Persistence Map (PlayerPrefs)
All persistent game state is stored using Unity's `PlayerPrefs` backed by `EcoQuestSaveKeys.cs`:

| Key Name | Type | Value Range | Owning Script | Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `EcoQuest_TabTipDone` | Int | `0` or `1` | `QuestPanelToggleController` | Tracks if player has completed the Tab hint. |
| `EcoQuest_SeenInfoCards` | String | Delimited IDs | `ModalManager` | Bitmask/list of tutorial popups already shown. |
| `EcoQuest_PreQuizScore` | Int | `-1` to `6` | `QuizManager` | Baseline evaluation score (`-1` = unattempted). |
| `EcoQuest_PostQuizScore` | Int | `-1` to `6` | `QuizManager` | Post-test evaluation score. |
| `SequentialWASD_TutorialCompleted` | Int | `0` or `1` | `SequentialWASDTutorial` | Tracks completion of WASD onboarding. |
| `EcoQuest_ForestQuestAccepted` | Int | `0` or `1` | `NPCInteraction` | Set true when player accepts Guardian cleanup quest. |
| `EcoQuest_ForestTrashQuestDone` | Int | `0` or `1` | `QuestManager` | Set true when all 9 forest items are sorted. |
| `EcoQuest_ForestRewardClaimed` | Int | `0` or `1` | `QuizManager` / `TravelGate` | Set true when post-quiz reward is collected. |
| `EcoQuest_ForestExplorerGateNotified`| Int | `0` or `1` | `TravelGate` | Prevents redundant gate unlocked banners. |
| `PlayerGold` | Int | `0` to `99999` | `PlayerCurrency` / `UI_EcoShop` | Main currency balance for shop purchases. |
| `PlayerCoins` | Int | `0` to `99999` | `CityBonusQuizAdapter` | Legacy mirror key used by City bonus quiz. |
| `EcoQuest_City_BestStars` | Int | `0` to `3` | `CityTrashDetectiveManager` | Rating achieved on Robin's city checklist. |
| `EcoQuest_City_BestTime` | Float | Seconds | `CityTrashDetectiveManager` | Best completion time for city cleanup. |
| `EcoQuest_Inventory_Slots` | String | JSON Array | `InventoryController` | Serialized item IDs and quantities in bag. |

### 5.2 Script Communication Patterns
* **Singletons (Direct Service Access):** Major subsystems use standard `Instance` singletons with `DontDestroyOnLoad` or scene-lifecycle guards (`TravelGate.Instance`, `QuizManager.Instance`, `PlayerCurrency.Instance`, `UI_EcoShop.Instance`, `ModalManager.Instance`, `UIOrchestrator.Instance`).
* **C# Static Action Events (Decoupled Messaging):**
  * `TravelGate.OnGateUnlocked`, `TravelGate.OnTravelStarted`
  * `QuizManager.OnPreQuizFinished(score, max)`, `QuizManager.OnPostQuizFinished(pre, post, gain)`
  * `PlayerSurvivalStats.OnHungerChanged(cur, max)`, `PlayerSurvivalStats.OnStaminaChanged(cur, max)`
  * `NPCInteraction.OnDialogueStarted`, `NPCInteraction.OnDialogueEnded`
* **Direct Serialized Injection:** Used within UI panels and slot view components (`UI_EcoShopSlot` holds direct serialized references to its text, buttons, and image components).

---

## 6. Code Quality, Issues & Recommendations

### 6.1 Critical Priority (Fix Before Build / Release)

#### Issue 1: Incorrect Build Settings Scene Order
* **Evidence:** `ProjectSettings/EditorBuildSettings.asset` lines 8–14:
  ```yaml
  - enabled: 1
    path: Assets/EcoQuest/Forest Scene.unity
  - enabled: 1
    path: Assets/EcoQuest/City Scene.unity
  - enabled: 1
    path: Assets/EcoQuest/Main Menu.unity
  ```
* **Impact:** Any standalone executable build will immediately launch into `Forest Scene.unity` as scene index 0, bypassing the Main Menu, audio initialization, and graphics settings.
* **Remediation:** Reorder build settings so `Assets/EcoQuest/Main Menu.unity` is at index 0.

#### Issue 2: Empty Beach Scene Stub & Missing City Level Exit
* **Evidence:** `Assets/EcoQuest/Beach.unity` contains only 4 GameObjects (0.08 MB) and is not in build settings. Furthermore, `CityTrashDetectiveManager.cs` and `LitterbugQuestManager.cs` complete their respective quests but provide no exit trigger to load Beach Scene or return to Main Menu.
* **Impact:** The game dead-ends in City Scene after finishing the quests.
* **Remediation:** Implement a City-to-Beach travel gate or a victory summary screen that routes back to Main Menu.

#### Issue 3: Per-Frame `GameObject.Find` and `GetComponent` in `Update()`
* **Evidence:**
  * `Assets/EcoQuest/Script/InteractDebugPanel.cs` lines 54–55:
    ```csharp
    var guide = lqm != null ? lqm.QuestGiverTransform : GameObject.Find("FountainQuestGiver")?.transform;
    var player = lqm != null ? lqm.PlayerTransform : GameObject.FindGameObjectWithTag("Player")?.transform;
    ```
  * `Assets/EcoQuest/Script/CenterScreenCursor.cs` lines 71–73:
    ```csharp
    hit.collider.GetComponentInParent<NPCInteraction>() != null ||
    hit.collider.GetComponentInParent<TrashItem>() != null ||
    hit.collider.GetComponentInParent<DragAndDrop>() != null
    ```
* **Impact:** Generates unnecessary string hashing and heap allocations every frame, causing frame drops on lower-spec hardware.
* **Remediation:** Cache component references in `Awake()` or pass references through event triggers.

---

### 6.2 Important Priority (Gameplay & Performance Polish)

#### Issue 4: Dual Currency Save Key Divergence
* **Evidence:** `Coins Reward.cs` and `UI_EcoShop.cs` read/write `"PlayerGold"`, while `CityBonusQuizAdapter.cs` lines 270–276 writes to both `"PlayerGold"` and `"PlayerCoins"`.
* **Impact:** Inconsistent naming creates subtle desynchronization bugs if external tools or future scripts read only `"PlayerCoins"`.
* **Remediation:** Standardize on a single constant `EcoQuestSaveKeys.PlayerGold`.

#### Issue 5: Silent Audio System (Unassigned Clips)
* **Evidence:** `Assets/EcoQuest/Script/AudioManager.cs` fields `defaultMusicClip`, `buttonClickClip`, `buttonHoverClip`, `sliderChangeClip` are unassigned across scenes, and only 10 loading screen audio files exist in the project.
* **Impact:** The game feels unpolished and lacks auditory positive reinforcement for sorting waste.
* **Remediation:** Import royalty-free ambient nature audio, urban city bustle, sorting success chimes, and error buzzers.

#### Issue 6: Extreme Asset Density in City Scene (29 MB Scene)
* **Evidence:** `Assets/EcoQuest/City Scene.unity` contains 9,031 GameObjects and 2,103 MonoBehaviours.
* **Impact:** Long loading times and potential draw-call bottlenecks on low-end machines.
* **Remediation:** Enable static batching, bake occlusion culling, and use distance-based disabling for distant traffic and pedestrian agents.

---

### 6.3 Nice-to-Have (Pedagogical & Accessibility Enhancements)
1. **Sorting Combo & Streak Multiplier:** Award bonus gold for depositing 3 or 5 correct items in a row without mistakes to encourage careful classification.
2. **Colorblind Indicators on Bins:** While bins use green, blue, and black, adding clear visual icons (leaf for bio, chasing arrows for recycle, crossed-out can for non-bio) directly onto the bin models improves accessibility for colorblind students.
3. **Audio Subtitles & Font Scaling:** Provide UI toggles in `SettingsPanelUI.cs` to adjust dialogue font size and toggle subtitles.

---

## 7. Diagram-Ready Specification Notes

These structured notes provide exact models for generating formal software engineering diagrams:

### 7.1 State Machine & Flow Chart Specification
* **Nodes (Screens & States):**
  1. `State_Boot`: Initial application load -> loads `Main Menu.unity`.
  2. `State_MainMenu`: Title screen idle; transitions to `State_SettingsModal` (on Settings click) or `State_ForestLoading` (on Play click).
  3. `State_Forest_Tutorial`: Sequential WASD verification (`SequentialWASDTutorial.cs`).
  4. `State_Forest_PreQuiz`: Guardian dialogue triggers baseline evaluation (`QuizManager.StartPreQuiz()`).
  5. `State_Forest_Sorting`: Kinesthetic cleanup; loops on `ItemGrab` -> `ItemDeposit` -> `CheckBinTag`.
     * *Decision:* If mistake >= 3 -> trigger `DeductGold(10)` & `State_WrongPopup`.
     * *Decision:* If sortedCount == targetCount -> transition to `State_Forest_PostQuiz`.
  6. `State_Forest_PostQuiz`: Learning-gain quiz and scorecard modal.
  7. `State_Forest_TravelGate`: Gate unlocked; player speaks with Explorer -> triggers `LoadingScreen.Load("City Scene")`.
  8. `State_City_Exploration`: Ambient exploration with HUD vitals active.
  9. `State_City_EcoShop`: Modal shop overlay with item cards and purchase validation.
  10. `State_City_Quests`: Parallel states: Robin Detective Checklist and Guardian Litterbug Chase.
  11. `State_PauseMenu`: Global overlay triggered by `Escape`; halts `Time.timeScale` to 0.

### 7.2 Use Case Specification
* **Primary Actor:** `Student / Player`
* **Secondary Actors:** `Guardian / Ranger NPC`, `Explorer NPC`, `Handyman Merchant NPC`, `Robin Detective NPC`, `Litterbug NPC`
* **Use Cases:**
  * **UC-01: Navigate Environment** (Actor: Player)
    * `<<include>>`: Consume Stamina during Sprint.
  * **UC-02: Sort Waste Item** (Actor: Player)
    * `<<include>>`: Grab Trash Item (`DragAndDrop`).
    * `<<include>>`: Validate Bin Category (`TrashBin` / `QuestManager`).
    * `<<extend>>`: Display Error Alert on Wrong Bin (`WrongTrashPopup`).
    * `<<extend>>`: Deduct Gold Penalty on 3rd Mistake (`PlayerCurrency`).
  * **UC-03: Complete Educational Quiz** (Actors: Player, Guardian)
    * `<<include>>`: Display Question Card (`QuizUIController`).
    * `<<include>>`: Compute Learning Gain (`QuizManager`).
    * `<<include>>`: Award Gold Coins (`PlayerCurrency`).
  * **UC-04: Purchase Survival Items** (Actors: Player, Handyman)
    * `<<include>>`: Verify Currency Balance (`UI_EcoShop`).
    * `<<include>>`: Transfer Item to Backpack (`InventoryController`).
  * **UC-05: Travel Between Biomes** (Actors: Player, Explorer)
    * `<<include>>`: Verify Travel Gating Prerequisites (`TravelGate`).
    * `<<include>>`: Execute Async Scene Loading (`LoadingScreenController`).

### 7.3 Data Flow Diagram (DFD) Specification

#### Level 0: Context Diagram
* **External Entity:** `Player`
* **Process 0:** `EcoQuest Game Engine`
* **Data Flows:**
  * Player -> Process 0: Inputs (Movement, Raycast Interaction, Grab/Drop, Quiz Answers, Shop Clicks).
  * Process 0 -> Player: Rendered Viewport, UI Overlays, Vitals Bars, Quiz Scores, Audio Feedback.
  * Process 0 <-> Data Store `D1 (PlayerPrefs)`: Read/Write persistent save keys, gold balance, inventory slots.

#### Level 1: Subsystem Data Flow
* **Process 1.0 (Kinesthetic Sorting):** Takes `Player Interaction`, checks `D2 (Trash Database)`, outputs `Deposit Event` to Process 2.0 and `Penalty Event` to Process 3.0.
* **Process 2.0 (Quest Management):** Aggregates deposited items, compares against target counts, updates `D1 (PlayerPrefs)`, and outputs `Quest Win Event` to Process 4.0.
* **Process 3.0 (Economy & Currency):** Receives reward gold from 2.0, deducts penalty from 1.0, processes purchase deductions from 5.0, reads/writes `D1 (PlayerPrefs)`.
* **Process 4.0 (Quiz & Evaluation):** Reads questions from `D3 (QuizData ScriptableObject)`, receives player answers, computes score delta, sends bonus gold to 3.0, and sets `ForestRewardClaimed` in `D1`.
* **Process 5.0 (Eco Shop & Inventory):** Reads item catalog from `D4 (Inventory Item Database)`, validates balance against 3.0, writes owned items to `D1 (Inventory Slots)`.

#### Level 2: Drag-and-Drop Waste Sorting Process Decomposition
* **Process 1.1 (Target Acquisition):** `CenterPointInteract` raycasts world -> identifies `TrashItem` collider -> returns target instance.
* **Process 1.2 (Hold & Physics Rigging):** `DragAndDrop` sets `isKinematic=true`, reparents/aligns to `HoldSocket`, calculates drag velocity vectors.
* **Process 1.3 (Release & Trajectory):** Calculates throw impulse -> sets `isKinematic=false` -> activates continuous dynamic collision detection.
* **Process 1.4 (Trigger Collision):** `TrashBin.OnTriggerEnter` catches collider -> extracts `TrashItem.tag`.
* **Process 1.5 (Tag Comparison):**
  * If `TrashItem.tag == TrashBin.acceptedTag`:
    * Send success signal to `QuestManager.currentCounts`.
    * Trigger `BinVisualTrashLayer.Grow()`.
    * Animate trash item into pile slot.
  * If `TrashItem.tag != TrashBin.acceptedTag`:
    * Increment `QuestManager.mistakes`.
    * Invoke `WrongTrashPopup.Show()`.
    * Apply ejection force vector $ec{F}_{eject} = 	ext{up} 	imes 4.0 + 	ext{normal} 	imes 2.0$.

---

## 8. Confidence & Gaps

* **Verification Method:** Static structural code analysis, YAML scene parsing, serialized asset inspection, and ProjectSettings cross-referencing.
* **High Confidence Verifications:**
  * Exact build settings order and scene indices.
  * Total script count (177 game scripts) and complete responsibility mapping.
  * Complete PlayerPrefs key inventory (13 unique keys verified against codebase).
  * Drag-and-drop physics, hold sockets, throw momentum formulas, and mistake handling.
  * Pre-quiz and post-quiz pedagogical loop and scoring mathematics.
  * Eco Shop catalog items, prices, stat buff durations, and inventory persistence.
* **Gaps & Unverified Items (Requires Live Unity Runtime Execution):**
  * *Traffic Simulation Stress:* Exact frame rates and draw call counts in City Scene under dense vehicle/pedestrian traffic cannot be measured without running the Unity Profiler on target hardware.
  * *Lightmap Quality:* Scene lightmaps, shadow distance cutoffs, and post-processing volume overrides require live Game View rendering to evaluate visual fidelity.
  * *Audio Routing:* While clips are unassigned in serialized fields, any dynamic procedural audio generated via third-party DLLs at runtime could not be verified statically.
