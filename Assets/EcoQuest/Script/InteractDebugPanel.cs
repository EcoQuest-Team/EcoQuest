#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class InteractDebugPanel : MonoBehaviour
{
    private static InteractDebugPanel instance;
    private bool isVisible = false;
    private float lastLogTime = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (instance != null) return;
        var go = new GameObject("--- InteractDebugPanel ---");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<InteractDebugPanel>();
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        // Toggle with F3
        bool togglePressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            togglePressed = true;
#endif
        if (Input.GetKeyDown(KeyCode.F3))
            togglePressed = true;

        if (togglePressed)
        {
            isVisible = !isVisible;
        }

        // Periodic logging within 6m of Guide
        var lqm = LitterbugQuestManager.Instance;
        var guide = lqm != null ? lqm.QuestGiverTransform : GameObject.Find("FountainQuestGiver")?.transform;
        var player = lqm != null ? lqm.PlayerTransform : GameObject.FindGameObjectWithTag("Player")?.transform;

        if (guide != null && player != null)
        {
            float dist = Vector3.Distance(player.position, guide.position);
            if (dist <= 6.0f && Time.unscaledTime - lastLogTime >= 1.0f)
            {
                lastLogTime = Time.unscaledTime;
                var projCam = WorldInteractionPrompt.GetActiveWorldCamera();
                var prompt = WorldInteractionPrompt.Instance;
                Vector3 toGuide = (guide.position - player.position);
                toGuide.y = 0f;
                Vector3 camFwd = projCam != null ? projCam.transform.forward : Vector3.forward;
                camFwd.y = 0f;
                float dot = Vector3.Dot(camFwd.normalized, toGuide.normalized);

                Debug.Log($"[InteractDebug] Dist: {dist:F2}m | Dot: {dot:F2} | InRadius: {dist <= (lqm != null ? lqm.QuestGiverInteractRadius : 3.5f)} | PromptVisible: {prompt?.IsVisible} | Alpha: {prompt?.CurrentAlpha:F2} | BehindCam: {prompt?.IsProjectedBehindCamera} | EKeyReady: {!LitterbugQuestManager.IsDialogueOpen}");
            }
        }
    }

    private void OnGUI()
    {
        if (!isVisible) return;

        GUI.backgroundColor = new Color(0.08f, 0.12f, 0.10f, 0.90f);
        GUILayout.BeginArea(new Rect(12, 12, 480, 660), GUI.skin.box);
        GUILayout.Label("<color=#4ADE80><b>=== INTERACTION DEBUG PANEL (F3 to Toggle) ===</b></color>");

        var lqm = LitterbugQuestManager.Instance;
        var projCam = WorldInteractionPrompt.GetActiveWorldCamera();
        var mainCamTagged = GameObject.FindGameObjectsWithTag("MainCamera");
        var player = lqm != null ? lqm.PlayerTransform : GameObject.FindGameObjectWithTag("Player")?.transform;
        var guide = lqm != null ? lqm.QuestGiverTransform : GameObject.Find("FountainQuestGiver")?.transform;
        var prompt = WorldInteractionPrompt.Instance;

        // 1. Camera
        GUILayout.Label("<b>1. Camera Projection</b>");
        if (projCam != null)
        {
            bool isMain = projCam == Camera.main;
            GUILayout.Label($"  Active Cam: {projCam.name} (tag: {projCam.tag}, enabled: {projCam.isActiveAndEnabled}, is Main: {isMain})");
        }
        else
        {
            GUILayout.Label("  <color=red>Active Cam: NONE (Warning logged!)</color>");
        }

        string camCountStr = $"Tagged MainCamera count: {mainCamTagged.Length}";
        if (mainCamTagged.Length > 1)
        {
            GUILayout.Label($"  <color=red><b>{camCountStr} (DUPLICATE DETECTED!)</b></color>");
            foreach (var mc in mainCamTagged)
            {
                GUILayout.Label($"    - {mc.name} (scene: {mc.scene.name}, DDOL: {mc.scene.name == "DontDestroyOnLoad"})");
            }
        }
        else
        {
            GUILayout.Label($"  {camCountStr} (Single MainCamera: OK)");
        }

        // 2. Interactables within 6m
        GUILayout.Label("<b>2. Interactables Within 6m</b>");
        if (player != null)
        {
            Vector3 camFwd = projCam != null ? projCam.transform.forward : (player != null ? player.forward : Vector3.forward);
            camFwd.y = 0f;

            // Check Guide
            if (guide != null)
            {
                float d = Vector3.Distance(player.position, guide.position);
                if (d <= 6.0f)
                {
                    Vector3 toTarget = (guide.position - player.position);
                    toTarget.y = 0f;
                    float dot = Vector3.Dot(camFwd.normalized, toTarget.normalized);
                    float rad = lqm != null ? lqm.QuestGiverInteractRadius : 3.5f;
                    bool inRad = d <= rad;
                    bool chosen = inRad && dot >= 0f && !LitterbugQuestManager.IsDialogueOpen;
                    GUILayout.Label($"  Guide: dist={d:F2}m, dot={dot:F2}, inRadius={inRad} (rad={rad:F1}m), CHOSEN: <color={(chosen ? "green" : "yellow")}>{chosen}</color>");
                }
                else
                {
                    GUILayout.Label($"  Guide: out of 6m range (dist={d:F2}m)");
                }
            }

            // Check Litterbugs
            var allBrains = Object.FindObjectsByType<NpcBrain>(FindObjectsSortMode.None);
            int nearbyNpcCount = 0;
            foreach (var b in allBrains)
            {
                if (b == null) continue;
                float d = Vector3.Distance(player.position, b.transform.position);
                if (d <= 6.0f)
                {
                    nearbyNpcCount++;
                    Vector3 toTarget = (b.transform.position - player.position);
                    toTarget.y = 0f;
                    float dot = Vector3.Dot(camFwd.normalized, toTarget.normalized);
                    var lb = b.GetComponent<LitterbugNpc>();
                    float rad = lb != null ? lb.InteractRadius : 3.5f;
                    bool inRad = d <= rad;
                    bool chosen = b.IsQuestTarget && inRad;
                    GUILayout.Label($"  NPC ({b.name}): dist={d:F2}m, dot={dot:F2}, inRadius={inRad}, CHOSEN: {chosen}");
                }
            }
            if (nearbyNpcCount == 0 && (guide == null || Vector3.Distance(player.position, guide.position) > 6.0f))
            {
                GUILayout.Label("  (No interactables within 6m)");
            }
        }
        else
        {
            GUILayout.Label("  <color=red>Player transform is NULL!</color>");
        }

        // 3. Guide Details
        GUILayout.Label("<b>3. Guide Status</b>");
        if (guide != null)
        {
            var col = guide.GetComponent<Collider>();
            var speaker = guide.GetComponent<NpcDialogueSpeakerAnimator>();
            float rad = lqm != null ? lqm.QuestGiverInteractRadius : 3.5f;
            GUILayout.Label($"  Active: {guide.gameObject.activeInHierarchy}, Layer: {LayerMask.LayerToName(guide.gameObject.layer)}, Tag: {guide.tag}");
            GUILayout.Label($"  Collider: {(col != null ? (col.enabled ? "Enabled" : "Disabled") + (col.isTrigger ? " (Trigger)" : " (Solid)") : "None")}");
            GUILayout.Label($"  Quest-Giver Component: {(speaker != null && speaker.enabled ? "NpcDialogueSpeakerAnimator (Active)" : "Missing/Disabled")}, Radius: {rad:F1}m");
        }
        else
        {
            GUILayout.Label("  <color=red>Guide object not found!</color>");
        }

        // 4. Prompt UI
        GUILayout.Label("<b>4. Prompt UI (WorldInteractionPrompt)</b>");
        if (prompt != null)
        {
            var canvas = prompt.GetComponent<Canvas>();
            var cg = prompt.CanvasGroup;
            var rect = prompt.PromptPanelRect;
            GUILayout.Label($"  Active: {prompt.gameObject.activeInHierarchy}, Canvas: {(canvas != null && canvas.enabled)}, SortingOrder: {canvas?.sortingOrder}");
            GUILayout.Label($"  CanvasGroup Alpha: {cg?.alpha:F2}, AnchoredPos: {rect?.anchoredPosition}");
            GUILayout.Label($"  BehindCam: {prompt.IsProjectedBehindCamera}, OffScreen: {prompt.IsProjectedOffScreen}, ScreenPos: {prompt.LastScreenPos}");
        }
        else
        {
            GUILayout.Label("  <color=red>WorldInteractionPrompt instance is NULL!</color>");
        }

        // 5. Input & Dialogue Flags
        GUILayout.Label("<b>5. Input & Control State</b>");
        var pInput = Object.FindFirstObjectByType<PlayerInput>();
        string mapName = pInput != null ? pInput.currentActionMap?.name : "N/A";
        var interactAction = pInput != null ? pInput.actions?.FindAction("Interact") : null;
        string actionState = interactAction != null ? $"Enabled: {interactAction.enabled}, Phase: {interactAction.phase}" : "N/A";
        bool fpsEnabled = lqm != null && lqm.CachedFps != null ? lqm.CachedFps.enabled : true;

        GUILayout.Label($"  ActionMap: {mapName} | E Action: {actionState}");
        GUILayout.Label($"  IsDialogueOpen: {LitterbugQuestManager.IsDialogueOpen} | ControlsEnabled: {fpsEnabled}");
        GUILayout.Label($"  AdvanceDelayLock: {lqm?.AdvanceLockTimer:F2}s | TimeScale: {Time.timeScale:F1}");
        GUILayout.Label($"  EventSystem: {(EventSystem.current != null ? EventSystem.current.name : "None")} | Cursor: {Cursor.lockState} (vis: {Cursor.visible})");

        // 6. Quest State
        GUILayout.Label("<b>6. Quest State</b>");
        string qState = lqm != null ? lqm.QuestState.ToString() : "N/A";
        GUILayout.Label($"  State: {qState} | Guide IsQuestTarget: N/A (Guide is QuestGiver) | Persuaded: N/A");

        // 7. UI Flow & Modals Matrix State
        GUILayout.Label("<b>7. UI Flow & Modals Matrix</b>");
        string currentPhaseStr = UIOrchestrator.Instance != null ? UIOrchestrator.Instance.CurrentPhase.ToString() : "N/A";
        string flowStepStr = NPCInteraction.CurrentFlowStep;
        int modalTotalCount = ModalManager.Instance != null ? ((ModalManager.Instance.ActiveModal != null ? 1 : 0) + ModalManager.Instance.ModalStackCount) : 0;
        float blurWeightVal = ModalManager.Instance != null && ModalManager.Instance.BlurVolume != null ? ModalManager.Instance.BlurVolume.weight : 0f;
        int tabTipDoneVal = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0);
        bool questActiveSig = NPCInteraction.IsAnyQuestActiveSignal;

        GUILayout.Label($"  Phase: {currentPhaseStr} | Flow Step: {flowStepStr}");
        GUILayout.Label($"  Modal Count: {modalTotalCount} | Blur Weight: {blurWeightVal:F2}");
        GUILayout.Label($"  TabTipDone: {tabTipDoneVal} | Quest-Active Signal: {questActiveSig}");

        GUILayout.EndArea();
    }
}
#endif
