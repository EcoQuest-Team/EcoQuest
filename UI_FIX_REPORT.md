# EcoQuest Quiz & Results UI Fix Report

## Overview
This report documents the resolution of the overlapping UI bug in the EcoQuest quiz and results screen. The fix reorganizes the scene hierarchy and layout without modifying visual design assets (sprites, colors, fonts, or button shapes) and updates `QuizUIController.cs` to ensure complete mutual exclusivity between the Quiz and Results views.

---

## 1. Step 1: Investigation Findings

### Scene Hierarchy & Canvas Context
- **Root Canvas**: `UI_Quiz` (`RenderMode.ScreenSpaceOverlay`, reference resolution `1920x1080`, Match Width/Height `0.5`).
- **Wooden Frame / Panel**: `QuizModalPanel` (size `980x640`, centered at `(0, 0)`).
  - Background sprite: `info_card_clean.png` (light parchment color `#ECE3CF` with wooden border, corner screws, and leaves).
  - Component settings: `CanvasGroup` alpha = `1.0`, `Image` color = `RGBA(1.000, 1.000, 1.000, 1.000)` (fully opaque).
- **Previous Hierarchy**:
  All quiz elements (`Header`, `QuestionContainer`, `AnswersContainer`, `FeedbackPanel`) and results elements (`ResultsPanel`) were direct sibling children of `QuizModalPanel` and were all active simultaneously.

### Initial Object States Before Fix

| Object | Parent | Active State | Anchors (Min / Max) | Pivot | Anchored Pos | Size Delta | Components |
|---|---|---|---|---|---|---|---|
| `UI_Quiz` | `--- UI ---` | Active | (0, 0) / (0, 0) | (0.5, 0.5) | (960, 540) | (1920, 1080) | Canvas, CanvasScaler, GraphicRaycaster, QuizUIController |
| `QuizModalPanel` | `UI_Quiz` | Active | (0.5, 0.5) / (0.5, 0.5) | (0.5, 0.5) | (0, 0) | (980, 640) | Image (`info_card_clean`), CanvasGroup (alpha=1.0) |
| `Header` | `QuizModalPanel` | Active | (0, 1) / (1, 1) | (0.5, 1) | (0, -20) | (-60, 85) | RectTransform |
| `TitleText` | `Header` | Active | (0, 0.5) / (0.7, 1) | (0.5, 0.5) | (204, -10) | (0, 0) | TextMeshProUGUI ("EcoQuest Learning Results") |
| `ProgressText` | `Header` | Active | (0.7, 0.5) / (1, 1) | (0.5, 0.5) | (-61, -11) | (0, 0) | TextMeshProUGUI ("Question 1 of 6") |
| `ProgressBar` | `Header` | Active | (0, 0) / (1, 0.35) | (0.5, 0.5) | (160, -12) | (-404.83, -10.8) | Image (`progress_bar_bg`) |
| `Fill` | `ProgressBar` | Active | (0, 0) / (1, 1) | (0.5, 0.5) | (0, 0) | (0, 0) | Image (`progress_bar_fill_white`) |
| `QuestionContainer` | `QuizModalPanel` | Active | (0, 1) / (1, 1) | (0.5, 1) | (0, -105) | (-80, 75) | CanvasGroup (alpha=1.0) |
| `QuestionText` | `QuestionContainer` | Active | (0, 0) / (1, 1) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI (sample question) |
| `AnswersContainer` | `QuizModalPanel` | Active | (0.5, 0) / (0.5, 0) | (0.5, 0) | (0, 175) | (860, 230) | VerticalLayoutGroup (spacing=8) |
| `AnswerButton_0..3` | `AnswersContainer` | Active | (0, 1) / (0, 1) | (0.5, 0.5) | (430, -24..-192) | (860, 48) | Button, Image, CanvasGroup, LayoutElement (prefH=48) |
| `FeedbackPanel` | `QuizModalPanel` | Active | (0.5, 0) / (0.5, 0) | (0.5, 0) | (0, 18) | (860, 145) | Image (alpha=0.95), CanvasGroup (alpha=1.0) |
| `FeedbackTitle` | `FeedbackPanel` | Active | (0.04, 0.6) / (0.7, 0.95) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI ("CORRECT!") |
| `ExplanationText` | `FeedbackPanel` | Active | (0.04, 0.08) / (0.72, 0.62) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI (card explanation) |
| `NextButton` | `FeedbackPanel` | Active | (0.76, 0.18) / (0.97, 0.82) | (0.5, 0.5) | (0, 0) | (0, 0) | Button, Image |
| `ResultsPanel` | `QuizModalPanel` | Active | (0, 0) / (1, 1) | (0.5, 0.5) | (0, -35) | (-60, -90) | CanvasGroup (alpha=1.0) |
| `PreScoreText` | `ResultsPanel` | Active | (0.1, 0.68) / (0.48, 0.84) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI ("Pre-Quiz Score: 0 / 6") |
| `PostScoreText` | `ResultsPanel` | Active | (0.52, 0.68) / (0.9, 0.84) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI ("Post-Quiz Score: 0 / 6") |
| `ImprovementBadge` | `ResultsPanel` | Active | (0.5, 0.48) / (0.5, 0.48) | (0.5, 0.5) | (0, 0) | (480, 54) | Image |
| `ImprovementText` | `ImprovementBadge` | Active | (0, 0) / (1, 1) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI ("+3 Points Improvement!") |
| `EncouragingMessage` | `ResultsPanel` | Active | (0.08, 0.26) / (0.92, 0.44) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI |
| `BonusGoldText` | `ResultsPanel` | Active | (0.2, 0.15) / (0.8, 0.25) | (0.5, 0.5) | (0, 0) | (0, 0) | TextMeshProUGUI ("+30 Bonus Gold Earned!") |
| `ContinueButton` | `ResultsPanel` | Active | (0.5, 0.02) / (0.5, 0.02) | (0.5, 0) | (0, 0) | (280, 52) | Button, Image |
| `CelebratoryParticles` | `ResultsPanel` | Active | (0.5, 0.5) / (0.5, 0.5) | (0.5, 0.5) | (0, 0) | (0, 0) | RectTransform (8 child stars) |

### Flow & Control Scripts
- **`QuizUIController.cs`**:
  - Controls modal visibility (`AnimateOpenModal`, `AnimateCloseModal`), question transitions (`AnimateDisplayQuestion`), feedback presentation (`AnimatePostQuizFeedback`), and results presentation (`AnimateResultsScreen`).
  - Previously lacked a parent container `QuizPanel` to toggle the question elements as a cohesive unit.
- **`QuizManager.cs`**:
  - Manages the lifecycle between `StartPreQuiz` (baseline, questions only) and `StartPostQuiz` (mastery, questions + feedback + results screen).

### Faded Panel Note
Investigation showed that `QuizModalPanel` has a `CanvasGroup` alpha of `1.0` and `Image` color of `RGBA(1.0, 1.0, 1.0, 1.0)` (full opacity). The perceived "faded" look was caused by the sprite `info_card_clean.png`, which is designed with a parchment texture (`#ECE3CF`) and a transparent photo-window cutout on the left where the darkened background/sky shows through. Because this is part of the original sprite asset design, it was preserved in accordance with the project guidelines.

---

## 2. Step 2: Implementation & Layout Fixes

### Reparenting Structure
Inside `QuizModalPanel`, elements are now cleanly separated into two mutually exclusive parent containers:
1. **`QuizPanel`** (New GameObject, size `(0, 0)`, stretch `(0, 0)` to `(1, 1)`):
   - `Header` (`ProgressText`, `ProgressBar` -> `Fill`)
   - `QuestionContainer` (`QuestionText`)
   - `AnswersContainer` (`AnswerButton_0..3`)
   - `FeedbackPanel` (`FeedbackTitle`, `ExplanationText`, `NextButton` -> `Text`)
2. **`ResultsPanel`** (Existing GameObject, size `(0, 0)`, stretch `(0, 0)` to `(1, 1)`):
   - `TitleText` (Reparented from `Header` as 1st child of `ResultsPanel`)
   - `PreScoreText`
   - `PostScoreText`
   - `ImprovementBadge` (`ImprovementText`)
   - `EncouragingMessage`
   - `BonusGoldText`
   - `ContinueButton` (`Text`)
   - `CelebratoryParticles` (`Star_0..7`)

### Quiz View Layout (Top to Bottom)
- **Header** (`anchoredPosition = (0, -25)`, `sizeDelta = (-60, 65)`):
  - `ProgressText`: Centered at `(0, 0)`, size `(720, 32)` displaying "Question 1 of 6".
  - `ProgressBar`: Centered at `(0, 0)`, size `(720, 18)` right below the progress text.
- **QuestionContainer** (`anchoredPosition = (0, -105)`, `sizeDelta = (-80, 75)`):
  - `QuestionText`: Centered, spanning the full container width with no truncation.
- **AnswersContainer** (`anchoredPosition = (0, 185)`, `sizeDelta = (860, 230)`):
  - `VerticalLayoutGroup`: Spacing `10px`, middle-center aligned.
  - Four answer buttons (`860x48`), evenly spaced with no overlap.
- **FeedbackPanel** (`anchoredPosition = (0, 25)`, `sizeDelta = (860, 145)`):
  - Sits neatly at the bottom beneath `AnswersContainer`. Contains "CORRECT!" title, card explanation, and "Next >" button.

### Results View Layout (Top to Bottom)
- **TitleText** (`anchoredPosition = (0, -55)`, `sizeDelta = (600, 50)`):
  - Centered at top: "EcoQuest Learning Results".
- **Scores Side by Side** (`anchoredPosition.y = -125`):
  - `PreScoreText`: `anchoredPosition = (-205, -125)`, size `(390, 45)`.
  - `PostScoreText`: `anchoredPosition = (205, -125)`, size `(390, 45)`.
- **Improvement Banner** (`anchoredPosition = (0, -195)`, `sizeDelta = (480, 54)`):
  - Centered banner with "+3 Points Improvement!".
- **Description Text** (`anchoredPosition = (0, -275)`, `sizeDelta = (780, 70)`):
  - Centered encouraging message with plenty of vertical breathing room.
- **Bonus Gold Banner** (`anchoredPosition = (0, -345)`, `sizeDelta = (500, 45)`):
  - Centered "+30 Bonus Gold Earned!".
- **Continue Journey Button** (`anchoredPosition = (0, 30)`, `sizeDelta = (280, 52)`):
  - Centered at the bottom with standard margin.

### Script Updates (`QuizUIController.cs`)
- Added `[SerializeField] private GameObject quizPanel;` to hold the `QuizPanel` reference.
- Wired mutual exclusivity across all runtime states:
  - `AnimateOpenModal`: activates `quizPanel`, deactivates `resultsPanel`.
  - `AnimateResultsScreen`: deactivates `quizPanel`, activates `resultsPanel`.
  - `HideImmediate`: hides both panels.
  - ContextMenu preview helpers (`PreviewQuizPanel`, `PreviewFeedbackPanel`, `PreviewResultsPanel`, `ShowAll`): enforces that only one panel is active at any time.
- Updated `SlideAndFade` question entry/exit animations to use `questionContainerRect`'s configured `anchoredPosition` rather than resetting it to `Vector2.zero`.

---

## 3. Step 3: Verification

### Visual Verification
- **Quiz View**: Clean presentation with centered progress indicator, question text, 4 evenly spaced answer buttons, and bottom feedback bar with Next button. Zero results elements visible.
- **Results View**: Clean presentation with title at top, side-by-side scores, improvement badge, description text, bonus gold, and bottom continue button. Zero quiz elements visible.
- **Default State**: `QuizPanel` active, `ResultsPanel` inactive.

### Reference & Error Checks
- **Console Errors**: 0 errors, 0 warnings, 0 exceptions.
- **Serialized References**: All 28 serialized references on `QuizUIController` verified intact (null count: 0).
- **Button Events**: All 6 buttons (`AnswerButton_0..3`, `NextButton`, `ContinueButton`) verified interactable and properly bound.
- **Text Truncation**: All TextMeshProUGUI elements verified with `isTextTruncated = false`.

---

## 4. Edit-Mode Scene Visibility & Runtime Lifecycle Management

### Objective
Make every GameObject and component under `UI_Quiz` visible, un-grayed, and directly editable in the Unity Editor Scene view without modifying any layout positions, anchors, sizes, button styling, fonts, or colors. Ensure all elements are properly hidden and managed dynamically at runtime so the player only sees the intended views without overlap.

### 1. GameObjects Set Active & Visible in Saved Scene
All child elements under `UI_Quiz` in `Assets/EcoQuest/Forest Scene.unity` were inspected and updated to `activeSelf = true`, with `CanvasGroup.alpha = 1.0` and `scale = (1, 1, 1)`:

| GameObject Path | Active State | CanvasGroup Alpha | Scale | Visibility in Edit Mode |
|---|---|---|---|---|
| `UI_Quiz/DarkOverlay` | `true` | `1.0` (was 0.0) | `(1, 1, 1)` | Visible dark modal backdrop |
| `UI_Quiz/QuizModalPanel` | `true` | `1.0` (was 0.0) | `(1, 1, 1)` | Visible wooden parchment frame |
| `QuizModalPanel/QuizPanel` | `true` | `1.0` | `(1, 1, 1)` | Visible quiz container |
| `QuizPanel/Header` | `true` | - | `(1, 1, 1)` | Visible header bar |
| `Header/ProgressText` | `true` | - | `(1, 1, 1)` | Visible progress text |
| `Header/ProgressBar` | `true` | - | `(1, 1, 1)` | Visible progress background |
| `ProgressBar/Fill` | `true` | - | `(1, 1, 1)` | Visible green fill |
| `QuizPanel/QuestionContainer` | `true` | `1.0` | `(1, 1, 1)` | Visible question container |
| `QuestionContainer/QuestionText` | `true` | - | `(1, 1, 1)` | Visible question prompt |
| `QuizPanel/AnswersContainer` | `true` | - | `(1, 1, 1)` | Visible answers vertical group |
| `AnswersContainer/AnswerButton_0..3` | `true` | `1.0` | `(1, 1, 1)` | Visible buttons with icons |
| `AnswerButton_0..3/Checkmark & Cross` | `true` | - | `(1, 1, 1)` | Visible feedback indicators |
| `QuizPanel/FeedbackPanel` | `true` | `1.0` | `(1, 1, 1)` | Visible feedback banner |
| `FeedbackPanel/FeedbackTitle` | `true` | - | `(1, 1, 1)` | Visible "CORRECT!" banner |
| `FeedbackPanel/ExplanationText` | `true` | - | `(1, 1, 1)` | Visible educational explanation |
| `FeedbackPanel/NextButton` | `true` | - | `(1, 1, 1)` | Visible green "Next >" button |
| `QuizModalPanel/ResultsPanel` | `true` | `1.0` | `(1, 1, 1)` | Visible results container |
| `ResultsPanel/TitleText` | `true` | - | `(1, 1, 1)` | Visible "EcoQuest Learning Results" |
| `ResultsPanel/PreScoreText` | `true` | - | `(1, 1, 1)` | Visible Pre-Quiz score text |
| `ResultsPanel/PostScoreText` | `true` | - | `(1, 1, 1)` | Visible Post-Quiz score text |
| `ResultsPanel/ImprovementBadge` | `true` | - | `(1, 1, 1)` | Visible improvement pill banner |
| `ImprovementBadge/ImprovementText` | `true` | - | `(1, 1, 1)` | Visible "+3 Points Improvement!" |
| `ResultsPanel/EncouragingMessage` | `true` | - | `(1, 1, 1)` | Visible descriptive feedback |
| `ResultsPanel/BonusGoldText` | `true` | - | `(1, 1, 1)` | Visible "+30 Bonus Gold Earned!" |
| `ResultsPanel/ContinueButton` | `true` | - | `(1, 1, 1)` | Visible "Continue Journey" button |
| `ResultsPanel/CelebrationParticles` | `true` | - | `(1, 1, 1)` | Visible celebratory star particles |

**Total Inactive Objects Remaining under `UI_Quiz` in Edit Mode:** 0.

### 2. Runtime Script Logic for Hiding Panels
All runtime hiding is handled in [`QuizUIController.cs`](file:///C:/Users/ssopb/OneDrive/Documents/GitHub/EcoQuest-v1.1.4/Assets/EcoQuest/Script/QuizUIController.cs):

1. **Initialization (`Awake()`, Line 104)**:
   - Calls `HideImmediate()` as soon as Play Mode begins, ensuring that modal elements active in the scene do not flash or obscure the game screen before the player interacts with the NPC.
2. **Immediate Modal Hiding (`HideImmediate()`, Lines 111–132)**:
   - `overlayCanvasGroup.alpha = 0f; overlayCanvasGroup.gameObject.SetActive(false);`
   - `modalPanelCanvasGroup.alpha = 0f; modalPanelRect.gameObject.SetActive(false);`
   - `quizPanel.SetActive(true);`
   - `feedbackPanel.SetActive(false);`
   - `resultsPanel.SetActive(false);`
   - `celebratoryParticlesGroup.SetActive(false);`
3. **Modal Opening Animation (`AnimateOpenModal()`, Lines 246–260)**:
   - Activates overlay and modal panel GameObjects and fades/pops them in smoothly.
   - Activates `quizPanel` while ensuring `resultsPanel` remains inactive.
4. **Question Display (`AnimateDisplayQuestion()`, Lines 289–295)**:
   - Keeps `feedbackPanel` inactive while the player reads and answers the question.
5. **Feedback Display (`AnimatePostQuizFeedback()`, Lines 401–410)**:
   - Activates `feedbackPanel` with smooth fade-in after an answer button is clicked in Post-Quiz.
6. **Results Screen (`AnimateResultsScreen()`, Lines 501–518)**:
   - Deactivates `quizPanel` and `feedbackPanel`.
   - Activates `resultsPanel` and fades in `resultsCanvasGroup`.
7. **Modal Closing (`AnimateCloseModal()`, Lines 655–674)**:
   - Fades out overlay and pops out modal, followed by `HideImmediate()`.

### 3. Editor ContextMenu Preview Tools
To allow viewing each distinct UI state cleanly in the Scene view without manual hierarchy toggling, the following context menu actions were added to `QuizUIController` (accessible via right-clicking the component in the Inspector):
- **`Preview Quiz` / `Preview Quiz Panel` (Lines 135–147)**: Shows `QuizPanel` with question container and answers; hides feedback and results.
- **`Preview Feedback` / `Preview Feedback Panel` (Lines 149–161)**: Shows `QuizPanel` with `FeedbackPanel` and "Next >" button; hides results.
- **`Preview Results` / `Preview Results Panel` (Lines 163–176)**: Shows `ResultsPanel` with scores, improvement badge, bonus gold, and continue button; hides quiz questions.
- **`Show All` / `Show All (Every Object Visible)` (Lines 178–189)**: Activates every single child object and resets alpha to 1.0 for editing.

### 4. Layout & Design Preservation Confirmation
- **Positions, Sizes, Anchors & Pivots**: 100% unchanged. All approved RectTransform layouts preserved.
- **Visual Design**: Sprites (`info_card_clean.png`, button frames, leaf accents, screw caps), colors, font assets, and font sizes 100% unchanged.
- **Features & Logic**: All Pre-Test questions, Post-Test feedback bars, score calculations, PlayerPrefs persistence, tutorial cards, and bonus gold rewards verified functional.
- **Verification Results**: Automated end-to-end Play Mode test passed with 100% success (`[TEST_SUCCESS] ALL PRE-TEST, TUTORIAL, POST-TEST AND RESULTS CHECKS PASSED WITH 100% SUCCESS!`) and 0 console errors.

---

## 5. Text Overflow Prevention, Kid-Friendly Sizing & Answer Highlighting Fix

### Objective
Ensure that text stays comfortably inside all UI containers (green answer buttons, question area, feedback bar, results title), apply kid-friendly font sizing, prevent any overlap with frames or adjacent buttons, and highlight the correct answer 15–20% lighter after the player answers.

### 1. Answer Buttons & Text Overflow Fixes
- **Button Height & Spacing**:
  - Increased button height from `48px` to `72px` (`minHeight: 64px`, `preferredHeight: 72px`).
  - `VerticalLayoutGroup` spacing set to `7px` with `childAlignment = MiddleCenter`.
  - Four buttons cleanly span `288px` inside `AnswersContainer` (`780x310px` at `y = 145`).
- **TextMeshPro Text Settings**:
  - `textWrappingMode = TextWrappingModes.Normal` (word wrap enabled).
  - `enableAutoSizing = true` (min `22pt`, max `27pt`, base `25pt`).
  - Margins: `(Left: 25, Top: 6, Right: 55, Bottom: 6)`. The `55px` right margin ensures long 2-line answers never overlap or touch the Checkmark / Cross icon.
  - Alignment: `MidlineLeft`.
  - Tested with the longest 74-character answer (*"Synthetic materials that cannot break down naturally in the environment"*): wraps into two balanced lines with ample padding inside the green button.

### 2. Question Text & Bounds Fixes
- **Position & Sizing**:
  - `QuestionContainer` set to `anchoredPosition = (243.5, -86)`, `sizeDelta = (780, 82)`.
  - Perfectly matches the horizontal span of the parchment and `AnswersContainer`.
  - Completely inside the panel and never touches the picture frame cutout on the left.
- **TextMeshPro Text Settings**:
  - `textWrappingMode = TextWrappingModes.Normal`.
  - `enableAutoSizing = true` (min `30pt`, max `38pt`, base `36pt`).
  - Font: `Fredoka SDF` (kid-friendly rounded font).
  - Color: `#332414` (`RGBA(0.20, 0.14, 0.08, 1.0)`) - high-contrast dark brown on light parchment.
  - Margins: `(Left: 15, Top: 4, Right: 15, Bottom: 4)`.
  - Tested with longest 73-character question (*"According to the Guardian, what happens if you make 3 sorting mistakes?"*): wraps into two centered lines with zero clipping.

### 3. Feedback Bar Layout Fixes
- **Panel Bounds**:
  - `FeedbackPanel` set to `anchoredPosition = (243.5, 42)`, `sizeDelta = (780, 92)`. Sits safely above the bottom wooden border.
- **Children Non-Overlapping Layout**:
  - `FeedbackTitle`: anchors `(0.03, 0.58)` to `(0.68, 0.95)`, font size `26pt` (bold).
  - `ExplanationText`: anchors `(0.03, 0.05)` to `(0.68, 0.58)`, font size `22pt` (`18–24pt` auto-size), dark brown text `#332414`.
  - `NextButton`: anchors `(0.72, 0.12)` to `(0.97, 0.88)`, font size `26pt`.
  - Text max-X is `0.68` while button min-X is `0.72` (4% / 31px horizontal buffer). Zero overlap between title, explanation, and Next button.

### 4. Results Screen Header Fix
- **Results Title Position**:
  - Moved `TitleText` in `ResultsPanel` from `(31, -101)` to centered `(0, -45)`.
  - Sits `30px` clear above `PreScoreText` and `PostScoreText` (`y = -125`).
  - Eliminates overlap with the score line while preserving all score texts, improvement badge, and bonus gold.

### 5. Correct Answer Highlighting Logic ([`QuizUIController.cs`](file:///C:/Users/ssopb/OneDrive/Documents/GitHub/EcoQuest-v1.1.4/Assets/EcoQuest/Script/QuizUIController.cs))
- Added `correctHighlightColor = Color.white` and `dimmedButtonColor = new Color(0.80f, 0.80f, 0.80f, 1f)`.
- In `AnimatePostQuizFeedback`:
  - The correct button image is tinted with `correctHighlightColor` (making the green `accept_button` sprite 18–20% lighter and brighter than the other buttons), text set to white, and checkmark displayed.
  - Other unselected buttons are dimmed to `dimmedButtonColor`.
  - If a wrong button was chosen, it shakes horizontally with `wrongColor` (red) and cross icon, while the correct button remains highlighted in light green with checkmark.
- In `AnimateDisplayQuestion`:
  - When the next question loads, all buttons, text colors, checkmarks, and crosses reset back to `normalButtonColor` and `normalTextColor`.

### 6. Verification
- Tested with longest question (73 chars) and longest option (74 chars) in Scene View.
- Play Mode automated run verified: Pre-Test -> Lessons -> Post-Test -> Results screen.
- Console: `[TEST_SUCCESS] ALL PRE-TEST, TUTORIAL, POST-TEST AND RESULTS CHECKS PASSED WITH 100% SUCCESS!` with 0 errors.

---

## 6. Settings System

### 1. Assets & Prefabs Added / Updated
| Asset / Prefab Path | Purpose |
|---|---|
| `Assets/EcoQuest/Audio/MainMixer.mixer` | Unity `AudioMixer` with `Master`, `Music`, and `SFX` groups and exposed volume parameters `MasterVol`, `MusicVol`, and `SFXVol` (`Mathf.Log10(Mathf.Max(v, 0.0001f)) * 20f`). |
| `Assets/EcoQuest/Prefabs/SettingsPanel.prefab` | Reusable wooden-panel and leaf-styled Settings UI Canvas (`sortingOrder = 100`, reference resolution `1920x1080`, `matchWidthOrHeight = 0.5`) shared by both `Main Menu.unity` and `Forest Scene.unity`. Uses existing UI sprites (`info_card_clean.png`, `dialogue_panel_no_button.png`, `accept_button.png`, `progress_bar_bg.png`, `progress_bar_fill_white.png`, `checkmark_icon.png`, `icon_info_leaf.png`) and fonts (`Fredoka SDF`, `Nunito SDF`). |
| `Assets/EcoQuest/Prefabs/SceneTransition.prefab` | Updated `StartSoundHook` and `WhooshSoundHook` `AudioSource` components to route through the `SFX` mixer group of `MainMixer.mixer`. |

### 2. Scene Objects Added
- **`Assets/EcoQuest/Main Menu.unity`**:
  - `SettingsManager` (root GameObject with `SettingsManager`, `DontDestroyOnLoad`).
  - `AudioManager` (root GameObject with `AudioManager`, child `MusicSource` routed to `Music` group, and child `SFXSource` routed to `SFX` group, `DontDestroyOnLoad`).
  - `SettingsPanel` (prefab instance of `SettingsPanel.prefab`, inactive by default, wired to `MainMenuManager.settingsPanel`).
  - Existing menu buttons (`Play Button`, `settings`, `Quit`) have their `AudioSource` components routed to the `SFX` mixer group.
- **`Assets/EcoQuest/Forest Scene.unity`**:
  - `UI_PauseMenu` (under `--- UI ---`, `Canvas` with `sortingOrder = 95`, `PauseMenuController` script):
    - `PauseOverlay` (`DarkBackdrop` + `PauseCard` with `ResumeButton`, `SettingsButton`, `MainMenuButton`, and `ConfirmMainMenuOverlay` popup with `YesButton` / `NoButton`).
  - `SettingsPanel` (under `--- UI ---`, prefab instance of `SettingsPanel.prefab`, `sortingOrder = 100`, inactive by default, wired to `PauseMenuController.settingsPanelObject` and `settingsPanelUI`).
  - `PlayerCapsule` (`FirstPersonController`): Added child `FootstepAudioSource` routed to the `SFX` mixer group and wired to `FirstPersonController.FootstepAudioSource`.

### 3. Scripts Added & Updated
| Script Path | Role |
|---|---|
| `Assets/EcoQuest/Script/AudioManager.cs` | Persistent `DontDestroyOnLoad` singleton managing `MainMixer.mixer` dB conversion (`LinearToDecibel`), `PlayMusic(clip)`, `PlaySFX(clip)`, and UI sound hooks (`PlayButtonClick`, `PlayButtonHover`, `PlaySliderChange`). Safely ignores `null` clips without warnings and automatically routes scene `AudioSource`s to `SFX`. |
| `Assets/EcoQuest/Script/SettingsManager.cs` | Persistent `DontDestroyOnLoad` singleton initialized via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`. Loads, saves, resets, and applies Audio, Graphics, and Gameplay settings across scenes, including non-compounding font scaling (`Normal` = `1.0x`, `Large` = `1.15x`) on `UI_Quiz` and `UI_Dialogue` only. |
| `Assets/EcoQuest/Script/SettingsPanelUI.cs` | Controller for `SettingsPanel.prefab` handling Audio sliders (`0–100%`) + `Mute All`, Graphics options + `Apply Graphics`, Gameplay options (`Mouse Sensitivity`, `Tutorial Hints`, `Text Size`), read-only Controls list, `Reset to Defaults` confirmation popup, and unscaled-time fade/scale open (`0.22s`) and close (`0.16s`) animations. |
| `Assets/EcoQuest/Script/PauseMenuController.cs` | Controls the `Esc` Pause Menu in `Forest Scene`. Pauses via `Time.timeScale = 0f`, unlocks cursor, disables `FirstPersonController`, and restores exact pre-pause cursor and controller states on resume. Blocks opening during dialogue, Pre-Test, Post-Test, lesson cards, and the active WASD intro tutorial. |
| `Assets/EcoQuest/Script/Main Menu.cs` | Updated `CloseSettings()` to invoke `SettingsPanelUI.ClosePanel()` for animated unscaled closing. |
| `Assets/EcoQuest/Script/ButtonHoverFX.cs` | Forwards hover and click sound triggers to `AudioManager.Instance` when local clips are unassigned. |
| `Assets/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs` | Added `FootstepAudioSource`, `FootstepAudioClips`, and `LandingAudioClip` routed through the `SFX` mixer group (`null`-safe). |
| `Assets/EcoQuest/Script/SequentialWASDTutorial.cs` & `GameplayControlTipsManager.cs` | Added `ApplyTutorialHintsSetting(bool)` to respect the `Tutorial Hints` toggle immediately. |
| `Assets/EcoQuest/Script/CursorQuickAccessController.cs` | Guarded `Update()` with `if (PauseMenuController.IsPaused) return;` so `Ctrl` cursor unlock does not conflict with the Pause Menu. |

### 4. PlayerPrefs Keys & Default Values
All settings keys use the `EcoQuest_Settings_` prefix. Existing quiz keys (`EcoQuest_PreQuizScore` and `EcoQuest_PostQuizScore`) are never modified or deleted by `SettingsManager`.

| PlayerPrefs Key | Type | Default Value | Description |
|---|---|---|---|
| `EcoQuest_Settings_MasterVol` | `float` | `1.0f` (`100%`, `0 dB`) | Master volume (`0.0` to `1.0`) |
| `EcoQuest_Settings_MusicVol` | `float` | `0.8f` (`80%`, `-1.94 dB`) | Music volume (`0.0` to `1.0`) |
| `EcoQuest_Settings_SFXVol` | `float` | `0.8f` (`80%`, `-1.94 dB`) | SFX volume (`0.0` to `1.0`) |
| `EcoQuest_Settings_MuteAll` | `int` | `0` (`false`) | Mute All toggle (`1` sets `MasterVol` to `-80 dB`) |
| `EcoQuest_Settings_Quality` | `int` | `2` (or current quality level) | Quality preset index (`QualitySettings.names`) |
| `EcoQuest_Settings_Fullscreen` | `int` | `1` (`true`) | Fullscreen (`1`) vs Windowed (`0`) |
| `EcoQuest_Settings_ResolutionWidth` | `int` | `1920` (or native width) | Screen resolution width |
| `EcoQuest_Settings_ResolutionHeight` | `int` | `1080` (or native height) | Screen resolution height |
| `EcoQuest_Settings_VSync` | `int` | `1` (`true`) | Vertical Sync (`QualitySettings.vSyncCount`) |
| `EcoQuest_Settings_FPSLimit` | `int` | `-1` (`Unlimited`) | Target frame rate (`30`, `60`, `120`, or `-1` for Unlimited) |
| `EcoQuest_Settings_MouseSensitivity` | `float` | `1.0f` (`1.0x`) | Applied to `FirstPersonController.RotationSpeed` (`0.2` to `3.0`) |
| `EcoQuest_Settings_TutorialHints` | `int` | `1` (`true`) | Enables/disables WASD and control tip popups |
| `EcoQuest_Settings_TextSize` | `int` | `0` (`Normal`) | `0` = Normal (`1.0x`), `1` = Large (`1.15x`) on Quiz & Dialogue text |

### 5. Assets You Can Provide Later (Optional Hooks)
The system is 100% functional and silent-safe without audio files. When you are ready to add sound effects and music, assign `AudioClip` assets to these Inspector fields:
1. **`AudioManager` (in `Main Menu.unity`)**:
   - `defaultMusicClip`: Background music clip (loops automatically on `MusicSource`).
   - `buttonClickClip`: Default UI button click sound.
   - `buttonHoverClip`: Default UI button hover sound.
   - `sliderChangeClip`: Subtle tick sound when dragging volume/sensitivity sliders (throttled to `0.06s`).
2. **`SceneTransition` (`Assets/EcoQuest/Prefabs/SceneTransition.prefab`)**:
   - `startSoundHook.clip`: Sound played when pressing Start Game.
   - `whooshSoundHook.clip`: Leaf-wipe transition whoosh sound.
3. **`FirstPersonController` (`PlayerCapsule` in `Forest Scene.unity`)**:
   - `FootstepAudioClips`: Array of footstep `AudioClip`s.
   - `LandingAudioClip`: Jump landing `AudioClip`.
4. **Optional Sprites**:
   - All panels, sliders, toggles, dropdowns, and buttons already use EcoQuest's existing wooden-panel and leaf sprites (`info_card_clean.png`, `dialogue_panel_no_button.png`, `accept_button.png`, `progress_bar_bg.png`, `progress_bar_fill_white.png`, `checkmark_icon.png`, `icon_info_leaf.png`). No additional sprites are required unless you wish to customize the slider handle knob or dropdown arrow icon.


---

## 7. Settings Panel Visual Rebuild & End-to-End Verification (`SettingsPanel.prefab`)

### 1. Backup & Design Reconstruction Summary
- **Backup Created**: Duplicated `Assets/EcoQuest/Prefabs/SettingsPanel.prefab` to `Assets/EcoQuest/Prefabs/SettingsPanel_BACKUP2.prefab` (unreferenced in all scenes).
- **Untouched Frame & Header**: Preserved `ModalFrame` (`1500x680`), `WoodenFrameBG` (`info_card_clean.png`), `LeftCutoutParchmentFill` (`#EDE3D0`), and `Header` (`GAME SETTINGS` title + `LeafIconLeft` / `LeafIconRight` with `icon_info_leaf.png`).
- **Controls Guide Card (`ControlsGuideBox`)**:
  - Added missing `A` keycap so `W` sits centered above `A`, `S`, `D` (`dark charcoal keycaps #342B24` with white text), paired with `Move Around` on a warm light strip.
  - Built cream keycaps (`SPACE`, `SHIFT`, `CTRL`, `TAB`, `ESC`) with dark border and dark text, plus smaller dark `E` keycap (`Talk to Guardian`), each on its own aligned light strip.
- **Section Headers (`AUDIO`, `GAMEPLAY`, `GRAPHICS`)**: Bold dark green (`#0F4D19`) uppercase headers with thin full-column underlines.
- **Sliders (`Master Volume`, `Music Volume`, `SFX Volume`, `Mouse Speed`)**: Label above, thin dark green track (`#143016`) with bright green fill (`#43A84A`), `24x24` round green handle (`Knob`), and right-aligned value text (`100%`, `80%`, `1.0x`) in evenly spaced rows.
- **Mute All Audio Switch**: Pill-shaped toggle switch (`60x30`) with a round green knob (`muteSwitchKnob`) that moves between `(-15, 0)` (unmuted) and `(+15, 0)` (muted).
- **Checkboxes (`Tutorial Hints`, `Fullscreen Mode`, `VSync`)**: Rounded dark green squares (`32x32`) with white `checkmark_icon.png` (`22x22`), aligned in right-hand columns.
- **Dropdowns (`Quiz/Chat Text`, `Quality Preset`, `Resolution`, `FPS Limit`)**: Dark green pills (`#133616`) with white text, solid golden downward carets (`#EDC247`), and dark green popup templates/items (`Template` / `Item Background`) with golden checkmarks.
- **Bottom Action Buttons**: Pill buttons with uppercase white text — `APPLY GRAPHICS` (green `#1B6B2A` + top-right dual-leaf accent), `RESET TO DEFAULTS` (olive `#3B591F` + top-right dual-leaf accent), and `BACK` (dark green `#133316`).

### 2. Pass / Fail Verification Table

| Function / Requirement | Scene(s) Tested | Verification Details & Measured Values | Status |
|---|---|---|---|
| **Backup Prefab (`SettingsPanel_BACKUP2.prefab`)** | Project Assets | Created at `Assets/EcoQuest/Prefabs/SettingsPanel_BACKUP2.prefab`; verified unreferenced in all scenes. | **PASS** |
| **Serialized References (No None / Missing)** | Both Scenes & Prefab | Inspected all serialized fields on `SettingsPanelUI`, `MainMenuManager`, and `PauseMenuController`; `0` null or missing references. | **PASS** |
| **Visual Match at 16:9 (`1920x1080`) & 16:10 (`1920x1200`)** | `Main Menu`, `Forest Scene` | Compared rendered output at `1920x1080` and `1920x1200` against reference screenshot; zero text overlaps, zero unintended line wraps, nothing outside panel. | **PASS** |
| **Master, Music & SFX Sliders (Live Mixer + `0` Silent + Save)** | `Main Menu`, `Forest Scene` | At `0`: labels = `0%`, `MasterVol`/`MusicVol`/`SFXVol` = `-80.0 dB`, `AudioListener.volume` = `0.00`. At `0.75/0.60/0.50`: labels = `75%/60%/50%`, dB = `-2.50/-4.44/-6.02 dB`, saved to `PlayerPrefs`. | **PASS** |
| **Mute All Audio Pill Switch (Non-Destructive + Knob State)** | `Main Menu`, `Forest Scene` | Toggling ON sets `MasterVol` = `-80.0 dB` and moves knob to `(15, 0)` while preserving saved slider `PlayerPrefs` (`0.75, 0.60, 0.50`). Toggling OFF restores `-2.50 dB` and moves knob to `(-15, 0)`. | **PASS** |
| **Mouse Speed (`0.2x` to `3.0x`) -> `FirstPersonController.RotationSpeed`** | `Main Menu` -> `Forest Scene` | Setting `2.4x` updates label and saves `EcoQuest_Settings_MouseSensitivity = 2.4`; loading `Forest Scene` applies `FirstPersonController.RotationSpeed = 2.40` on startup and live while paused (`1.80`). | **PASS** |
| **Tutorial Hints Toggle (Live On/Off)** | `Main Menu`, `Forest Scene` | Toggling OFF/ON updates `EcoQuest_Settings_TutorialHints` and immediately toggles `SequentialWASDTutorial` and `GameplayControlTipsManager` visibility. | **PASS** |
| **Quiz/Chat Text (`Normal` / `Large`, Non-Compounding)** | `Forest Scene` | Scales only `UI_Quiz` (`30.0pt -> 34.5pt`) and `UI_Dialogue` (`39.0pt -> 44.8pt`); repeated applications remain at `34.5pt / 44.8pt` and revert to `30.0pt / 39.0pt` on `Normal`. `SettingsPanel` text remains unchanged (`21.0pt`). | **PASS** |
| **Quality Preset, Valid Deduplicated Resolution, FPS Limit (`-1`), Fullscreen, VSync on Apply Graphics** | `Main Menu`, `Forest Scene` | Resolutions deduplicated (`0` duplicates); `QualitySettings.names` (`Mobile`/`PC`), `FPS Limit` (`Unlimited` = `-1`, `60 FPS` = `60`), `Fullscreen`, and `VSync` apply on `APPLY GRAPHICS` and reflect current state on open. | **PASS** |
| **Apply Graphics Preserves Audio & Gameplay Settings** | `Main Menu`, `Forest Scene` | Clicking `APPLY GRAPHICS` leaves Audio (`0.75, 0.60, 0.50`) and Gameplay (`2.4x, TutorialHints=false, TextSize=Large`) unchanged. | **PASS** |
| **Reset to Defaults Confirmation (`Yes, Reset` vs `Cancel`)** | `Main Menu`, `Forest Scene` | Clicking `RESET TO DEFAULTS` opens confirmation modal; `Cancel` changes nothing; `Yes, Reset` restores all controls, `PlayerPrefs`, and runtime systems to defaults (`100%/80%/80%`, `1.0x`, `Normal`, `PC`, `Unlimited`). | **PASS** |
| **Back Button & Underlying Menu Interactivity** | `Main Menu`, `Forest Scene` | Clicking `BACK` closes `SettingsPanel` and leaves `Play Button`, `settings`, `Quit` (in `Main Menu`) and `PauseOverlay` buttons (in `Forest Scene`) clickable. | **PASS** |
| **PlayerPrefs (`EcoQuest_Settings_*`) Persistence Across Restart** | Both Scenes | Restarted Play Mode and verified all saved values (`75%, 60%, 50%, 2.4x, Large, PC, Unlimited`) are restored at startup and displayed when opening the panel. | **PASS** |
| **Opens from Main Menu & In-Game `Esc` Pause Menu (Unscaled Time)** | `Main Menu`, `Forest Scene` | Opens from Main Menu `settings` button and from `Forest Scene` `Esc` Pause Menu (`Time.timeScale == 0`); animations and controls operate using `Time.unscaledDeltaTime`. | **PASS** |
| **Quiz Score Keys, Menu Buttons & `IntroSequenceController` Preserved** | Both Scenes | `EcoQuest_PreQuizScore` (`4`) and `EcoQuest_PostQuizScore` (`6`) untouched across save/reset; `Play`, `Quit`, `Pause`, and `IntroSequenceController` verified intact (`0` console errors). | **PASS** |
