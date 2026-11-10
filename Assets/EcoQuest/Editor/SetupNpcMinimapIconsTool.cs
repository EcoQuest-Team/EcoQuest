using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace EcoQuest.EditorTools
{
    /// <summary>
    /// Idempotent Editor tool to configure MinimapIcon components on shared NPC prefabs and scene instances.
    /// Ranger NPC & Explorer NPC -> Assets/EcoQuest/Sprites/MapIcons/map_npc_ranger.png
    /// Handyman NPC (Shop) -> Assets/EcoQuest/Sprites/MapIcons/map_npc_shop.png
    /// Fountain Guard NPC -> Assets/EcoQuest/Sprites/MapIcons/map_npc_fountain_guard.png
    /// </summary>
    public static class SetupNpcMinimapIconsTool
    {
        public const string RangerSpritePath = "Assets/EcoQuest/Sprites/MapIcons/map_npc_ranger.png";
        public const string ShopSpritePath = "Assets/EcoQuest/Sprites/MapIcons/map_npc_shop.png";
        public const string FountainGuardSpritePath = "Assets/EcoQuest/Sprites/MapIcons/map_npc_fountain_guard.png";

        [MenuItem("Tools/EcoQuest/Setup NPC Minimap Icons")]
        public static void ApplyNpcMinimapIcons()
        {
            Sprite rangerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RangerSpritePath);
            Sprite shopSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShopSpritePath);
            Sprite fountainGuardSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FountainGuardSpritePath);

            if (rangerSprite == null) Debug.LogWarning($"[SetupNpcMinimapIcons] Ranger sprite missing at {RangerSpritePath}");
            if (shopSprite == null) Debug.LogWarning($"[SetupNpcMinimapIcons] Shop sprite missing at {ShopSpritePath}");
            if (fountainGuardSprite == null) Debug.LogWarning($"[SetupNpcMinimapIcons] Fountain Guard sprite missing at {FountainGuardSpritePath}");

            // 1. Configure Ranger & Explorer Prefabs
            string[] rangerAndExplorerPrefabs = new string[]
            {
                "Assets/EcoQuest/Prefabs/NPC/NPC_Ranger.prefab",
                "Assets/EcoQuest/Characters/Explorer/NPC_Explorer.prefab",
                "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var1.prefab",
                "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var2.prefab",
                "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var3.prefab",
                "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var4.prefab",
                "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var5.prefab"
            };

            string[] handymanPrefabs = new string[]
            {
                "Assets/EcoQuest/Characters/Handyman/NPC_Handyman.prefab",
                "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Green.prefab",
                "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Blue.prefab",
                "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Red.prefab",
                "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_NoSatchel.prefab"
            };

            foreach (var path in rangerAndExplorerPrefabs)
            {
                ConfigurePrefab(path, MinimapIcon.IconType.NPC, rangerSprite);
            }

            foreach (var path in handymanPrefabs)
            {
                ConfigurePrefab(path, MinimapIcon.IconType.NPC, shopSprite);
            }

            // 2. Configure Active Scene
            ConfigureSceneInstances(rangerSprite, shopSprite, fountainGuardSprite);

            AssetDatabase.SaveAssets();
            Debug.Log("[SetupNpcMinimapIcons] Successfully configured all NPC Minimap Icons on prefabs and scene.");
        }

        public static void ConfigurePrefab(string prefabPath, MinimapIcon.IconType iconType, Sprite sprite)
        {
            if (!File.Exists(prefabPath)) return;

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) return;

            try
            {
                var icon = root.GetComponent<MinimapIcon>();
                if (icon == null) icon = root.AddComponent<MinimapIcon>();

                icon.type = iconType;
                if (sprite != null) icon.customSprite = sprite;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[SetupNpcMinimapIcons] Updated prefab: {prefabPath} with sprite: {(sprite != null ? sprite.name : "null")}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void ConfigureSceneInstances(Sprite rangerSprite, Sprite shopSprite, Sprite fountainGuardSprite)
        {
            // Ranger / Forest NPC
            var npcInteractions = Object.FindObjectsByType<NPCInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var npc in npcInteractions)
            {
                if (npc == null) continue;
                var icon = npc.GetComponent<MinimapIcon>() ?? npc.gameObject.AddComponent<MinimapIcon>();
                icon.type = MinimapIcon.IconType.NPC;
                if (rangerSprite != null) icon.customSprite = rangerSprite;
                EditorUtility.SetDirty(npc.gameObject);
                Debug.Log($"[SetupNpcMinimapIcons] Configured Ranger on: {npc.gameObject.name}");
            }

            // Explorer NPC in active scene (Explorer_Var2 / Ranger Robin)
            var explorers = Object.FindObjectsByType<NpcExplorerController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var exp in explorers)
            {
                if (exp == null) continue;
                var icon = exp.GetComponent<MinimapIcon>() ?? exp.gameObject.AddComponent<MinimapIcon>();
                icon.type = MinimapIcon.IconType.NPC;
                if (rangerSprite != null) icon.customSprite = rangerSprite;
                EditorUtility.SetDirty(exp.gameObject);
                Debug.Log($"[SetupNpcMinimapIcons] Configured Explorer on: {exp.gameObject.name}");
            }

            // Also check by name "Explorer_Var2" if component is different
            var expGo = GameObject.Find("Explorer_Var2");
            if (expGo != null)
            {
                var icon = expGo.GetComponent<MinimapIcon>() ?? expGo.AddComponent<MinimapIcon>();
                icon.type = MinimapIcon.IconType.NPC;
                if (rangerSprite != null) icon.customSprite = rangerSprite;
                EditorUtility.SetDirty(expGo);
            }

            // Handyman / Shop NPC in active scene
            var handymanAnimators = Object.FindObjectsByType<EcoQuest.Characters.Handyman.NpcConversationAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var handy in handymanAnimators)
            {
                if (handy == null) continue;
                var icon = handy.GetComponent<MinimapIcon>() ?? handy.gameObject.AddComponent<MinimapIcon>();
                icon.type = MinimapIcon.IconType.NPC;
                if (shopSprite != null) icon.customSprite = shopSprite;
                EditorUtility.SetDirty(handy.gameObject);
                Debug.Log($"[SetupNpcMinimapIcons] Configured Handyman on: {handy.gameObject.name}");
            }

            // Fountain Guard NPC in active scene (FountainQuestGiver)
            var fountainGuard = GameObject.Find("FountainQuestGiver");
            if (fountainGuard == null)
            {
                var allGos = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var g in allGos)
                {
                    if (g.name == "FountainQuestGiver" && g.scene.isLoaded)
                    {
                        fountainGuard = g;
                        break;
                    }
                }
            }

            if (fountainGuard != null)
            {
                var icon = fountainGuard.GetComponent<MinimapIcon>() ?? fountainGuard.AddComponent<MinimapIcon>();
                icon.type = MinimapIcon.IconType.NPC;
                if (fountainGuardSprite != null) icon.customSprite = fountainGuardSprite;
                EditorUtility.SetDirty(fountainGuard);

                var markerTr = fountainGuard.transform.Find("FountainQuestOverheadMarker");
                if (markerTr != null)
                {
                    var sr = markerTr.GetComponent<SpriteRenderer>();
                    if (sr != null && fountainGuardSprite != null)
                    {
                        sr.sprite = fountainGuardSprite;
                        EditorUtility.SetDirty(markerTr.gameObject);
                    }
                }
                Debug.Log($"[SetupNpcMinimapIcons] Configured Fountain Guard on: {fountainGuard.name}");
            }

            if (!Application.isPlaying)
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                if (activeScene.isLoaded)
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                }
            }
        }
    }
}
