using UnityEngine;

/// <summary>
/// ScriptableObject storing kid-friendly control tutorial text (Grade 4-6),
/// instructions, done messages, and key configurations for EcoQuest.
/// </summary>
[CreateAssetMenu(fileName = "ControlTipsData", menuName = "EcoQuest/Control Tips Data")]
public class ControlTipsData : ScriptableObject
{
    [System.Serializable]
    public class TipConfig
    {
        [Tooltip("Fallback header step indicator (e.g. 'STEP 1 OF 4')")]
        public string stepLabel = "STEP 1 OF 4";

        [Tooltip("Kid-friendly headline title")]
        public string title = "Title";

        [Tooltip("Simple, encouraging instruction")]
        [TextArea(2, 4)]
        public string instruction = "Instruction";

        [Tooltip("Positive completion message")]
        public string doneMessage = "Great job!";

        [Tooltip("Key icon sprite (e.g. key_space, key_shift, key_tab, key_tab_blank)")]
        public Sprite keyIcon;

        [Tooltip("Optional text label on key (used if sprite is a blank key, e.g. 'CTRL')")]
        public string keyLabelOverride = "";

        [Tooltip("Whether this step displays the 4-key W A S D cluster")]
        public bool isWASDCluster = false;

        [Tooltip("Required duration in seconds to complete the step")]
        public float requiredDuration = 1.0f;
    }

    [Header("Step 1: MOVE (W A S D)")]
    public TipConfig moveTip = new TipConfig
    {
        stepLabel = "STEP 1 OF 6",
        title = "Move Around!",
        instruction = "Press W A S D to walk.",
        doneMessage = "Great walking!",
        isWASDCluster = true,
        requiredDuration = 1.0f
    };

    [Header("Step 2: JUMP (SPACE)")]
    public TipConfig jumpTip = new TipConfig
    {
        stepLabel = "STEP 2 OF 6",
        title = "Jump!",
        instruction = "Press SPACE to jump over things.",
        doneMessage = "Nice jump!",
        isWASDCluster = false,
        requiredDuration = 0.1f
    };

    [Header("Step 3: SPRINT (SHIFT)")]
    public TipConfig sprintTip = new TipConfig
    {
        stepLabel = "STEP 3 OF 6",
        title = "Run Faster!",
        instruction = "Hold SHIFT while you walk to run super fast.",
        doneMessage = "Zoom! Great running!",
        isWASDCluster = false,
        requiredDuration = 1.5f
    };

    [Header("Step 4: FREE MOUSE (CTRL)")]
    public TipConfig ctrlTip = new TipConfig
    {
        stepLabel = "STEP 4 OF 6",
        title = "Use Your Mouse",
        instruction = "Hold CTRL to show your mouse. Let go to look around again.",
        doneMessage = "You can click anything now!",
        keyLabelOverride = "CTRL",
        isWASDCluster = false,
        requiredDuration = 1.0f
    };

    [Header("Step 5: MAP (M / CLICK MINIMAP)")]
    public TipConfig mapTip = new TipConfig
    {
        stepLabel = "STEP 5 OF 6",
        title = "Open the Map!",
        instruction = "Press M or click the minimap.",
        doneMessage = "Awesome! You can check the map anytime!",
        keyLabelOverride = "M",
        isWASDCluster = false,
        requiredDuration = 0f
    };

    [Header("Step 6: TALK TO NPC")]
    public TipConfig npcTip = new TipConfig
    {
        stepLabel = "STEP 6 OF 6",
        title = "Follow the arrow and talk to the NPC.",
        instruction = "",
        doneMessage = "",
        keyIcon = null,
        keyLabelOverride = "",
        isWASDCluster = false,
        requiredDuration = 0f
    };

    [Header("Post-Bin Intro: QUEST LIST (TAB)")]
    public TipConfig tabTip = new TipConfig
    {
        stepLabel = "TIP",
        title = "Hide Your Quest List",
        instruction = "Press TAB to hide it. Press TAB again to show it.",
        doneMessage = "Quest list hidden and back again. Nice!",
        keyLabelOverride = "TAB",
        isWASDCluster = false,
        requiredDuration = 0f
    };
}
