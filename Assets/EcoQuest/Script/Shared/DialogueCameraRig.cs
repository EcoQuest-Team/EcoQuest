using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Cinemachine;

namespace EcoQuest.Shared
{
    /// <summary>
    /// Shared DialogueCameraRig used by all NPC dialogues (Guardian, Litterbugs, Ranger Robin).
    /// Handles:
    /// 1. Full player body/head visibility on dialogue enter (enables HiddenFromFirstPerson layer & renderers, hides FP viewmodel/carried item) and guaranteed restoration on exit.
    /// 2. Over-the-shoulder framing (~1.4m behind, 0.52m to side of player eye, FOV 38, look at NPC head/chest midpoint, NPC head in upper third and opposite horizontal third).
    /// 3. Raycast/bounds occlusion check to pick the shoulder side where player backpack/body does not occlude the NPC, plus SphereCast anti-wall clipping.
    /// 4. Per-frame URP Depth of Field focus distance tracking on the NPC head (0->1 over 0.4s in, 1->0 over 0.3s out).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class DialogueCameraRig : MonoBehaviour
    {
        private static DialogueCameraRig instance;
        public static DialogueCameraRig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<DialogueCameraRig>(FindObjectsInactive.Include);
                    if (instance == null)
                    {
                        GameObject go = new GameObject("DialogueCameraRig");
                        var managers = GameObject.Find("--- MANAGERS ---");
                        if (managers != null) go.transform.SetParent(managers.transform, false);
                        instance = go.AddComponent<DialogueCameraRig>();
                    }
                }
                return instance;
            }
        }

        private struct RendererState
        {
            public Renderer renderer;
            public bool wasEnabled;
            public ShadowCastingMode shadowMode;
        }

        private readonly HashSet<string> activeOwners = new HashSet<string>();
        private readonly List<RendererState> savedRendererStates = new List<RendererState>();

        private Camera mainCam;
        private int savedCullingMask;
        private bool cullingMaskSaved = false;
        private GameObject hiddenViewmodelCam;
        private GameObject hiddenCarriedItem;

        private Transform activeNpc;
        private Transform activePlayer;
        private CinemachineCamera sharedVCam;
        private Coroutine dofWeightRoutine;
        private Coroutine fallbackCamBlendRoutine;
        private Vector3 preDialogueCamPos;
        private Quaternion preDialogueCamRot;
        private float preDialogueCamFov = 60f;
        private bool hasSavedFallbackCam = false;
        private float lastComputedSideSign = 1f;

        public static bool IsDialogueCameraActive => instance != null && instance.activeOwners.Count > 0;
        public Transform ActiveNpc => activeNpc;
        public CinemachineCamera VirtualCamera => sharedVCam;
        public float LastSideSign => lastComputedSideSign;

        private void Awake()
        {
            if (instance == null) instance = this;
            else if (instance != this)
            {
                Destroy(gameObject);
                return;
            }
            EnsureVirtualCamera();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                ForceRestoreAll();
                instance = null;
            }
        }

        private CinemachineCamera EnsureVirtualCamera()
        {
            if (sharedVCam != null && sharedVCam.gameObject != null) return sharedVCam;

            Transform child = transform.Find("DialogueSharedVCam");
            GameObject vcamGo;
            if (child != null)
            {
                vcamGo = child.gameObject;
            }
            else
            {
                vcamGo = new GameObject("DialogueSharedVCam");
                vcamGo.transform.SetParent(transform, false);
            }

            sharedVCam = vcamGo.GetComponent<CinemachineCamera>();
            if (sharedVCam == null)
            {
                sharedVCam = vcamGo.AddComponent<CinemachineCamera>();
            }

            sharedVCam.Lens.FieldOfView = 38f;
            sharedVCam.Lens.NearClipPlane = 0.03f;
            sharedVCam.Priority.Value = 0;
            vcamGo.SetActive(false);
            return sharedVCam;
        }

        public static Vector3 GetNpcHeadPosition(Transform npc)
        {
            if (npc == null) return Vector3.zero;
            Animator anim = npc.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                Transform headBone = anim.GetBoneTransform(HumanBodyBones.Head);
                if (headBone != null) return headBone.position + Vector3.up * 0.06f;
            }
            return npc.position + Vector3.up * 1.58f;
        }

        public static Vector3 GetNpcChestMidpoint(Transform npc)
        {
            if (npc == null) return Vector3.zero;
            Vector3 head = GetNpcHeadPosition(npc);
            Vector3 chest = npc.position + Vector3.up * 1.24f;
            Animator anim = npc.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                Transform chestBone = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
                if (chestBone != null) chest = chestBone.position;
            }
            return Vector3.Lerp(chest, head, 0.55f);
        }

        /// <summary>
        /// Enters shared over-the-shoulder dialogue camera, makes full player body/head visible,
        /// frames NPC and player cleanly without backpack occlusion, and starts NPC-focused DOF blur.
        /// </summary>
        public void EnterDialogue(Transform npcTransform, Transform playerTransform = null, float blendDuration = 0.6f, string ownerId = "Dialogue")
        {
            if (npcTransform == null) return;
            if (string.IsNullOrEmpty(ownerId)) ownerId = "Dialogue";

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            activeOwners.Add(ownerId);
            activeNpc = npcTransform;

            if (mainCam == null || !mainCam.isActiveAndEnabled)
            {
                mainCam = Camera.main ?? WorldInteractionPrompt.GetActiveWorldCamera();
            }

            if (playerTransform == null)
            {
                GameObject pObj = GameObject.FindGameObjectWithTag("Player");
                if (pObj != null) playerTransform = pObj.transform;
            }
            activePlayer = playerTransform;

            // 1. Restore full player body/head visibility on enter
            SetPlayerFullBodyVisible(true);

            // 2. Compute over-the-shoulder framing & apply to Cinemachine / MainCamera
            ComputeOverTheShoulderShot(npcTransform, activePlayer, out Vector3 shotPos, out Quaternion shotRot, out float chosenSideSign);
            lastComputedSideSign = chosenSideSign;

            EnsureVirtualCamera();
            sharedVCam.transform.SetPositionAndRotation(shotPos, shotRot);
            sharedVCam.Lens.FieldOfView = 38f;
            sharedVCam.Lens.NearClipPlane = 0.03f;
            if (!sharedVCam.gameObject.activeSelf) sharedVCam.gameObject.SetActive(true);
            sharedVCam.Priority.Value = 45;

            bool usedCinemachine = false;
            if (mainCam != null)
            {
                mainCam.nearClipPlane = 0.03f;
                CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
                if (brain != null && brain.enabled)
                {
                    brain.IgnoreTimeScale = true;
                    brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendDuration);
                    usedCinemachine = true;
                }

                if (!usedCinemachine)
                {
                    if (!hasSavedFallbackCam)
                    {
                        preDialogueCamPos = mainCam.transform.position;
                        preDialogueCamRot = mainCam.transform.rotation;
                        preDialogueCamFov = mainCam.fieldOfView;
                        hasSavedFallbackCam = true;
                    }
                    if (fallbackCamBlendRoutine != null) StopCoroutine(fallbackCamBlendRoutine);
                    fallbackCamBlendRoutine = StartCoroutine(FallbackCameraBlendRoutine(shotPos, shotRot, 38f, blendDuration));
                }
            }

            // 3. Acquire NPC-focused Depth of Field (0 -> 1 over 0.4s)
            UIBackgroundBlur.AcquireDialogueDof(ownerId, npcTransform, new Vector3(0f, 1.58f, 0f));
            if (dofWeightRoutine != null) StopCoroutine(dofWeightRoutine);
            dofWeightRoutine = StartCoroutine(LerpDofWeightRoutine(1f, 0.40f));
        }

        /// <summary>
        /// Exits dialogue camera, restoring first-person player body state, camera blend (0.6s), and DOF weight (1 -> 0 over 0.3s).
        /// Safe to call from a finally block.
        /// </summary>
        public void ExitDialogue(string ownerId = "Dialogue", float blendDuration = 0.6f)
        {
            if (string.IsNullOrEmpty(ownerId)) ownerId = "Dialogue";
            activeOwners.Remove(ownerId);
            UIBackgroundBlur.ReleaseDialogueDof(ownerId);

            if (activeOwners.Count > 0)
            {
                return;
            }

            activeNpc = null;

            // Restore first-person visual state
            SetPlayerFullBodyVisible(false);

            // Ease out camera
            if (sharedVCam != null)
            {
                sharedVCam.Priority.Value = 0;
                sharedVCam.gameObject.SetActive(false);
            }

            if (mainCam != null)
            {
                CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
                if (brain != null && brain.enabled)
                {
                    brain.IgnoreTimeScale = true;
                    brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendDuration);
                }
                else if (hasSavedFallbackCam && isActiveAndEnabled)
                {
                    if (fallbackCamBlendRoutine != null) StopCoroutine(fallbackCamBlendRoutine);
                    fallbackCamBlendRoutine = StartCoroutine(FallbackCameraBlendRoutine(preDialogueCamPos, preDialogueCamRot, preDialogueCamFov, blendDuration));
                    hasSavedFallbackCam = false;
                }
            }

            // Lerp DOF weight 1 -> 0 over 0.3s
            if (isActiveAndEnabled)
            {
                if (dofWeightRoutine != null) StopCoroutine(dofWeightRoutine);
                dofWeightRoutine = StartCoroutine(LerpDofWeightRoutine(0f, 0.30f));
            }
            else
            {
                UIBackgroundBlur.SetWeight(0f);
            }
        }

        public void ForceRestoreAll()
        {
            activeOwners.Clear();
            activeNpc = null;
            UIBackgroundBlur.ClearAllDialogueOwners();
            SetPlayerFullBodyVisible(false);
            if (sharedVCam != null)
            {
                sharedVCam.Priority.Value = 0;
                sharedVCam.gameObject.SetActive(false);
            }
            if (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalOpen)
            {
                UIBackgroundBlur.SetWeight(0f);
            }
        }

        private void LateUpdate()
        {
            if (activeOwners.Count == 0 || activeNpc == null) return;

            // Keep DOF focusDistance locked on the NPC head every frame during dialogue
            Vector3 npcHeadPos = GetNpcHeadPosition(activeNpc);
            UIBackgroundBlur.UpdateDialogueFocusImmediate(activeNpc, npcHeadPos);
        }

        public void ComputeOverTheShoulderShot(Transform npc, Transform player, out Vector3 finalPos, out Quaternion finalRot, out float chosenSideSign)
        {
            Vector3 npcPos = npc.position;
            Vector3 playerPos = player != null ? player.position : (npcPos - npc.forward * 2.0f);

            Vector3 toNpc = npcPos - playerPos;
            toNpc.y = 0f;
            float horizDist = toNpc.magnitude;
            if (horizDist < 0.05f)
            {
                toNpc = player != null ? player.forward : Vector3.forward;
                toNpc.y = 0f;
            }
            toNpc.Normalize();

            // Rotate player horizontally toward NPC so player head/shoulders face the NPC and backpack is on the player's back
            if (player != null)
            {
                player.rotation = Quaternion.LookRotation(toNpc, Vector3.up);
            }

            Vector3 right = Vector3.Cross(Vector3.up, toNpc).normalized;
            Vector3 playerEye = playerPos + Vector3.up * 1.52f;
            Vector3 npcHead = GetNpcHeadPosition(npc);
            Vector3 npcMidpoint = GetNpcChestMidpoint(npc);

            // Evaluate both right (+1) and left (-1) shoulder positions (~1.4m behind, 0.54m to side of player eye)
            Vector3 posRight = EvaluateCandidatePosition(playerEye, toNpc, right, 1f, npcHead, player, out bool occludedRight, out float clearanceRight);
            Vector3 posLeft = EvaluateCandidatePosition(playerEye, toNpc, right, -1f, npcHead, player, out bool occludedLeft, out float clearanceLeft);

            // Pick side where player's backpack/body or environment does NOT occlude the NPC
            if (!occludedRight && occludedLeft)
            {
                chosenSideSign = 1f;
                finalPos = posRight;
            }
            else if (occludedRight && !occludedLeft)
            {
                chosenSideSign = -1f;
                finalPos = posLeft;
            }
            else
            {
                // Neither occluded (or both): pick the side with better clearance
                if (clearanceRight >= clearanceLeft - 0.05f)
                {
                    chosenSideSign = 1f;
                    finalPos = posRight;
                }
                else
                {
                    chosenSideSign = -1f;
                    finalPos = posLeft;
                }
            }

            // Aim so NPC head/chest midpoint sits in the upper vertical third and opposite horizontal third,
            // while player head & shoulder sit in the near horizontal third.
            Vector3 compositionLookTarget = npcMidpoint - right * (chosenSideSign * 0.20f) - Vector3.up * 0.10f;
            Vector3 lookDir = (compositionLookTarget - finalPos).normalized;
            if (lookDir.sqrMagnitude < 0.001f) lookDir = toNpc;
            finalRot = Quaternion.LookRotation(lookDir, Vector3.up);
        }

        private Vector3 EvaluateCandidatePosition(
            Vector3 playerEye,
            Vector3 toNpc,
            Vector3 right,
            float sideSign,
            Vector3 npcHead,
            Transform player,
            out bool isOccluded,
            out float clearanceScore)
        {
            const float backDist = 1.40f;
            const float sideOffset = 0.54f;
            const float upOffset = 0.06f;

            Vector3 desiredPos = playerEye - toNpc * backDist + right * (sideSign * sideOffset) + Vector3.up * upOffset;

            // 1. SphereCast from playerEye to desiredPos to avoid walls, fences, and street poles
            int envMask = ~((1 << 8) | (1 << 7) | (1 << 2)); // Ignore Player(8), HiddenFromFirstPerson(7), IgnoreRaycast(2)
            Vector3 castVec = desiredPos - playerEye;
            float castDist = castVec.magnitude;
            Vector3 candidatePos = desiredPos;
            clearanceScore = castDist;

            if (castDist > 0.05f)
            {
                Vector3 castDir = castVec / castDist;
                if (Physics.SphereCast(playerEye, 0.18f, castDir, out RaycastHit envHit, castDist, envMask, QueryTriggerInteraction.Ignore))
                {
                    float safeDist = Mathf.Max(0.45f, envHit.distance - 0.12f);
                    candidatePos = playerEye + castDir * safeDist;
                    clearanceScore = safeDist;
                }
            }

            // 2. Raycast & bounds check from candidatePos to npcHead to ensure player's backpack/body does not occlude NPC
            isOccluded = false;
            Vector3 toHead = npcHead - candidatePos;
            float distToHead = toHead.magnitude;
            if (distToHead > 0.1f)
            {
                Vector3 rayDir = toHead / distToHead;
                Ray ray = new Ray(candidatePos, rayDir);

                // Check player backpack / torso renderer bounds intersection
                if (player != null)
                {
                    var renderers = player.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in renderers)
                    {
                        if (r == null || !r.enabled || r.name == "Capsule") continue;
                        if (r.name.Contains("Backpack") || r.name.Contains("Jacket") || r.name.Contains("Shirt"))
                        {
                            Bounds b = r.bounds;
                            if (b.IntersectRay(ray, out float hitDist) && hitDist < distToHead)
                            {
                                isOccluded = true;
                                break;
                            }
                        }
                    }
                }

                // Also check physics raycast for any non-NPC environment obstruction between camera and NPC head
                if (!isOccluded && Physics.Raycast(candidatePos, rayDir, out RaycastHit losHit, distToHead - 0.25f, envMask, QueryTriggerInteraction.Ignore))
                {
                    if (activeNpc == null || !losHit.transform.IsChildOf(activeNpc))
                    {
                        isOccluded = true;
                    }
                }
            }

            return candidatePos;
        }

        private void SetPlayerFullBodyVisible(bool fullBodyVisible)
        {
            if (mainCam == null) mainCam = Camera.main ?? WorldInteractionPrompt.GetActiveWorldCamera();

            if (fullBodyVisible)
            {
                if (mainCam != null && !cullingMaskSaved)
                {
                    savedCullingMask = mainCam.cullingMask;
                    int hfLayer = LayerMask.NameToLayer("HiddenFromFirstPerson");
                    if (hfLayer >= 0) mainCam.cullingMask |= (1 << hfLayer);
                    mainCam.cullingMask |= (1 << 7);
                    cullingMaskSaved = true;
                }

                if (mainCam != null)
                {
                    Transform vm = mainCam.transform.Find("ViewmodelCamera");
                    if (vm != null && vm.gameObject.activeSelf)
                    {
                        hiddenViewmodelCam = vm.gameObject;
                        hiddenViewmodelCam.SetActive(false);
                    }
                }

                if (ItemCarry.ActiveCarriedItem != null && ItemCarry.ActiveCarriedItem.activeSelf)
                {
                    hiddenCarriedItem = ItemCarry.ActiveCarriedItem;
                    hiddenCarriedItem.SetActive(false);
                }
                else if (DragAndDrop.CurrentlyGrabbedItem != null && DragAndDrop.CurrentlyGrabbedItem.gameObject.activeSelf)
                {
                    hiddenCarriedItem = DragAndDrop.CurrentlyGrabbedItem.gameObject;
                    hiddenCarriedItem.SetActive(false);
                }

                if (activePlayer != null && savedRendererStates.Count == 0)
                {
                    var renderers = activePlayer.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in renderers)
                    {
                        if (r == null || r.name == "Capsule") continue;
                        if (hiddenViewmodelCam != null && r.transform.IsChildOf(hiddenViewmodelCam.transform)) continue;

                        savedRendererStates.Add(new RendererState
                        {
                            renderer = r,
                            wasEnabled = r.enabled,
                            shadowMode = r.shadowCastingMode
                        });

                        r.enabled = true;
                        if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                        {
                            r.shadowCastingMode = ShadowCastingMode.On;
                        }
                    }
                }
            }
            else
            {
                if (cullingMaskSaved && mainCam != null)
                {
                    mainCam.cullingMask = savedCullingMask;
                    cullingMaskSaved = false;
                }

                if (hiddenViewmodelCam != null)
                {
                    hiddenViewmodelCam.SetActive(true);
                    hiddenViewmodelCam = null;
                }

                if (hiddenCarriedItem != null)
                {
                    hiddenCarriedItem.SetActive(true);
                    hiddenCarriedItem = null;
                }

                if (savedRendererStates.Count > 0)
                {
                    foreach (var st in savedRendererStates)
                    {
                        if (st.renderer != null)
                        {
                            st.renderer.enabled = st.wasEnabled;
                            st.renderer.shadowCastingMode = st.shadowMode;
                        }
                    }
                    savedRendererStates.Clear();
                }
            }
        }

        private IEnumerator LerpDofWeightRoutine(float targetWeight, float duration)
        {
            float startWeight = UIBackgroundBlur.GetWeight();
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = duration > 0.001f ? Mathf.Clamp01(elapsed / duration) : 1f;
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                if (activeNpc != null)
                {
                    UIBackgroundBlur.UpdateDialogueFocusImmediate(activeNpc, GetNpcHeadPosition(activeNpc));
                }
                UIBackgroundBlur.SetWeight(Mathf.Lerp(startWeight, targetWeight, smooth));
                yield return null;
            }

            UIBackgroundBlur.SetWeight(targetWeight);
            dofWeightRoutine = null;
        }

        private IEnumerator FallbackCameraBlendRoutine(Vector3 targetPos, Quaternion targetRot, float targetFov, float duration)
        {
            if (mainCam == null) yield break;
            Vector3 startPos = mainCam.transform.position;
            Quaternion startRot = mainCam.transform.rotation;
            float startFov = mainCam.fieldOfView;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = duration > 0.001f ? Mathf.Clamp01(elapsed / duration) : 1f;
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                if (mainCam != null)
                {
                    mainCam.transform.SetPositionAndRotation(
                        Vector3.Lerp(startPos, targetPos, smooth),
                        Quaternion.Slerp(startRot, targetRot, smooth));
                    mainCam.fieldOfView = Mathf.Lerp(startFov, targetFov, smooth);
                }
                yield return null;
            }

            if (mainCam != null)
            {
                mainCam.transform.SetPositionAndRotation(targetPos, targetRot);
                mainCam.fieldOfView = targetFov;
            }
            fallbackCamBlendRoutine = null;
        }
    }
}
