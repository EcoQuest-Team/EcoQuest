using System;
using System.Collections.Generic;
using UnityEngine;

public static class OutfitBuilder
{
    public enum PartRole : byte
    {
        Keep = 0,
        Skin = 1,
        Hair = 2,
        Top = 3,
        Secondary = 4,
        Bottom = 5,
        Shoes = 6,
        Accent = 7,
        Glasses = 8
    }

    public enum SleeveMode : byte
    {
        Default = 0,
        ShortSleeve = 1,
        RaglanTwoTone = 2
    }

    [Serializable]
    public class GeneratedOutfit
    {
        public int seed;
        public OutfitArchetype archetype;
        public ModelColoringMethod coloringMethod;
        public string paletteSummary;
        public Color skinColor;
        public Color hairColor;
        public Color topColor;
        public Color secondaryColor;
        public Color bottomColor;
        public Color shoeColor;
        public Color accentColor;
        public float topBottomLightnessDiff;
        public bool hasStripes;
        public bool openJacket;
        public SleeveMode sleeveMode;
        public float uniformScale = 1f;
        public float bodyWidthScale = 1f;
        public List<AccessoryId> accessories = new List<AccessoryId>();
        public string signature;
    }

    private const string AccessoryContainerName = "--- Outfit Accessories ---";
    private const int TexSize = 256;

    // Exact verified submesh-to-role map for all 10 Polyperfect M rigs
    private static readonly Dictionary<string, PartRole[]> PolySlotMap = new Dictionary<string, PartRole[]>
    {
        { "girl-winter",       new[]{ PartRole.Shoes, PartRole.Bottom, PartRole.Top, PartRole.Accent, PartRole.Secondary, PartRole.Skin, PartRole.Hair } },
        { "man-coat",          new[]{ PartRole.Shoes, PartRole.Bottom, PartRole.Top, PartRole.Secondary, PartRole.Skin, PartRole.Hair } },
        { "man-doctor",        new[]{ PartRole.Shoes, PartRole.Bottom, PartRole.Top, PartRole.Secondary, PartRole.Secondary, PartRole.Hair, PartRole.Skin, PartRole.Glasses } },
        { "man-fire",          new[]{ PartRole.Shoes, PartRole.Bottom, PartRole.Accent, PartRole.Top, PartRole.Skin, PartRole.Hair } },
        { "man-lord",          new[]{ PartRole.Shoes, PartRole.Bottom, PartRole.Top, PartRole.Accent, PartRole.Hair, PartRole.Skin } },
        { "man-police",        new[]{ PartRole.Bottom, PartRole.Top, PartRole.Skin, PartRole.Accent, PartRole.Glasses, PartRole.Secondary } },
        { "man-sir",           new[]{ PartRole.Shoes, PartRole.Bottom, PartRole.Top, PartRole.Secondary, PartRole.Skin, PartRole.Hair } },
        { "man-swat",          new[]{ PartRole.Bottom, PartRole.Top, PartRole.Accent, PartRole.Skin } },
        { "woman-pirate",      new[]{ PartRole.Shoes, PartRole.Bottom, PartRole.Top, PartRole.Secondary, PartRole.Accent, PartRole.Skin, PartRole.Hair } },
        { "woman-winter",      new[]{ PartRole.Shoes, PartRole.Top, PartRole.Bottom, PartRole.Accent, PartRole.Secondary, PartRole.Skin, PartRole.Hair } }
    };

    private class MeshTexTemplate
    {
        public byte[] boneCat;      // 0=none, 1=Head, 2=Hand, 3=ForeArm, 4=UpperArm, 5=Chest, 6=Leg, 7=Foot
        public byte[] baseRole;     // Keep, Skin, Hair, Top, Bottom, Shoes
        public float[] shade;       // Normalized luminance ratio around role mean
        public float[] normChestX;  // [-1..1] across chest width
        public float[] normChestY;  // [0..1] along torso height
        public bool[] chestFront;   // Front half of chest
        public Color[] origPixels;
    }

    private const string SocketName = "[OutfitSocket]";
    private static readonly Dictionary<Mesh, MeshTexTemplate> TemplateCache = new Dictionary<Mesh, MeshTexTemplate>();
    private static readonly Dictionary<long, Texture2D> OutfitTexCache = new Dictionary<long, Texture2D>();
    private static readonly Dictionary<int, Texture2D> PatternTexCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<AccessoryId, Mesh> ProceduralMeshCache = new Dictionary<AccessoryId, Mesh>();
    private static readonly Dictionary<string, Mesh> CompoundSubMeshCache = new Dictionary<string, Mesh>();
    private static Shader cachedUrpLit;
    private static Material cachedUrpLitTemplate;
    private static int cachedIgnoreRaycastLayer = -2;

    private static Shader UrpLit => cachedUrpLit != null ? cachedUrpLit : (cachedUrpLit = Shader.Find("Universal Render Pipeline/Lit"));
    private static Material UrpLitTemplate
    {
        get
        {
            if (cachedUrpLitTemplate == null)
            {
                cachedUrpLitTemplate = new Material(UrpLit) { name = "NPC_URP_Lit_Template", hideFlags = HideFlags.DontSave };
            }
            return cachedUrpLitTemplate;
        }
    }
    private static int IgnoreRaycastLayer => cachedIgnoreRaycastLayer != -2
        ? cachedIgnoreRaycastLayer
        : (cachedIgnoreRaycastLayer = Mathf.Max(0, LayerMask.NameToLayer("Ignore Raycast")));

    public static ModelColoringMethod ClassifyModelMethod(GameObject npcRoot)
    {
        var smr = npcRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.sharedMesh != null)
        {
            if (smr.sharedMesh.subMeshCount > 1) return ModelColoringMethod.SeparateSlots;
            string mn = smr.sharedMesh.name.ToLowerInvariant();
            if (mn.Contains("mesh_male") || mn.Contains("mesh_female")) return ModelColoringMethod.PaletteTexture;
        }
        return ModelColoringMethod.SingleShadedTexture;
    }

    public static bool HasBuiltInHeadwear(string meshOrPrefabName)
    {
        string n = meshOrPrefabName.ToLowerInvariant();
        return n.Contains("girlwinter") || n.Contains("girl-winter") ||
               n.Contains("womanwinter") || n.Contains("woman-winter") ||
               n.Contains("pirate") || n.Contains("fire") ||
               n.Contains("police") || n.Contains("sir") ||
               n.Contains("swat") || n.Contains("lord") || n.Contains("doctor");
    }

    public static bool HasBuiltInGlasses(string meshOrPrefabName)
    {
        string n = meshOrPrefabName.ToLowerInvariant();
        return n.Contains("doctor") || n.Contains("police") || n.Contains("swat");
    }

    public static void CleanupNpcOutfit(GameObject npcRoot, List<Material> ownedMaterials = null, Material[] defaultSharedMats = null)
    {
        if (npcRoot != null)
        {
            var stale = new List<GameObject>();
            foreach (var t in npcRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == npcRoot.transform) continue;
                if (t.name == SocketName || t.name.StartsWith("[Acc]_") || t.name == AccessoryContainerName)
                    stale.Add(t.gameObject);
            }
            foreach (var go in stale)
            {
                if (go == null) continue;
                if (Application.isPlaying)
                {
                    go.name = "[DestroyedOutfitNode]";
                    go.SetActive(false);
                    go.transform.SetParent(null, false);
                    UnityEngine.Object.Destroy(go);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            if (defaultSharedMats != null && defaultSharedMats.Length > 0)
            {
                var smr = npcRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null) smr.sharedMaterials = defaultSharedMats;
            }
        }

        if (ownedMaterials != null)
        {
            for (int i = 0; i < ownedMaterials.Count; i++)
            {
                var m = ownedMaterials[i];
                if (m == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(m);
                else UnityEngine.Object.DestroyImmediate(m);
            }
            ownedMaterials.Clear();
        }
    }

    public static GeneratedOutfit ApplyToNpc(
        GameObject npcRoot,
        OutfitProfile profile,
        int seed,
        OutfitArchetype archetype,
        bool allowPhone,
        bool allowHandheld = true,
        List<Material> ownedMaterials = null,
        Material[] defaultSharedMats = null)
    {
        if (profile == null || npcRoot == null) return null;
        profile.EnsureDefaultRules();

        CleanupNpcOutfit(npcRoot, ownedMaterials, defaultSharedMats);

        // Hide stray weapon props & remove any MeshCollider on clothing/child renderers
        foreach (var t in npcRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0)
                t.gameObject.SetActive(false);
            var mc = t.GetComponent<MeshCollider>();
            if (mc != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(mc);
                else UnityEngine.Object.DestroyImmediate(mc);
            }
        }

        // Keep all SkinnedMeshRenderers updating offscreen so clothes never pop or freeze near frustum edges
        foreach (var r in npcRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            r.updateWhenOffscreen = true;
        }

        var smr = npcRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        string modelId = smr != null && smr.sharedMesh != null ? smr.sharedMesh.name : npcRoot.name;
        var outfit = GenerateOutfitData(profile, seed, archetype, modelId, ClassifyModelMethod(npcRoot), allowPhone, allowHandheld);

        // Reset root scale to 1 before Animator pose evaluation & bounds calibration
        npcRoot.transform.localScale = Vector3.one;

        // Enforce Animator settings & evaluate resting Idle pose BEFORE measuring or attaching bones (both Edit & Play Mode)
        var anim = npcRoot.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.enabled = true;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var brain = npcRoot.GetComponent<NpcBrain>();
            if (brain != null) brain.ApplyGenderController();
            if (npcRoot.activeInHierarchy && anim.runtimeAnimatorController != null)
            {
                if (Application.isPlaying) anim.Rebind();
                anim.Update(0.001f);
            }
        }

        if (smr != null && smr.sharedMesh != null)
        {
            if (outfit.coloringMethod == ModelColoringMethod.SeparateSlots)
                ApplySeparateSlots(smr, outfit, ownedMaterials);
            else
                ApplyRecoloredTexture(smr, profile, outfit, ownedMaterials);
        }

        if (npcRoot.activeInHierarchy)
        {
            AttachAccessories(npcRoot, smr, anim, profile, outfit, ownedMaterials);
        }

        // Strictly uniform root scale so rotated bones never acquire shear/skew matrices during Walk/Run/Talk
        npcRoot.transform.localScale = Vector3.one * outfit.uniformScale;

        return outfit;
    }

    public static GeneratedOutfit GenerateOutfitData(
        OutfitProfile profile,
        int seed,
        OutfitArchetype archetype,
        string modelName,
        ModelColoringMethod method,
        bool allowPhone,
        bool allowHandheld)
    {
        var rng = new System.Random(seed);
        var rule = profile.GetRule(archetype);
        var o = new GeneratedOutfit
        {
            seed = seed,
            archetype = archetype,
            coloringMethod = method,
            uniformScale = Mathf.Lerp(0.95f, 1.05f, (float)rng.NextDouble()),
            bodyWidthScale = Mathf.Lerp(0.97f, 1.03f, (float)rng.NextDouble())
        };

        // 1. Natural diverse skin & hair
        o.skinColor = profile.skinTones[rng.Next(profile.skinTones.Length)];
        if (archetype == OutfitArchetype.Elder)
        {
            o.hairColor = profile.naturalHairColors[rng.Next(0, 2) == 0 ? 6 : 7]; // Salt-and-pepper or Silver
        }
        else if (archetype == OutfitArchetype.Artist && rng.NextDouble() < 0.45)
        {
            o.hairColor = profile.artistDyedHairColors[rng.Next(profile.artistDyedHairColors.Length)];
        }
        else
        {
            o.hairColor = profile.naturalHairColors[rng.Next(0, 6)];
        }

        // 2. Curated HSV Harmony: Neutral base + Neutral contrast + 1 Accent (max 3 main colors, zero neon)
        int topNeutralIdx = rule.allowedNeutralTopIndices[rng.Next(rule.allowedNeutralTopIndices.Length)];
        int botNeutralIdx = rule.allowedNeutralBottomIndices[rng.Next(rule.allowedNeutralBottomIndices.Length)];
        if (botNeutralIdx == topNeutralIdx)
            botNeutralIdx = (botNeutralIdx + 1) % profile.neutralPalette.Length;

        int accentIdx = rule.allowedAccentIndices[rng.Next(rule.allowedAccentIndices.Length)];
        Color baseTop = profile.neutralPalette[topNeutralIdx];
        Color baseBot = profile.neutralPalette[botNeutralIdx];
        Color accent = ClampMutedHSV(profile.accentPalette[accentIdx]);

        // Analogous or complementary micro-shift for subtle seed variety within harmonious bounds
        Color.RGBToHSV(accent, out float ah, out float asat, out float av);
        float shift = rng.NextDouble() < 0.7 ? ((float)rng.NextDouble() * 0.06f - 0.03f) : 0.0f;
        accent = Color.HSVToRGB(Mathf.Repeat(ah + shift, 1f), Mathf.Clamp(asat, 0.25f, 0.65f), Mathf.Clamp(av, 0.35f, 0.84f));
        o.accentColor = accent;

        bool accentOnTop = rng.NextDouble() < rule.accentOnTopChance;
        o.topColor = accentOnTop ? accent : baseTop;
        o.bottomColor = baseBot;

        // Enforce lightness difference |V(top) - V(bottom)| >= 0.22
        EnforceLightnessContrast(ref o.topColor, ref o.bottomColor, 0.22f, out o.topBottomLightnessDiff);

        // Secondary color: crisp neutral shirt/trim for open-jacket or stripes (stays within the 3-color budget)
        o.openJacket = rng.NextDouble() < rule.openJacketChance;
        o.hasStripes = !o.openJacket && (rng.NextDouble() < rule.stripeOrPatternChance);
        Color.RGBToHSV(o.topColor, out float th, out float ts, out float tv);
        if (o.openJacket || o.hasStripes)
        {
            // Pick stone-white if top is dark, charcoal/navy if top is light
            o.secondaryColor = tv < 0.55f ? profile.neutralPalette[4] : profile.neutralPalette[1];
        }
        else
        {
            // Closed jacket: tonal lapel/cuff match
            o.secondaryColor = Color.HSVToRGB(th, ts * 0.85f, Mathf.Clamp(tv + (tv < 0.5f ? 0.10f : -0.10f), 0.14f, 0.90f));
        }

        // Sleeve variety on textured meshes depending on archetype
        if (archetype == OutfitArchetype.Jogger || archetype == OutfitArchetype.Tourist)
            o.sleeveMode = rng.NextDouble() < 0.65 ? SleeveMode.ShortSleeve : SleeveMode.RaglanTwoTone;
        else if (archetype == OutfitArchetype.Student || archetype == OutfitArchetype.Casual)
            o.sleeveMode = (SleeveMode)rng.Next(0, 3);
        else
            o.sleeveMode = SleeveMode.Default;

        // Shoes: darker version of bottom color or neutral dark/sneaker
        Color.RGBToHSV(o.bottomColor, out float bh, out float bs, out float bv);
        if ((archetype == OutfitArchetype.Jogger || archetype == OutfitArchetype.Student) && rng.NextDouble() < 0.45)
            o.shoeColor = new Color(0.84f, 0.84f, 0.82f); // Clean athletic sneaker
        else if (rng.NextDouble() < 0.5)
            o.shoeColor = Color.HSVToRGB(bh, bs * 0.65f, Mathf.Clamp(bv * 0.52f, 0.11f, 0.32f));
        else
            o.shoeColor = rng.Next(2) == 0 ? new Color(0.16f, 0.15f, 0.15f) : new Color(0.28f, 0.19f, 0.13f);

        string topLabel = accentOnTop ? profile.accentNames[accentIdx] : profile.neutralNames[topNeutralIdx];
        string botLabel = profile.neutralNames[botNeutralIdx];
        string accLabel = profile.accentNames[accentIdx];
        o.paletteSummary = $"{topLabel} top / {botLabel} bottom + {accLabel} accent";

        // 3. Accessories (0-3 per NPC by archetype, avoiding built-in headwear/glasses collisions)
        bool builtInHat = HasBuiltInHeadwear(modelName);
        bool builtInGlass = HasBuiltInGlasses(modelName);
        int targetAccCount = rng.Next(rule.minAccessories, rule.maxAccessories + 1);

        if (archetype == OutfitArchetype.Construction)
        {
            if (!builtInHat) o.accessories.Add(AccessoryId.HardHat);
            o.accessories.Add(AccessoryId.Vest);
        }
        else if (archetype == OutfitArchetype.Delivery)
        {
            if (!builtInHat) o.accessories.Add(AccessoryId.Cap);
            o.accessories.Add(rule.bodyPool[rng.Next(rule.bodyPool.Length)]);
            if (allowPhone && allowHandheld && o.accessories.Count < targetAccCount)
                o.accessories.Add(AccessoryId.Phone);
        }
        else
        {
            // Head candidate
            if (rule.headPool != null && rule.headPool.Length > 0 && o.accessories.Count < targetAccCount && rng.NextDouble() < 0.75)
            {
                var cand = FilterHeadPool(rule.headPool, builtInHat, builtInGlass);
                if (cand.Count > 0) o.accessories.Add(cand[rng.Next(cand.Count)]);
            }
            // If Jogger wants both Cap + Headphones when possible
            if (archetype == OutfitArchetype.Jogger && !builtInHat)
            {
                o.accessories.Clear();
                o.accessories.Add(AccessoryId.Cap);
                o.accessories.Add(AccessoryId.Headphones);
            }
            // Body candidate
            if (rule.bodyPool != null && rule.bodyPool.Length > 0 && o.accessories.Count < targetAccCount)
            {
                o.accessories.Add(rule.bodyPool[rng.Next(rule.bodyPool.Length)]);
            }
            // Hand candidate
            if (allowHandheld && rule.handPool != null && rule.handPool.Length > 0 && o.accessories.Count < targetAccCount)
            {
                var hPool = new List<AccessoryId>();
                foreach (var h in rule.handPool)
                {
                    if (h == AccessoryId.Phone && !allowPhone) continue;
                    hPool.Add(h);
                }
                if (hPool.Count > 0) o.accessories.Add(hPool[rng.Next(hPool.Count)]);
            }
        }

        string accSig = o.accessories.Count > 0 ? string.Join("+", o.accessories) : "None";
        o.signature = $"{archetype}|{topLabel}-{botLabel}-{accLabel}|{accSig}";
        return o;
    }

    private static List<AccessoryId> FilterHeadPool(AccessoryId[] pool, bool builtInHat, bool builtInGlass)
    {
        var list = new List<AccessoryId>();
        foreach (var id in pool)
        {
            bool isHat = id == AccessoryId.Cap || id == AccessoryId.Beanie || id == AccessoryId.HardHat || id == AccessoryId.Headphones;
            bool isGlass = id == AccessoryId.Glasses || id == AccessoryId.Sunglasses;
            if (isHat && builtInHat) continue;
            if (isGlass && builtInGlass) continue;
            list.Add(id);
        }
        return list;
    }

    private static Color ClampMutedHSV(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Clamp(s, 0f, 0.65f), Mathf.Clamp(v, 0.12f, 0.88f));
    }

    private static void EnforceLightnessContrast(ref Color top, ref Color bottom, float minDiff, out float actualDiff)
    {
        Color.RGBToHSV(top, out float th, out float ts, out float tv);
        Color.RGBToHSV(bottom, out float bh, out float bs, out float bv);
        float diff = Mathf.Abs(tv - bv);
        if (diff < minDiff)
        {
            float needed = minDiff - diff + 0.02f;
            if (tv >= bv)
            {
                float subBot = Mathf.Min(bv - 0.14f, needed * 0.45f);
                bv -= subBot;
                tv = Mathf.Min(0.88f, tv + (needed - subBot));
                if (tv - bv < minDiff) bv = Mathf.Max(0.08f, tv - minDiff);
            }
            else
            {
                float subTop = Mathf.Min(tv - 0.14f, needed * 0.45f);
                tv -= subTop;
                bv = Mathf.Min(0.88f, bv + (needed - subTop));
                if (bv - tv < minDiff) tv = Mathf.Max(0.08f, bv - minDiff);
            }
            top = Color.HSVToRGB(th, ts, tv);
            bottom = Color.HSVToRGB(bh, bs, bv);
        }
        actualDiff = Mathf.Abs(tv - bv);
    }

    // =========================================================================
    // PATH A: SEPARATE MATERIAL SLOTS (Polyperfect M Rigs)
    // =========================================================================
    private static void ApplySeparateSlots(SkinnedMeshRenderer smr, GeneratedOutfit o, List<Material> ownedMaterials)
    {
        string meshName = smr.sharedMesh.name.ToLowerInvariant();
        PartRole[] slotRoles = null;
        foreach (var kv in PolySlotMap)
        {
            if (meshName.Contains(kv.Key)) { slotRoles = kv.Value; break; }
        }

        int count = smr.sharedMesh.subMeshCount;
        var newMats = new Material[count];
        var roleMatCache = new Dictionary<PartRole, Material>();

        // On Delivery archetype with built-in police/uniform cap, match Accent slot (cap) to Top color
        Color effectiveAccent = (o.archetype == OutfitArchetype.Delivery) ? o.topColor : o.accentColor;

        for (int i = 0; i < count; i++)
        {
            PartRole role = (slotRoles != null && i < slotRoles.Length) ? slotRoles[i] : PartRole.Top;
            if (!roleMatCache.TryGetValue(role, out Material mat))
            {
                Color roleCol = GetColorForRole(role, o, effectiveAccent);
                mat = CreateUrpFlatMaterial($"{smr.name}_{role}", roleCol, role == PartRole.Glasses ? 0.65f : 0.12f, ownedMaterials);

                if (role == PartRole.Top && o.hasStripes)
                {
                    var patTex = GetOrCreatePatternTexture(o.topColor, o.secondaryColor, o.archetype == OutfitArchetype.OfficeWorker);
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetTexture("_BaseMap", patTex);
                    mat.SetTextureScale("_BaseMap", new Vector2(4f, 4f));
                }
                roleMatCache[role] = mat;
            }
            newMats[i] = mat;
        }
        smr.sharedMaterials = newMats;
    }

    private static Color GetColorForRole(PartRole role, GeneratedOutfit o, Color effectiveAccent)
    {
        switch (role)
        {
            case PartRole.Skin: return o.skinColor;
            case PartRole.Hair: return o.hairColor;
            case PartRole.Top: return o.topColor;
            case PartRole.Secondary: return o.secondaryColor;
            case PartRole.Bottom: return o.bottomColor;
            case PartRole.Shoes: return o.shoeColor;
            case PartRole.Accent: return effectiveAccent;
            case PartRole.Glasses: return new Color(0.12f, 0.13f, 0.15f);
            default: return o.topColor;
        }
    }

    private static Texture2D GetOrCreatePatternTexture(Color cA, Color cB, bool verticalPinstripe)
    {
        int key = HashCode.Combine((Color32)cA, (Color32)cB, verticalPinstripe);
        if (PatternTexCache.TryGetValue(key, out Texture2D cached) && cached != null)
            return cached;

        const int sz = 16;
        var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.DontSave
        };
        var px = new Color[sz * sz];
        for (int y = 0; y < sz; y++)
        {
            for (int x = 0; x < sz; x++)
            {
                bool stripe = verticalPinstripe ? (x % 4 == 0) : ((y / 3) % 2 == 0);
                px[y * sz + x] = stripe ? Color.Lerp(cA, cB, 0.65f) : cA;
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, false);
        PatternTexCache[key] = tex;
        return tex;
    }

    // =========================================================================
    // PATH B: PALETTE / UV-REGION TEXTURE RECOLORING (Point Filtered, URP/Lit)
    // =========================================================================
    private static void ApplyRecoloredTexture(SkinnedMeshRenderer smr, OutfitProfile profile, GeneratedOutfit o, List<Material> ownedMaterials)
    {
        Mesh mesh = smr.sharedMesh;
        long texKey = ((long)mesh.GetInstanceID() << 32) ^ (uint)HashCode.Combine(o.seed, (int)o.archetype, (int)o.sleeveMode, o.openJacket, o.hasStripes);

        if (!OutfitTexCache.TryGetValue(texKey, out Texture2D recolored) || recolored == null)
        {
            if (!TemplateCache.TryGetValue(mesh, out MeshTexTemplate tpl))
            {
                Texture srcTex = ResolveCanonicalBaseTexture(smr, profile);
                if (srcTex == null) return;
                tpl = BuildMeshTexTemplate(smr, srcTex);
                TemplateCache[mesh] = tpl;
            }

            var outPixels = new Color[TexSize * TexSize];
            for (int i = 0; i < outPixels.Length; i++)
            {
                PartRole role = (PartRole)tpl.baseRole[i];
                byte bCat = tpl.boneCat[i];

                if (role == PartRole.Keep || bCat == 0)
                {
                    outPixels[i] = tpl.origPixels[i];
                    continue;
                }

                // Apply per-archetype sleeve, stripe, and open-jacket rules on Top region
                if (role == PartRole.Top)
                {
                    if (o.sleeveMode == SleeveMode.ShortSleeve && bCat == 3)
                        role = PartRole.Skin;
                    else if (o.sleeveMode == SleeveMode.RaglanTwoTone && (bCat == 3 || bCat == 4))
                        role = PartRole.Secondary;
                    else if (o.openJacket && bCat == 5 && tpl.chestFront[i] && Mathf.Abs(tpl.normChestX[i]) < 0.34f)
                        role = PartRole.Secondary;
                    else if (o.hasStripes && (bCat == 5 || bCat == 4) && (Mathf.FloorToInt(tpl.normChestY[i] * 9f) % 2 == 1))
                        role = PartRole.Secondary;
                }

                Color target = GetColorForRole(role, o, o.accentColor);
                float s = tpl.shade[i];
                outPixels[i] = new Color(
                    Mathf.Clamp01(target.r * s),
                    Mathf.Clamp01(target.g * s),
                    Mathf.Clamp01(target.b * s),
                    1f
                );
            }

            recolored = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                name = $"{mesh.name}_Outfit_{o.seed}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            recolored.SetPixels(outPixels);
            recolored.Apply(false, false);
            OutfitTexCache[texKey] = recolored;
        }

        // Clone once from SRP Batcher-friendly URP/Lit template (_BaseColor = white, _BaseMap = cached recolored tex)
        var mat = CreateUrpFlatMaterial($"{mesh.name}_Mat_{o.seed}", Color.white, 0.12f, ownedMaterials);
        mat.SetTexture("_BaseMap", recolored);
        smr.sharedMaterial = mat;
    }

    private static Texture ResolveCanonicalBaseTexture(SkinnedMeshRenderer smr, OutfitProfile profile)
    {
        string mn = smr.sharedMesh.name.ToLowerInvariant();
        if (mn.Contains("lowpolyman") && profile.maleCivilianTex != null) return profile.maleCivilianTex;
        if (mn.Contains("femalecivilian") && profile.femaleCivilianTex != null) return profile.femaleCivilianTex;
        if (mn.Contains("mesh_male") && profile.everydayMaleTex != null) return profile.everydayMaleTex;
        if (mn.Contains("mesh_female") && profile.everydayFemaleTex != null) return profile.everydayFemaleTex;
        return smr.sharedMaterial != null ? smr.sharedMaterial.GetTexture("_BaseMap") : null;
    }

    private static Matrix4x4 GetBakedMeshToRootMatrix(Transform npcRoot, SkinnedMeshRenderer smr)
    {
        // BakeMesh(mesh, false) already outputs meter-scaled vertices without smr.lossyScale; avoid multiplying by 0.0091 import scale
        return npcRoot.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
    }

    private static MeshTexTemplate BuildMeshTexTemplate(SkinnedMeshRenderer smr, Texture srcTex)
    {
        var tpl = new MeshTexTemplate
        {
            boneCat = new byte[TexSize * TexSize],
            baseRole = new byte[TexSize * TexSize],
            shade = new float[TexSize * TexSize],
            normChestX = new float[TexSize * TexSize],
            normChestY = new float[TexSize * TexSize],
            chestFront = new bool[TexSize * TexSize],
            origPixels = ReadScaledPixels(srcTex, TexSize)
        };
        for (int i = 0; i < tpl.shade.Length; i++) tpl.shade[i] = 1f;

        Mesh mesh = smr.sharedMesh;
        var verts = mesh.vertices;
        var tris = mesh.triangles;
        var uvs = mesh.uv;
        var bw = mesh.boneWeights;
        var bones = smr.bones;

        // Classify each vertex's dominant bone category and measure chest bounding box in root meter space
        byte[] vertBoneCat = new byte[verts.Length];
        Vector3[] rootPos = new Vector3[verts.Length];
        Mesh baked = new Mesh();
        smr.BakeMesh(baked, false);
        var bVerts = baked.vertices;
        Matrix4x4 bakeToRoot = GetBakedMeshToRootMatrix(smr.transform.root, smr);

        float cMinX = 999f, cMaxX = -999f, cMinY = 999f, cMaxY = -999f, cMidZ = 0f;
        int chestCount = 0;

        for (int v = 0; v < verts.Length; v++)
        {
            rootPos[v] = bakeToRoot.MultiplyPoint3x4(bVerts[v]);
            byte cat = 5; // Default Chest
            if (bw != null && v < bw.Length && bones != null)
            {
                int bi = bw[v].boneIndex0;
                if (bi >= 0 && bi < bones.Length && bones[bi] != null)
                {
                    string bn = bones[bi].name.ToLowerInvariant();
                    if (bn.Contains("head") || bn.Contains("neck")) cat = 1;
                    else if (bn.Contains("hand") || bn.Contains("finger")) cat = 2;
                    else if (bn.Contains("forearm") || bn.Contains("lowerarm")) cat = 3;
                    else if (bn.Contains("upperarm") || bn.Contains("arm") || bn.Contains("shoulder") || bn.Contains("clavicle")) cat = 4;
                    else if (bn.Contains("foot") || bn.Contains("toe")) cat = 7;
                    else if (bn.Contains("upleg") || bn.Contains("leg") || bn.Contains("thigh") || bn.Contains("calf") || bn.Contains("hip") || bn.Contains("pelvis")) cat = 6;
                    else cat = 5;
                }
            }
            vertBoneCat[v] = cat;
            if (cat == 5)
            {
                if (rootPos[v].x < cMinX) cMinX = rootPos[v].x;
                if (rootPos[v].x > cMaxX) cMaxX = rootPos[v].x;
                if (rootPos[v].y < cMinY) cMinY = rootPos[v].y;
                if (rootPos[v].y > cMaxY) cMaxY = rootPos[v].y;
                cMidZ += rootPos[v].z;
                chestCount++;
            }
        }
        UnityEngine.Object.DestroyImmediate(baked);
        if (chestCount > 0) cMidZ /= chestCount;
        float cCenterX = (cMinX + cMaxX) * 0.5f;
        float cHalfW = Mathf.Max(0.05f, (cMaxX - cMinX) * 0.5f);
        float cHeight = Mathf.Max(0.10f, cMaxY - cMinY);

        // Sample reference bare skin color directly from Hand triangles
        Vector3 skinSum = Vector3.zero;
        int skinSamples = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            if (vertBoneCat[tris[t]] == 2)
            {
                Vector2 uv = (uvs[tris[t]] + uvs[tris[t + 1]] + uvs[tris[t + 2]]) / 3f;
                Color sc = SampleUV(tpl.origPixels, uv);
                skinSum += new Vector3(sc.r, sc.g, sc.b);
                skinSamples++;
            }
        }
        Color refSkin = skinSamples > 0
            ? new Color(skinSum.x / skinSamples, skinSum.y / skinSamples, skinSum.z / skinSamples)
            : new Color(0.95f, 0.76f, 0.60f);

        // Rasterize every UV triangle into the 256x256 template
        for (int t = 0; t < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            byte cat = MajorityCat(vertBoneCat[i0], vertBoneCat[i1], vertBoneCat[i2]);
            Vector2 uv0 = uvs[i0] * TexSize, uv1 = uvs[i1] * TexSize, uv2 = uvs[i2] * TexSize;
            Vector2 centroidUV = (uvs[i0] + uvs[i1] + uvs[i2]) / 3f;
            Color triCol = SampleUV(tpl.origPixels, centroidUV);

            Vector3 rp = (rootPos[i0] + rootPos[i1] + rootPos[i2]) / 3f;
            float nx = Mathf.Clamp((rp.x - cCenterX) / cHalfW, -1f, 1f);
            float ny = Mathf.Clamp01((rp.y - cMinY) / cHeight);
            bool front = rp.z >= cMidZ;

            PartRole triRole = ClassifyTriangleRole(cat, triCol, refSkin);

            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x))) - 1, 0, TexSize - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x))) + 1, 0, TexSize - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y))) - 1, 0, TexSize - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y))) + 1, 0, TexSize - 1);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    if (PointNearTriangle(new Vector2(px + 0.5f, py + 0.5f), uv0, uv1, uv2, 0.85f))
                    {
                        int idx = py * TexSize + px;
                        tpl.boneCat[idx] = cat;
                        tpl.baseRole[idx] = (byte)triRole;
                        tpl.normChestX[idx] = nx;
                        tpl.normChestY[idx] = ny;
                        tpl.chestFront[idx] = front;
                    }
                }
            }
        }

        // Dilate 2px at UV island borders so boundary texels never bleed
        for (int pass = 0; pass < 2; pass++)
        {
            byte[] nextCat = (byte[])tpl.boneCat.Clone();
            byte[] nextRole = (byte[])tpl.baseRole.Clone();
            for (int y = 1; y < TexSize - 1; y++)
            {
                for (int x = 1; x < TexSize - 1; x++)
                {
                    int idx = y * TexSize + x;
                    if (tpl.boneCat[idx] != 0) continue;
                    int[] nb = { idx - 1, idx + 1, idx - TexSize, idx + TexSize };
                    foreach (int n in nb)
                    {
                        if (tpl.boneCat[n] != 0)
                        {
                            nextCat[idx] = tpl.boneCat[n];
                            nextRole[idx] = tpl.baseRole[n];
                            tpl.normChestX[idx] = tpl.normChestX[n];
                            tpl.normChestY[idx] = tpl.normChestY[n];
                            tpl.chestFront[idx] = tpl.chestFront[n];
                            break;
                        }
                    }
                }
            }
            tpl.boneCat = nextCat;
            tpl.baseRole = nextRole;
        }

        // Compute per-role mean luminance so subtle baked cloth/skin shading is preserved
        float[] lumSum = new float[9];
        int[] lumCnt = new int[9];
        for (int i = 0; i < tpl.origPixels.Length; i++)
        {
            byte r = tpl.baseRole[i];
            if (r == 0 || tpl.boneCat[i] == 0) continue;
            float lum = Luminance(tpl.origPixels[i]);
            lumSum[r] += lum;
            lumCnt[r]++;
        }
        for (int i = 0; i < tpl.origPixels.Length; i++)
        {
            byte r = tpl.baseRole[i];
            if (r == 0 || lumCnt[r] == 0) continue;
            float mean = Mathf.Max(0.08f, lumSum[r] / lumCnt[r]);
            tpl.shade[i] = Mathf.Clamp(Luminance(tpl.origPixels[i]) / mean, 0.72f, 1.22f);
        }

        return tpl;
    }

    private static PartRole ClassifyTriangleRole(byte cat, Color c, Color refSkin)
    {
        Color.RGBToHSV(c, out _, out float s, out float v);
        float skinDist = Vector3.Distance(new Vector3(c.r, c.g, c.b), new Vector3(refSkin.r, refSkin.g, refSkin.b));
        bool isSkinTone = skinDist < 0.25f && c.r > c.b;

        switch (cat)
        {
            case 2: // Hand
                return PartRole.Skin;
            case 1: // Head / Neck
                if (v > 0.89f && s < 0.09f) return PartRole.Keep; // Eye sclera / teeth
                if (v < 0.14f) return PartRole.Keep;              // Pupils / lash line
                return isSkinTone ? PartRole.Skin : PartRole.Hair;
            case 3: // ForeArm
            case 4: // UpperArm
            case 5: // Chest
                return isSkinTone ? PartRole.Skin : PartRole.Top;
            case 6: // Leg / Hips
                return isSkinTone ? PartRole.Skin : PartRole.Bottom;
            case 7: // Foot
                if (isSkinTone) return PartRole.Skin;
                if (v > 0.92f && s < 0.06f) return PartRole.Keep; // White sneaker sole trim
                return PartRole.Shoes;
            default:
                return PartRole.Top;
        }
    }

    private static byte MajorityCat(byte a, byte b, byte c) => (a == b || a == c) ? a : b;
    private static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

    private static Color SampleUV(Color[] px, Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * TexSize), 0, TexSize - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * TexSize), 0, TexSize - 1);
        return px[y * TexSize + x];
    }

    private static bool PointNearTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float padPx)
    {
        Vector2 v0 = c - a, v1 = b - a, v2 = p - a;
        float dot00 = Vector2.Dot(v0, v0), dot01 = Vector2.Dot(v0, v1), dot02 = Vector2.Dot(v0, v2);
        float dot11 = Vector2.Dot(v1, v1), dot12 = Vector2.Dot(v1, v2);
        float denom = dot00 * dot11 - dot01 * dot01;
        if (Mathf.Abs(denom) < 1e-5f) return Vector2.Distance(p, a) <= padPx;
        float inv = 1f / denom;
        float u = (dot11 * dot02 - dot01 * dot12) * inv;
        float v = (dot00 * dot12 - dot01 * dot02) * inv;
        float tol = -padPx / Mathf.Max(2f, Mathf.Sqrt(Mathf.Max(dot00, dot11)));
        return u >= tol && v >= tol && (u + v) <= (1f - tol);
    }

    private static Color[] ReadScaledPixels(Texture src, int size)
    {
        RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var tmp = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tmp.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var cols = tmp.GetPixels();
        UnityEngine.Object.DestroyImmediate(tmp);
        return cols;
    }

    // =========================================================================
    // ACCESSORIES & HUMANOID BONE ATTACHMENT (Renderer-Bounds Calibrated)
    // =========================================================================
    private struct BodyMetrics
    {
        public Bounds head;
        public Bounds torso;
    }

    private static void AttachAccessories(
        GameObject npcRoot,
        SkinnedMeshRenderer smr,
        Animator anim,
        OutfitProfile profile,
        GeneratedOutfit o,
        List<Material> ownedMaterials)
    {
        if (o.accessories == null || o.accessories.Count == 0 || smr == null) return;
        if (anim == null || !anim.isHuman) return;

        Transform headBone = anim.GetBoneTransform(HumanBodyBones.Head);
        Transform spineBone = anim.GetBoneTransform(HumanBodyBones.Spine);
        Transform chestSub = anim.GetBoneTransform(HumanBodyBones.Chest);
        Transform upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
        Transform chestBone = upperChest ?? chestSub ?? spineBone;
        Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform rLowerArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform lLowerArm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);

        BodyMetrics bm = MeasureBodyMetrics(npcRoot.transform, smr, headBone, spineBone, chestSub, upperChest);

        bool rightHandUsed = false;

        foreach (var accId in o.accessories)
        {
            GameObject accObj = BuildAccessoryVisual(accId, profile, o, ownedMaterials);
            if (accObj == null) continue;
            accObj.name = $"[Acc]_{accId}";
            StripPhysicsAndSetLayer(accObj);

            PositionAndParentAccessory(
                accId, accObj.transform, npcRoot.transform, bm,
                headBone, chestBone,
                rightHandUsed ? lHand : rHand,
                rightHandUsed ? lLowerArm : rLowerArm,
                !rightHandUsed,
                ref rightHandUsed
            );
        }
    }

    private static void StripPhysicsAndSetLayer(GameObject root)
    {
        int layer = IgnoreRaycastLayer;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = layer;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(rb);
                else UnityEngine.Object.DestroyImmediate(rb);
            }
            var col = t.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(col);
                else UnityEngine.Object.DestroyImmediate(col);
            }
        }
    }

    private static Transform GetOrCreateBoneSocket(Transform bone)
    {
        Transform sock = bone.Find(SocketName);
        if (sock == null)
        {
            var go = new GameObject(SocketName) { layer = IgnoreRaycastLayer };
            sock = go.transform;
            sock.SetParent(bone, false);
        }
        sock.localPosition = Vector3.zero;
        sock.localRotation = Quaternion.identity;
        Vector3 ls = bone.lossyScale;
        sock.localScale = new Vector3(
            Mathf.Abs(ls.x) > 1e-5f ? 1f / ls.x : 1f,
            Mathf.Abs(ls.y) > 1e-5f ? 1f / ls.y : 1f,
            Mathf.Abs(ls.z) > 1e-5f ? 1f / ls.z : 1f
        );
        return sock;
    }

    private static void AttachToSocket(Transform acc, Transform bone, Vector3 worldPos, Quaternion worldRot, Vector3 metricScale)
    {
        Transform socket = GetOrCreateBoneSocket(bone);
        acc.SetParent(socket, false);
        acc.localPosition = socket.InverseTransformPoint(worldPos);
        acc.localRotation = Quaternion.Inverse(socket.rotation) * worldRot;
        acc.localScale = metricScale;
    }

    private static BodyMetrics MeasureBodyMetrics(
        Transform npcRoot,
        SkinnedMeshRenderer smr,
        Transform headBone,
        Transform spineBone,
        Transform chestBone,
        Transform upperChestBone)
    {
        var bm = new BodyMetrics
        {
            head = new Bounds(new Vector3(0f, 1.60f, 0f), new Vector3(0.20f, 0.24f, 0.22f)),
            torso = new Bounds(new Vector3(0f, 1.22f, 0f), new Vector3(0.34f, 0.44f, 0.22f))
        };

        Mesh mesh = smr.sharedMesh;
        var bw = mesh.boneWeights;
        var bones = smr.bones;
        if (bw == null || bw.Length == 0 || bones == null) return bm;

        Mesh baked = new Mesh();
        smr.BakeMesh(baked, false);
        var bVerts = baked.vertices;
        Matrix4x4 bakeToRoot = GetBakedMeshToRootMatrix(npcRoot, smr);

        bool hasHead = false, hasTorso = false;
        Bounds hb = default, tb = default;

        for (int i = 0; i < bVerts.Length && i < bw.Length; i++)
        {
            int bi = bw[i].boneIndex0;
            if (bi < 0 || bi >= bones.Length || bones[bi] == null) continue;
            Transform bt = bones[bi];
            Vector3 lp = bakeToRoot.MultiplyPoint3x4(bVerts[i]);

            if (headBone != null && (bt == headBone || bt.IsChildOf(headBone)))
            {
                if (!hasHead) { hb = new Bounds(lp, Vector3.zero); hasHead = true; }
                else hb.Encapsulate(lp);
            }
            else if (bt == spineBone || bt == chestBone || bt == upperChestBone)
            {
                if (!hasTorso) { tb = new Bounds(lp, Vector3.zero); hasTorso = true; }
                else tb.Encapsulate(lp);
            }
        }
        UnityEngine.Object.DestroyImmediate(baked);

        if (hasHead && hb.size.y > 0.05f) bm.head = hb;
        if (hasTorso && tb.size.y > 0.10f) bm.torso = tb;
        return bm;
    }

    private static void PositionAndParentAccessory(
        AccessoryId id,
        Transform acc,
        Transform root,
        BodyMetrics bm,
        Transform headBone,
        Transform chestBone,
        Transform handBone,
        Transform lowerArmBone,
        bool isRightHand,
        ref bool rightHandUsed)
    {
        Quaternion rootRot = root.rotation;
        float headW = Mathf.Clamp(bm.head.size.x, 0.14f, 0.28f);
        float torsoW = Mathf.Clamp(bm.torso.size.x, 0.22f, 0.48f);
        float torsoD = Mathf.Clamp(bm.torso.size.z, 0.14f, 0.34f);

        switch (id)
        {
            case AccessoryId.Cap:
            case AccessoryId.HardHat:
            case AccessoryId.Beanie:
            {
                if (headBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                float fit = Mathf.Clamp(headW / 0.20f, 0.78f, 1.22f);
                Vector3 localPos = new Vector3(bm.head.center.x, Mathf.Lerp(bm.head.center.y, bm.head.max.y, 0.38f), bm.head.center.z);
                AttachToSocket(acc, headBone, root.TransformPoint(localPos), rootRot, Vector3.one * fit);
                break;
            }
            case AccessoryId.Headphones:
            {
                if (headBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                float fit = Mathf.Clamp((headW * 1.08f) / 0.24f, 0.78f, 1.22f);
                Vector3 localPos = new Vector3(bm.head.center.x, bm.head.max.y - 0.132f * fit, bm.head.center.z + 0.015f);
                AttachToSocket(acc, headBone, root.TransformPoint(localPos), rootRot, Vector3.one * fit);
                break;
            }
            case AccessoryId.Glasses:
            case AccessoryId.Sunglasses:
            {
                if (headBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                float fit = Mathf.Clamp((headW * 0.86f) / 0.15f, 0.78f, 1.22f);
                float eyeY = Mathf.Lerp(bm.head.min.y, bm.head.max.y, 0.48f);
                Vector3 localPos = new Vector3(bm.head.center.x, eyeY, bm.head.max.z + 0.008f);
                AttachToSocket(acc, headBone, root.TransformPoint(localPos), rootRot, Vector3.one * fit);
                break;
            }
            case AccessoryId.Backpack:
            {
                if (chestBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                float fit = Mathf.Clamp(torsoW / 0.34f, 0.65f, 1.05f);
                float s = 0.62f * fit;
                Vector3 localPos = new Vector3(bm.torso.center.x, bm.torso.center.y + 0.02f, bm.torso.min.z - (0.16f * s) - 0.018f);
                AttachToSocket(acc, chestBone, root.TransformPoint(localPos), rootRot * Quaternion.Euler(0f, 180f, 0f), Vector3.one * s);
                break;
            }
            case AccessoryId.ShoulderBag:
            case AccessoryId.Tote:
            {
                if (chestBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                float fit = Mathf.Clamp(torsoW / 0.32f, 0.78f, 1.12f);
                float s = (id == AccessoryId.Tote ? 0.38f : 0.72f) * fit;
                Vector3 localPos = new Vector3(bm.torso.max.x + 0.075f, bm.torso.min.y + 0.06f, bm.torso.center.z - 0.05f);
                AttachToSocket(acc, chestBone, root.TransformPoint(localPos), rootRot, Vector3.one * s);
                break;
            }
            case AccessoryId.Scarf:
            {
                if (chestBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                float fit = Mathf.Clamp(torsoW / 0.32f, 0.78f, 1.15f);
                float neckY = bm.head.min.y + 0.01f;
                Vector3 localPos = new Vector3(bm.torso.center.x, neckY, bm.torso.center.z + 0.012f);
                AttachToSocket(acc, chestBone, root.TransformPoint(localPos), rootRot, Vector3.one * fit);
                break;
            }
            case AccessoryId.Tie:
            case AccessoryId.Lanyard:
            {
                if (chestBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                float fit = Mathf.Clamp(torsoW / 0.32f, 0.78f, 1.15f);
                float topChestY = bm.torso.max.y - 0.03f;
                Vector3 localPos = new Vector3(bm.torso.center.x, topChestY, bm.torso.max.z + 0.016f);
                AttachToSocket(acc, chestBone, root.TransformPoint(localPos), rootRot, Vector3.one * fit);
                break;
            }
            case AccessoryId.Vest:
            {
                if (chestBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                Vector3 localPos = bm.torso.center + new Vector3(0f, 0.03f, 0f);
                Vector3 vestScale = new Vector3(torsoW * 1.12f, Mathf.Clamp(bm.torso.size.y * 0.72f, 0.22f, 0.38f), torsoD * 1.16f);
                AttachToSocket(acc, chestBone, root.TransformPoint(localPos), rootRot, vestScale);
                break;
            }
            default: // Hand accessories: Phone, CoffeeCup, ShoppingBag, BookFolder, Umbrella
            {
                if (handBone == null) { DestroyOrImmediate(acc.gameObject); break; }
                Vector3 armDir = (lowerArmBone != null && (handBone.position - lowerArmBone.position).sqrMagnitude > 1e-5f)
                    ? (handBone.position - lowerArmBone.position).normalized
                    : -root.up;
                Vector3 rawSide = root.right * (isRightHand ? 1f : -1f);
                Vector3 gripForward = Vector3.Cross(rawSide, -armDir);
                if (gripForward.sqrMagnitude < 1e-4f) gripForward = root.forward;
                else gripForward.Normalize();
                if (Vector3.Dot(gripForward, root.forward) < 0f) gripForward = -gripForward;
                Vector3 gripOut = Vector3.Cross(-armDir, gripForward).normalized * (isRightHand ? 1f : -1f);
                if (Vector3.Dot(gripOut, rawSide) < 0f) gripOut = -gripOut;

                Vector3 gripPos = handBone.position + armDir * 0.055f + gripOut * 0.045f;
                Vector3 worldPos = gripPos;
                Quaternion worldRot;
                Vector3 scale;

                if (id == AccessoryId.ShoppingBag)
                {
                    worldRot = Quaternion.LookRotation(gripForward, -armDir);
                    scale = Vector3.one * 0.38f;
                }
                else if (id == AccessoryId.BookFolder)
                {
                    worldPos = gripPos + gripOut * 0.015f;
                    worldRot = Quaternion.LookRotation(gripForward, gripOut);
                    scale = Vector3.one * 0.62f;
                }
                else if (id == AccessoryId.CoffeeCup)
                {
                    worldPos = gripPos + gripForward * 0.035f;
                    worldRot = Quaternion.LookRotation(gripForward, Vector3.Lerp(root.up, -armDir, 0.35f));
                    scale = Vector3.one * 0.80f;
                }
                else if (id == AccessoryId.Phone)
                {
                    worldPos = gripPos + gripForward * 0.025f;
                    worldRot = Quaternion.LookRotation(armDir, -gripOut);
                    scale = Vector3.one * 0.95f;
                }
                else // Umbrella (folded)
                {
                    worldRot = Quaternion.LookRotation(gripForward, -armDir);
                    scale = Vector3.one * 0.85f;
                }

                AttachToSocket(acc, handBone, worldPos, worldRot, scale);
                rightHandUsed = true;
                break;
            }
        }
    }

    private static void DestroyOrImmediate(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
        else UnityEngine.Object.DestroyImmediate(obj);
    }

    private static GameObject BuildAccessoryVisual(AccessoryId id, OutfitProfile p, GeneratedOutfit o, List<Material> ownedMaterials)
    {
        Color darkNeutral = new Color(0.15f, 0.16f, 0.18f);
        Color leather = new Color(0.28f, 0.19f, 0.13f);
        Color hiVis = new Color(0.90f, 0.72f, 0.14f);

        switch (id)
        {
            case AccessoryId.Cap:
                Color capCol = o.archetype == OutfitArchetype.Delivery ? o.topColor : o.accentColor;
                return p.capMesh != null
                    ? CreateMeshObj(p.capMesh, capCol, Vector3.zero, ownedMaterials)
                    : CreateMeshObj(GetOrBuildProceduralMesh(id), capCol, Vector3.zero, ownedMaterials);

            case AccessoryId.HardHat:
                return p.hardHatMesh != null
                    ? CreateMeshObj(p.hardHatMesh, hiVis, Vector3.zero, ownedMaterials)
                    : CreateMeshObj(GetOrBuildProceduralMesh(AccessoryId.Beanie), hiVis, Vector3.zero, ownedMaterials);

            case AccessoryId.Headphones:
                return p.headphonesMesh != null
                    ? CreateMultiMatMeshObj(p.headphonesMesh, darkNeutral, o.accentColor, Vector3.zero, ownedMaterials)
                    : CreateMeshObj(GetOrBuildProceduralMesh(id), darkNeutral, Vector3.zero, ownedMaterials);

            case AccessoryId.Backpack:
                Color packCol = o.archetype == OutfitArchetype.Delivery ? o.accentColor : leather;
                if (p.survivalBagMesh != null)
                    return CreateMeshObj(p.survivalBagMesh, packCol, -p.survivalBagMesh.bounds.center, ownedMaterials);
                if (p.backpackMesh != null)
                    return CreateMeshObj(p.backpackMesh, packCol, -p.backpackMesh.bounds.center, ownedMaterials);
                return CreateMeshObj(GetOrBuildProceduralMesh(AccessoryId.ShoulderBag), packCol, Vector3.zero, ownedMaterials);

            case AccessoryId.Tote:
            case AccessoryId.ShoppingBag:
                Color bagCol = id == AccessoryId.Tote ? o.accentColor : new Color(0.86f, 0.80f, 0.68f);
                if (p.toteBagMesh != null)
                {
                    Vector3 pivotOffset = id == AccessoryId.ShoppingBag ? new Vector3(0f, -0.60f, 0f) : -p.toteBagMesh.bounds.center;
                    return CreateMultiMatMeshObj(p.toteBagMesh, bagCol, leather, pivotOffset, ownedMaterials);
                }
                return CreateMeshObj(GetOrBuildProceduralMesh(AccessoryId.ShoulderBag), bagCol, Vector3.zero, ownedMaterials);

            case AccessoryId.Phone:
                return p.phoneMesh != null
                    ? CreateMultiMatMeshObj(p.phoneMesh, darkNeutral, new Color(0.55f, 0.72f, 0.88f), Vector3.zero, ownedMaterials)
                    : CreateMeshObj(GetOrBuildProceduralMesh(id), darkNeutral, Vector3.zero, ownedMaterials);

            case AccessoryId.CoffeeCup:
                return p.coffeeCupMesh != null
                    ? CreateMultiMatMeshObj(p.coffeeCupMesh, new Color(0.92f, 0.90f, 0.86f), new Color(0.62f, 0.44f, 0.28f), new Vector3(0f, -0.075f, 0f), ownedMaterials)
                    : CreateMeshObj(GetOrBuildProceduralMesh(id), new Color(0.92f, 0.90f, 0.86f), Vector3.zero, ownedMaterials);

            case AccessoryId.BookFolder:
                if (p.notebookMesh != null)
                    return CreateMultiMatMeshObj(p.notebookMesh, o.accentColor, new Color(0.93f, 0.92f, 0.88f), Vector3.zero, ownedMaterials);
                if (p.bookMesh != null)
                    return CreateMultiMatMeshObj(p.bookMesh, o.accentColor, new Color(0.93f, 0.92f, 0.88f), -p.bookMesh.bounds.center, ownedMaterials);
                return CreateMeshObj(GetOrBuildProceduralMesh(id), o.accentColor, Vector3.zero, ownedMaterials);

            case AccessoryId.Beanie:
                return CreateMeshObj(GetOrBuildProceduralMesh(id), o.accentColor, Vector3.zero, ownedMaterials);

            case AccessoryId.Glasses:
                return CreateMeshObj(GetOrBuildProceduralMesh(id), darkNeutral, Vector3.zero, ownedMaterials);

            case AccessoryId.Sunglasses:
                return CreateMeshObj(GetOrBuildProceduralMesh(id), new Color(0.09f, 0.09f, 0.11f), Vector3.zero, ownedMaterials);

            case AccessoryId.Scarf:
                return CreateMeshObj(GetOrBuildProceduralMesh(id), o.accentColor, Vector3.zero, ownedMaterials);

            case AccessoryId.Tie:
                return CreateMeshObj(GetOrBuildProceduralMesh(id), o.accentColor, Vector3.zero, ownedMaterials);

            case AccessoryId.Lanyard:
                return BuildLanyardCompound(o.accentColor, ownedMaterials);

            case AccessoryId.Vest:
                return BuildHiVisVestCompound(hiVis, ownedMaterials);

            case AccessoryId.ShoulderBag:
                return BuildShoulderBagCompound(o.archetype == OutfitArchetype.Delivery ? o.topColor : leather, ownedMaterials);

            case AccessoryId.Umbrella:
                return BuildFoldedUmbrellaCompound(o.accentColor, darkNeutral, ownedMaterials);

            default:
                return null;
        }
    }

    private static Material CreateUrpFlatMaterial(string name, Color color, float smoothness = 0.15f, List<Material> ownedMaterials = null)
    {
        var mat = new Material(UrpLitTemplate)
        {
            name = name,
            hideFlags = HideFlags.DontSave
        };
        mat.SetColor("_BaseColor", color);
        mat.SetTexture("_BaseMap", null);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        ownedMaterials?.Add(mat);
        return mat;
    }

    private static GameObject CreateMeshObj(Mesh mesh, Color col, Vector3 visualLocalOffset, List<Material> ownedMaterials)
    {
        var root = new GameObject();
        GameObject target = root;
        if (visualLocalOffset.sqrMagnitude > 0.0001f)
        {
            var child = new GameObject("Visual");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = visualLocalOffset;
            target = child;
        }
        var mf = target.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = target.AddComponent<MeshRenderer>();
        var mat = CreateUrpFlatMaterial("AccMat", col, 0.15f, ownedMaterials);
        var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        mr.sharedMaterials = mats;
        return root;
    }

    private static GameObject CreateMultiMatMeshObj(Mesh mesh, Color colA, Color colB, Vector3 visualLocalOffset, List<Material> ownedMaterials)
    {
        var root = new GameObject();
        GameObject target = root;
        if (visualLocalOffset.sqrMagnitude > 0.0001f)
        {
            var child = new GameObject("Visual");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = visualLocalOffset;
            target = child;
        }
        var mf = target.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = target.AddComponent<MeshRenderer>();
        var matA = CreateUrpFlatMaterial("AccMatA", colA, 0.15f, ownedMaterials);
        var matB = CreateUrpFlatMaterial("AccMatB", colB, 0.15f, ownedMaterials);
        var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = (i == 0) ? matA : matB;
        mr.sharedMaterials = mats;
        return root;
    }

    // =========================================================================
    // PROCEDURAL LOW-POLY ACCESSORY MESHES (Flat URP Lit, Cached, Zero Colliders)
    // =========================================================================
    private static Mesh GetOrBuildProceduralMesh(AccessoryId id)
    {
        if (ProceduralMeshCache.TryGetValue(id, out Mesh cached) && cached != null)
            return cached;

        var verts = new List<Vector3>();
        var tris = new List<int>();

        switch (id)
        {
            case AccessoryId.Beanie:
                AddCylinder(verts, tris, new Vector3(0f, 0.02f, 0f), 0.108f, 0.105f, 0.045f, 10); // Cuff
                AddCylinder(verts, tris, new Vector3(0f, 0.065f, 0f), 0.102f, 0.065f, 0.055f, 10); // Dome
                AddCylinder(verts, tris, new Vector3(0f, 0.11f, 0f), 0.065f, 0.01f, 0.03f, 8);     // Crown
                break;

            case AccessoryId.Glasses:
                AddBox(verts, tris, new Vector3(-0.038f, 0f, 0f), new Vector3(0.052f, 0.034f, 0.008f));
                AddBox(verts, tris, new Vector3(0.038f, 0f, 0f), new Vector3(0.052f, 0.034f, 0.008f));
                AddBox(verts, tris, new Vector3(0f, 0.006f, 0f), new Vector3(0.026f, 0.008f, 0.008f));
                AddBox(verts, tris, new Vector3(-0.065f, 0.004f, -0.055f), new Vector3(0.007f, 0.007f, 0.11f));
                AddBox(verts, tris, new Vector3(0.065f, 0.004f, -0.055f), new Vector3(0.007f, 0.007f, 0.11f));
                break;

            case AccessoryId.Sunglasses:
                AddBox(verts, tris, new Vector3(-0.040f, 0f, 0f), new Vector3(0.058f, 0.038f, 0.010f));
                AddBox(verts, tris, new Vector3(0.040f, 0f, 0f), new Vector3(0.058f, 0.038f, 0.010f));
                AddBox(verts, tris, new Vector3(0f, 0.010f, 0f), new Vector3(0.030f, 0.010f, 0.010f));
                AddBox(verts, tris, new Vector3(-0.068f, 0.005f, -0.055f), new Vector3(0.009f, 0.010f, 0.11f));
                AddBox(verts, tris, new Vector3(0.068f, 0.005f, -0.055f), new Vector3(0.009f, 0.010f, 0.11f));
                break;

            case AccessoryId.Scarf:
                AddCylinder(verts, tris, Vector3.zero, 0.105f, 0.098f, 0.075f, 10);
                AddBox(verts, tris, new Vector3(0.028f, -0.09f, 0.088f), new Vector3(0.055f, 0.16f, 0.022f));
                break;

            case AccessoryId.Tie:
                AddBox(verts, tris, new Vector3(0f, 0f, 0f), new Vector3(0.036f, 0.034f, 0.018f));   // Knot
                AddBox(verts, tris, new Vector3(0f, -0.11f, 0.004f), new Vector3(0.044f, 0.19f, 0.012f)); // Blade
                break;

            default:
                AddBox(verts, tris, Vector3.zero, new Vector3(0.08f, 0.12f, 0.03f));
                break;
        }

        var m = new Mesh { name = $"Proc_{id}", hideFlags = HideFlags.DontSave };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        ProceduralMeshCache[id] = m;
        return m;
    }

    private static GameObject BuildLanyardCompound(Color strapColor, List<Material> ownedMaterials)
    {
        var root = new GameObject();
        AddCachedSubPart(root.transform, "Lanyard_Strap", strapColor, ownedMaterials, (v, t) =>
        {
            AddBox(v, t, new Vector3(-0.032f, -0.06f, 0f), new Vector3(0.012f, 0.14f, 0.008f));
            AddBox(v, t, new Vector3(0.032f, -0.06f, 0f), new Vector3(0.012f, 0.14f, 0.008f));
        });
        AddCachedSubPart(root.transform, "Lanyard_IDCard", new Color(0.93f, 0.94f, 0.96f), ownedMaterials, (v, t) =>
        {
            AddBox(v, t, new Vector3(0f, -0.155f, 0.004f), new Vector3(0.055f, 0.075f, 0.008f));
        });
        return root;
    }

    private static GameObject BuildHiVisVestCompound(Color hiVisColor, List<Material> ownedMaterials)
    {
        var root = new GameObject();
        AddCachedSubPart(root.transform, "Vest_Shell", hiVisColor, ownedMaterials, (v, t) =>
        {
            AddCylinder(v, t, new Vector3(0f, -0.42f, 0f), 0.50f, 0.48f, 0.84f, 10);
        });
        AddCachedSubPart(root.transform, "Vest_Stripes", new Color(0.88f, 0.90f, 0.92f), ownedMaterials, (v, t) =>
        {
            AddCylinder(v, t, new Vector3(0f, -0.22f, 0f), 0.512f, 0.510f, 0.07f, 10);
            AddCylinder(v, t, new Vector3(0f, 0.02f, 0f), 0.500f, 0.498f, 0.07f, 10);
        });
        return root;
    }

    private static GameObject BuildShoulderBagCompound(Color bagColor, List<Material> ownedMaterials)
    {
        var root = new GameObject();
        AddCachedSubPart(root.transform, "Satchel_Body", bagColor, ownedMaterials, (v, t) =>
        {
            AddBox(v, t, Vector3.zero, new Vector3(0.11f, 0.22f, 0.28f));
            AddBox(v, t, new Vector3(0f, 0.16f, 0.08f), new Vector3(0.025f, 0.16f, 0.025f));
            AddBox(v, t, new Vector3(0f, 0.16f, -0.08f), new Vector3(0.025f, 0.16f, 0.025f));
        });
        return root;
    }

    private static GameObject BuildFoldedUmbrellaCompound(Color canopyColor, Color handleColor, List<Material> ownedMaterials)
    {
        var root = new GameObject();
        AddCachedSubPart(root.transform, "Umbrella_Canopy", canopyColor, ownedMaterials, (v, t) =>
        {
            AddCylinder(v, t, new Vector3(0f, -0.42f, 0f), 0.022f, 0.032f, 0.44f, 8);
        });
        AddCachedSubPart(root.transform, "Umbrella_Shaft", handleColor, ownedMaterials, (v, t) =>
        {
            AddCylinder(v, t, new Vector3(0f, -0.02f, 0f), 0.012f, 0.012f, 0.12f, 6);
            AddCylinder(v, t, new Vector3(0f, -0.49f, 0f), 0.006f, 0.012f, 0.07f, 6);
        });
        return root;
    }

    private static void AddCachedSubPart(Transform parent, string cacheKey, Color col, List<Material> ownedMaterials, Action<List<Vector3>, List<int>> buildGeom)
    {
        if (!CompoundSubMeshCache.TryGetValue(cacheKey, out Mesh m) || m == null)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            buildGeom(verts, tris);
            m = new Mesh { name = cacheKey, hideFlags = HideFlags.DontSave };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            CompoundSubMeshCache[cacheKey] = m;
        }
        var go = new GameObject(cacheKey);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterial = CreateUrpFlatMaterial(cacheKey + "_Mat", col, 0.15f, ownedMaterials);
    }

    private static void AddBox(List<Vector3> verts, List<int> tris, Vector3 center, Vector3 size)
    {
        int b = verts.Count;
        Vector3 h = size * 0.5f;
        Vector3[] c =
        {
            center + new Vector3(-h.x, -h.y, -h.z), center + new Vector3(h.x, -h.y, -h.z),
            center + new Vector3(h.x,  h.y, -h.z), center + new Vector3(-h.x,  h.y, -h.z),
            center + new Vector3(-h.x, -h.y,  h.z), center + new Vector3(h.x, -h.y,  h.z),
            center + new Vector3(h.x,  h.y,  h.z), center + new Vector3(-h.x,  h.y,  h.z)
        };
        int[,] faces =
        {
            {0,3,2,1}, {4,5,6,7}, {0,1,5,4}, {2,3,7,6}, {1,2,6,5}, {0,4,7,3}
        };
        for (int f = 0; f < 6; f++)
        {
            int idx = verts.Count;
            verts.Add(c[faces[f, 0]]); verts.Add(c[faces[f, 1]]);
            verts.Add(c[faces[f, 2]]); verts.Add(c[faces[f, 3]]);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 3);
        }
    }

    private static void AddCylinder(List<Vector3> verts, List<int> tris, Vector3 bottomCenter, float rBot, float rTop, float height, int segs)
    {
        int baseIdx = verts.Count;
        for (int i = 0; i < segs; i++)
        {
            float a = (i / (float)segs) * Mathf.PI * 2f;
            float nx = Mathf.Cos(a), nz = Mathf.Sin(a);
            verts.Add(bottomCenter + new Vector3(nx * rBot, 0f, nz * rBot));
            verts.Add(bottomCenter + new Vector3(nx * rTop, height, nz * rTop));
        }
        int topCenterIdx = verts.Count;
        verts.Add(bottomCenter + new Vector3(0f, height, 0f));

        for (int i = 0; i < segs; i++)
        {
            int next = (i + 1) % segs;
            int b0 = baseIdx + i * 2, t0 = b0 + 1;
            int b1 = baseIdx + next * 2, t1 = b1 + 1;
            tris.Add(b0); tris.Add(t0); tris.Add(t1);
            tris.Add(b0); tris.Add(t1); tris.Add(b1);
            tris.Add(t0); tris.Add(topCenterIdx); tris.Add(t1);
        }
    }
}
