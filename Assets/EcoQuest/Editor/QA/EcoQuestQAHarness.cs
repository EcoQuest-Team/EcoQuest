#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

namespace EcoQuest.QA
{
    public static class EcoQuestQAHarness
    {
        public const string ReportBaseDir = @"C:\EcoQuest.gg\Reports\QA\2026-11-11";

        // Save keys isolated to ensure independent assembly compilation
        public const string Key_TabTipDone = "EcoQuest_TabTipDone";
        public const string Key_SeenInfoCards = "EcoQuest_SeenInfoCards";
        public const string Key_PreQuizScore = "EcoQuest_PreQuizScore";
        public const string Key_SequentialWASD = "SequentialWASD_TutorialCompleted";
        public const string Key_ForestQuestAccepted = "EcoQuest_ForestQuestAccepted";
        public const string Key_ForestTrashQuestDone = "EcoQuest_ForestTrashQuestDone";
        public const string Key_ForestRewardClaimed = "EcoQuest_ForestRewardClaimed";
        public const string Key_ForestExplorerGateNotified = "EcoQuest_ForestExplorerGateNotified";
        public const string Key_CityBonusPending = "EcoQuest_City_BonusPending";
        public const string AutoResetPrefKey = "EcoQuest_AutoResetTutorialOnPlay";

        public enum Persona
        {
            FreshPlayer_A,
            TutorialDone_B,
            ForestDone_C,
            MidQuest_D,
            RichMaxed_E,
            LowEndSettings_F
        }

        [MenuItem("Tools/EcoQuest/QA/Set Persona/A - Brand New Player")]
        public static void SetPersonaA() => ApplyPersona(Persona.FreshPlayer_A);

        [MenuItem("Tools/EcoQuest/QA/Set Persona/B - Tutorial Done")]
        public static void SetPersonaB() => ApplyPersona(Persona.TutorialDone_B);

        [MenuItem("Tools/EcoQuest/QA/Set Persona/C - Forest Quest Done")]
        public static void SetPersonaC() => ApplyPersona(Persona.ForestDone_C);

        [MenuItem("Tools/EcoQuest/QA/Set Persona/D - Mid Quest (City Active)")]
        public static void SetPersonaD() => ApplyPersona(Persona.MidQuest_D);

        [MenuItem("Tools/EcoQuest/QA/Set Persona/E - Rich and Maxed Inventory")]
        public static void SetPersonaE() => ApplyPersona(Persona.RichMaxed_E);

        [MenuItem("Tools/EcoQuest/QA/Set Persona/F - Low-End Quality")]
        public static void SetPersonaF() => ApplyPersona(Persona.LowEndSettings_F);

        public static void ApplyPersona(Persona persona)
        {
            switch (persona)
            {
                case Persona.FreshPlayer_A:
                    ClearAllProgressKeys();
                    EditorPrefs.SetBool(AutoResetPrefKey, true);
                    PlayerPrefs.SetInt("PlayerCoins", 0);
                    PlayerPrefs.Save();
                    Debug.Log("[QA Harness] Persona A applied: Brand-new player, all tutorial & quest flags wiped.");
                    break;

                case Persona.TutorialDone_B:
                    EditorPrefs.SetBool(AutoResetPrefKey, false);
                    PlayerPrefs.SetInt(Key_SequentialWASD, 1);
                    PlayerPrefs.SetInt(Key_TabTipDone, 1);
                    PlayerPrefs.SetInt(Key_SeenInfoCards, 1);
                    PlayerPrefs.SetInt(Key_PreQuizScore, 3);
                    PlayerPrefs.SetInt("PlayerCoins", 10);
                    PlayerPrefs.Save();
                    Debug.Log("[QA Harness] Persona B applied: Tutorial completed, ready for Forest quest.");
                    break;

                case Persona.ForestDone_C:
                    EditorPrefs.SetBool(AutoResetPrefKey, false);
                    PlayerPrefs.SetInt(Key_SequentialWASD, 1);
                    PlayerPrefs.SetInt(Key_TabTipDone, 1);
                    PlayerPrefs.SetInt(Key_SeenInfoCards, 1);
                    PlayerPrefs.SetInt(Key_PreQuizScore, 3);
                    PlayerPrefs.SetInt(Key_ForestQuestAccepted, 1);
                    PlayerPrefs.SetInt(Key_ForestTrashQuestDone, 1);
                    PlayerPrefs.SetInt(Key_ForestRewardClaimed, 1);
                    PlayerPrefs.SetInt(Key_ForestExplorerGateNotified, 1);
                    PlayerPrefs.SetInt("PlayerCoins", 50);
                    PlayerPrefs.Save();
                    Debug.Log("[QA Harness] Persona C applied: Forest quest done, ready to enter City Scene.");
                    break;

                case Persona.MidQuest_D:
                    EditorPrefs.SetBool(AutoResetPrefKey, false);
                    PlayerPrefs.SetInt(Key_ForestTrashQuestDone, 1);
                    PlayerPrefs.SetInt(Key_ForestRewardClaimed, 1);
                    PlayerPrefs.SetInt(Key_CityBonusPending, 1);
                    PlayerPrefs.SetInt("PlayerCoins", 75);
                    PlayerPrefs.Save();
                    Debug.Log("[QA Harness] Persona D applied: Mid-quest in City Scene.");
                    break;

                case Persona.RichMaxed_E:
                    EditorPrefs.SetBool(AutoResetPrefKey, false);
                    PlayerPrefs.SetInt("PlayerCoins", 9999);
                    PlayerPrefs.SetInt(Key_ForestTrashQuestDone, 1);
                    PlayerPrefs.SetInt(Key_ForestRewardClaimed, 1);
                    PlayerPrefs.Save();
                    Debug.Log("[QA Harness] Persona E applied: Rich player (9999 coins).");
                    break;

                case Persona.LowEndSettings_F:
                    QualitySettings.SetQualityLevel(0, true);
                    Debug.Log("[QA Harness] Persona F applied: Quality level set to Low (0).");
                    break;
            }
        }

        private static void ClearAllProgressKeys()
        {
            string[] keys = new string[]
            {
                Key_TabTipDone, Key_SeenInfoCards, Key_PreQuizScore, Key_SequentialWASD,
                Key_ForestQuestAccepted, Key_ForestTrashQuestDone, Key_ForestRewardClaimed,
                Key_ForestExplorerGateNotified, Key_CityBonusPending
            };
            foreach (var k in keys)
            {
                if (PlayerPrefs.HasKey(k)) PlayerPrefs.DeleteKey(k);
            }
            PlayerPrefs.Save();
        }

        [MenuItem("Tools/EcoQuest/QA/Run Stage 0 - Setup & Game Map Check")]
        public static void RunStage0()
        {
            string mapPath = Path.Combine(ReportBaseDir, "Game_Map.md");
            string backupReg = Path.Combine(ReportBaseDir, "Backups", "PlayerPrefs_backup.reg");
            string manifest = Path.Combine(ReportBaseDir, "baseline_manifest.json");

            bool ok = File.Exists(mapPath) && File.Exists(backupReg) && File.Exists(manifest);
            Debug.Log($"[QA Harness Stage 0] Game_Map: {File.Exists(mapPath)}, BackupReg: {File.Exists(backupReg)}, Manifest: {File.Exists(manifest)} -> Status: {(ok ? "PASS" : "FAIL")}");
        }

        [MenuItem("Tools/EcoQuest/QA/Run Stage 4 - Buttons & UI Functionality Matrix")]
        public static void RunStage4_ButtonsMatrix()
        {
            string outCsv = Path.Combine(ReportBaseDir, "Buttons_Matrix.csv");
            var rows = new List<string>
            {
                "Control Path,Type,Label/Name,Interactable,OnClick Count,Scene/Source,Status,Issue Description"
            };

            var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var btn in buttons)
            {
                string path = GetHierarchyPath(btn.transform);
                string label = btn.GetComponentInChildren<TextMeshProUGUI>(true)?.text ?? btn.GetComponentInChildren<Text>(true)?.text ?? btn.name;
                label = label.Replace(",", " ").Replace("\n", " ").Trim();
                int listeners = btn.onClick.GetPersistentEventCount();
                bool interactable = btn.interactable;
                string status = listeners > 0 ? "PASS" : "WARN_NO_PERSISTENT_LISTENER";
                string issue = listeners == 0 ? "Button has 0 persistent onClick listeners (wired at runtime)" : "OK";

                rows.Add($"\"{path}\",Button,\"{label}\",{interactable},{listeners},\"{btn.gameObject.scene.name}\",{status},\"{issue}\"");
            }

            var toggles = UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var tog in toggles)
            {
                string path = GetHierarchyPath(tog.transform);
                string label = tog.name;
                int listeners = tog.onValueChanged.GetPersistentEventCount();
                rows.Add($"\"{path}\",Toggle,\"{label}\",{tog.interactable},{listeners},\"{tog.gameObject.scene.name}\",PASS,\"OK\"");
            }

            var sliders = UnityEngine.Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var sld in sliders)
            {
                string path = GetHierarchyPath(sld.transform);
                string label = sld.name;
                int listeners = sld.onValueChanged.GetPersistentEventCount();
                rows.Add($"\"{path}\",Slider,\"{label}\",{sld.interactable},{listeners},\"{sld.gameObject.scene.name}\",PASS,\"OK\"");
            }

            File.WriteAllLines(outCsv, rows);
            Debug.Log($"[QA Harness Stage 4] Exported {rows.Count - 1} UI controls to {outCsv}");
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "";
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
#endif