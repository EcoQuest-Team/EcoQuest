using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PickupTestRunner
{
    private struct SavedItemTransform
    {
        public TrashItem item;
        public DragAndDrop dad;
        public Transform parent;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;
        public bool activeSelf;
    }

    private struct ItemTestRow
    {
        public string itemName;
        public int uprightPass;
        public int uprightTotal;
        public int sidePass;
        public int sideTotal;
        public bool drop01sPass;
        public bool autoSettlePass;
        public float settleTimeSec;
        public bool nearBinPass;
        public bool wrongEjectPass;
        public int cyclesBeforeResetPass;
        public int cyclesAfterResetPass;
        public int failures;
    }

    [MenuItem("EcoQuest/Run Pickup Test", false, 51)]
    public static bool RunPickupTest()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogError("[PickupTest] No Camera found in scene.");
            return false;
        }

        TrashItem[] allItems = Object.FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(allItems, (a, b) => string.CompareOrdinal(a.name, b.name));

        if (allItems.Length == 0)
        {
            Debug.LogError("[PickupTest] No TrashItem instances found in scene.");
            return false;
        }

        // Save camera & player state
        Transform camTransform = cam.transform;
        Vector3 savedCamPos = camTransform.position;
        Quaternion savedCamRot = camTransform.rotation;
        bool prevAllowed = TrashItem.AreTrashItemsAllowed;
        SimulationMode prevSimMode = Physics.simulationMode;

        // First, ensure colliders are fitted to meshes and serialized cleanly in Edit Mode
        FitAllTrashCollidersInScene(allItems);

        // Save all items' state
        List<SavedItemTransform> savedStates = new List<SavedItemTransform>(allItems.Length);
        foreach (var ti in allItems)
        {
            DragAndDrop dad = ti.GetComponent<DragAndDrop>();
            savedStates.Add(new SavedItemTransform
            {
                item = ti,
                dad = dad,
                parent = ti.transform.parent,
                position = ti.transform.position,
                rotation = ti.transform.rotation,
                localScale = ti.transform.localScale,
                activeSelf = ti.gameObject.activeSelf
            });
        }

        // Create temporary test floor in an isolated clear area high above scene geometry so random trees/terrain don't interfere with distance casts
        GameObject testFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testFloor.name = "__PickupTestFloor__";
        Vector3 arenaOrigin = new Vector3(0f, 150f, 0f);
        testFloor.transform.position = arenaOrigin + new Vector3(0f, -0.5f, 0f);
        testFloor.transform.localScale = new Vector3(30f, 1f, 30f);

        GameObject testWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testWall.name = "__PickupTestWall__";
        int binSolidLayer = LayerMask.NameToLayer("BinSolid");
        if (binSolidLayer != -1) testWall.layer = binSolidLayer;
        testWall.transform.position = arenaOrigin + new Vector3(0f, 1f, 6f);
        testWall.transform.localScale = new Vector3(4f, 2f, 0.3f);

        int totalPickupFailures = 0;
        int totalLeftInReleased = 0;
        DragAndDrop.ResetStaticState();
        DragAndDrop.WatchdogRecoveryCount = 0;
        TrashItem.AreTrashItemsAllowed = true;

        List<ItemTestRow> results = new List<ItemTestRow>(allItems.Length);
        float[] testDistances = new float[] { 0.3f, 1.0f, 3.0f };

        try
        {
            Physics.simulationMode = SimulationMode.Script;

            // Deactivate all trash items initially so only the active test item is in the arena
            foreach (var s in savedStates)
            {
                if (s.item != null) s.item.gameObject.SetActive(false);
            }

            foreach (var s in savedStates)
            {
                TrashItem ti = s.item;
                DragAndDrop dad = s.dad;
                if (ti == null || dad == null) continue;

                ti.gameObject.SetActive(true);
                dad.settleQuietDuration = 0.25f;
                dad.settleTimeout = 1.0f;
                dad.FitColliderToMesh();
                ti.ResetTrash();
                dad.EnsurePickable();

                ItemTestRow row = new ItemTestRow
                {
                    itemName = ti.name,
                    uprightTotal = testDistances.Length,
                    sideTotal = testDistances.Length
                };

                // 1A. Upright at 0.3m, 1.0m, 3.0m
                foreach (float dist in testDistances)
                {
                    PlaceItemOnArena(ti, dad, arenaOrigin, Quaternion.identity);
                    AimCameraAtItem(camTransform, dad, dist);
                    Physics.SyncTransforms();

                    bool grabbed = AttemptPickupAndRelease(dad);
                    if (grabbed) row.uprightPass++;
                    else { row.failures++; totalPickupFailures++; Debug.LogError($"[PickupTest] FAIL Upright @ {dist}m on '{ti.name}': {DragAndDrop.LastPickupRejectionReason}"); }
                }

                // 1B. On its side (90 deg X) at 0.3m, 1.0m, 3.0m
                foreach (float dist in testDistances)
                {
                    PlaceItemOnArena(ti, dad, arenaOrigin, Quaternion.Euler(90f, 25f, 0f));
                    AimCameraAtItem(camTransform, dad, dist);
                    Physics.SyncTransforms();

                    bool grabbed = AttemptPickupAndRelease(dad);
                    if (grabbed) row.sidePass++;
                    else { row.failures++; totalPickupFailures++; Debug.LogError($"[PickupTest] FAIL Side @ {dist}m on '{ti.name}': {DragAndDrop.LastPickupRejectionReason}"); }
                }

                // 2A. Drop on the ground, then pick up after 0.1s (while still in ItemState.Released)
                {
                    PlaceItemOnArena(ti, dad, arenaOrigin, Quaternion.identity);
                    AimCameraAtItem(camTransform, dad, 1.2f);
                    Physics.SyncTransforms();

                    dad.TryGrabObject();
                    if (dad.IsGrabbed)
                    {
                        camTransform.position = arenaOrigin + new Vector3(0f, 1.4f, -1.2f);
                        camTransform.rotation = Quaternion.Euler(25f, 0f, 0f);
                        dad.ReleaseObject("test_drop_01s");

                        SimulatePhysicsAndSettle(dad, 0.10f, 0.02f);
                        AimCameraAtItem(camTransform, dad, 1.0f);
                        Physics.SyncTransforms();

                        dad.TryGrabObject();
                        if (dad.IsGrabbed && dad.CurrentItemState == DragAndDrop.ItemState.Held)
                        {
                            row.drop01sPass = true;
                            dad.ReleaseObject("cleanup");
                            dad.EnsurePickable();
                        }
                        else
                        {
                            row.drop01sPass = false;
                            row.failures++;
                            totalPickupFailures++;
                            Debug.LogError($"[PickupTest] FAIL Drop+0.1s pickup on '{ti.name}': state={dad.CurrentItemState}, reason={DragAndDrop.LastPickupRejectionReason}");
                        }
                    }
                    else
                    {
                        row.drop01sPass = false;
                        row.failures++;
                        totalPickupFailures++;
                    }
                }

                // 2B. Verify Released -> Settled automatic transition within <= 1.0s (0 items left in Released after 1.5s, 0 watchdog recoveries)
                {
                    PlaceItemOnArena(ti, dad, arenaOrigin + Vector3.up * 0.15f, Quaternion.Euler(15f, 0f, 10f));
                    AimCameraAtItem(camTransform, dad, 1.2f);
                    Physics.SyncTransforms();

                    dad.TryGrabObject();
                    if (dad.IsGrabbed)
                    {
                        dad.ReleaseObject("test_settle");
                        Rigidbody rb = dad.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.linearVelocity = Vector3.down * 0.5f;
                            rb.angularVelocity = Vector3.zero;
                        }

                        float elapsed = 0f;
                        float settledAt = -1f;
                        const float step = 0.05f;
                        while (elapsed < 1.5f)
                        {
                            Physics.Simulate(step);
                            Physics.SyncTransforms();
                            dad.StepPhysicsAndSettle(step);
                            elapsed += step;
                            if (settledAt < 0f && dad.CurrentItemState == DragAndDrop.ItemState.Settled)
                            {
                                settledAt = elapsed;
                            }
                        }

                        if (dad.CurrentItemState == DragAndDrop.ItemState.Released)
                        {
                            totalLeftInReleased++;
                            row.autoSettlePass = false;
                            row.failures++;
                            Debug.LogError($"[PickupTest] FAIL '{ti.name}' still in Released after 1.5s!");
                        }
                        else
                        {
                            row.autoSettlePass = (dad.CurrentItemState == DragAndDrop.ItemState.Settled && settledAt > 0f && settledAt <= 1.05f);
                            row.settleTimeSec = settledAt > 0f ? settledAt : elapsed;
                            if (!row.autoSettlePass)
                            {
                                row.failures++;
                                Debug.LogError($"[PickupTest] FAIL '{ti.name}' did not auto-settle properly (state={dad.CurrentItemState}, settledAt={settledAt:F2}s)");
                            }
                        }
                    }
                }

                // 3. Drop next to a bin or wall (BinSolid collider right behind/beside item), then pick up
                {
                    Vector3 nearWallPos = testWall.transform.position + new Vector3(0f, -0.8f, -0.28f);
                    ti.transform.SetParent(null);
                    ti.transform.position = nearWallPos;
                    ti.transform.rotation = Quaternion.Euler(0f, 45f, 90f);
                    dad.EnsurePickable();
                    Physics.SyncTransforms();

                    AimCameraAtItemFromDirection(camTransform, dad, new Vector3(0f, 0.35f, -1f).normalized, 0.9f);
                    Physics.SyncTransforms();

                    dad.TryGrabObject();
                    if (dad.IsGrabbed)
                    {
                        dad.ReleaseObject("drop_near_wall");
                        SimulatePhysicsAndSettle(dad, 0.15f, 0.03f);
                        AimCameraAtItemFromDirection(camTransform, dad, new Vector3(0f, 0.35f, -1f).normalized, 0.9f);
                        Physics.SyncTransforms();

                        bool pickedNearBin = AttemptPickupAndRelease(dad);
                        row.nearBinPass = pickedNearBin;
                        if (!pickedNearBin)
                        {
                            row.failures++;
                            totalPickupFailures++;
                            Debug.LogError($"[PickupTest] FAIL Drop near bin/wall on '{ti.name}': {DragAndDrop.LastPickupRejectionReason}");
                        }
                    }
                    else
                    {
                        row.nearBinPass = false;
                        row.failures++;
                        totalPickupFailures++;
                        Debug.LogError($"[PickupTest] FAIL Initial grab near bin/wall on '{ti.name}': {DragAndDrop.LastPickupRejectionReason}");
                    }
                }

                // 4. Eject from a wrong bin (with deposit cooldown active + Released state), then pick up
                {
                    Transform ejectOrigin = testWall.transform;
                    ti.SetDepositCooldown(1.0f);
                    dad.OnEjectedFromBin(ejectOrigin);
                    // Place on arena floor as if landed from ejection while still in Released + cooldown active
                    ti.transform.position = arenaOrigin + new Vector3(0f, 0.05f, 0f);
                    SimulatePhysicsAndSettle(dad, 0.10f, 0.02f);

                    AimCameraAtItem(camTransform, dad, 1.1f);
                    Physics.SyncTransforms();

                    bool pickedAfterEject = AttemptPickupAndRelease(dad);
                    row.wrongEjectPass = pickedAfterEject;
                    if (!pickedAfterEject)
                    {
                        row.failures++;
                        totalPickupFailures++;
                        Debug.LogError($"[PickupTest] FAIL Wrong-bin eject pickup on '{ti.name}': {DragAndDrop.LastPickupRejectionReason}");
                    }
                }

                // 5A. 20 pick/drop cycles before Stop/Play
                {
                    int passBefore = 0;
                    for (int c = 0; c < 20; c++)
                    {
                        Quaternion rot = (c % 2 == 0) ? Quaternion.Euler(0f, c * 18f, 0f) : Quaternion.Euler(90f, c * 18f, 0f);
                        PlaceItemOnArena(ti, dad, arenaOrigin, rot);
                        float dist = testDistances[c % testDistances.Length];
                        AimCameraAtItem(camTransform, dad, dist);
                        Physics.SyncTransforms();

                        dad.TryGrabObject();
                        if (dad.IsGrabbed && dad.CurrentItemState == DragAndDrop.ItemState.Held)
                        {
                            dad.ReleaseObject($"cycle_pre_{c}");
                            SimulatePhysicsAndSettle(dad, 0.05f, 0.025f);
                            passBefore++;
                        }
                        else
                        {
                            row.failures++;
                            totalPickupFailures++;
                            Debug.LogError($"[PickupTest] FAIL Pre-reset cycle {c + 1}/20 on '{ti.name}': {DragAndDrop.LastPickupRejectionReason}");
                        }
                    }
                    row.cyclesBeforeResetPass = passBefore;
                }

                // 5B. Simulated Stop/Play reset, then 20 more pick/drop cycles
                {
                    DragAndDrop.ResetStaticState();
                    TrashItem.AreTrashItemsAllowed = true;
                    ti.ResetTrash();
                    dad.EnsurePickable();

                    int passAfter = 0;
                    for (int c = 0; c < 20; c++)
                    {
                        Quaternion rot = (c % 2 == 0) ? Quaternion.Euler(0f, c * 18f, 0f) : Quaternion.Euler(0f, c * 18f, 90f);
                        PlaceItemOnArena(ti, dad, arenaOrigin, rot);
                        float dist = testDistances[c % testDistances.Length];
                        AimCameraAtItem(camTransform, dad, dist);
                        Physics.SyncTransforms();

                        dad.TryGrabObject();
                        if (dad.IsGrabbed && dad.CurrentItemState == DragAndDrop.ItemState.Held)
                        {
                            dad.ReleaseObject($"cycle_post_{c}");
                            SimulatePhysicsAndSettle(dad, 0.05f, 0.025f);
                            passAfter++;
                        }
                        else
                        {
                            row.failures++;
                            totalPickupFailures++;
                            Debug.LogError($"[PickupTest] FAIL Post-reset cycle {c + 1}/20 on '{ti.name}': {DragAndDrop.LastPickupRejectionReason}");
                        }
                    }
                    row.cyclesAfterResetPass = passAfter;
                }

                dad.EnsurePickable();
                ti.gameObject.SetActive(false);
                results.Add(row);
            }
        }
        finally
        {
            Physics.simulationMode = prevSimMode;

            if (testFloor != null) Object.DestroyImmediate(testFloor);
            if (testWall != null) Object.DestroyImmediate(testWall);

            // Restore all items and camera to original state
            DragAndDrop.ResetStaticState();
            TrashItem.AreTrashItemsAllowed = prevAllowed;

            camTransform.position = savedCamPos;
            camTransform.rotation = savedCamRot;

            foreach (var s in savedStates)
            {
                if (s.item == null) continue;
                s.item.gameObject.SetActive(s.activeSelf);
                s.item.transform.SetParent(s.parent, true);
                s.item.transform.position = s.position;
                s.item.transform.rotation = s.rotation;
                s.item.transform.localScale = s.localScale;
                if (s.dad != null)
                {
                    s.dad.settleQuietDuration = 0.25f;
                    s.dad.settleTimeout = 1.0f;
                    s.dad.FitColliderToMesh();
                    s.dad.EnsurePickable();
                }
            }

            Physics.SyncTransforms();
        }

        int watchdogRecoveries = DragAndDrop.WatchdogRecoveryCount;
        bool overallPass = (totalPickupFailures == 0 && watchdogRecoveries == 0 && totalLeftInReleased == 0);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================================================================================================");
        sb.AppendLine($"[EcoQuest Pickup Test] OVERALL RESULT: {(overallPass ? "PASS" : "FAIL")}");
        sb.AppendLine($"Pass Criteria -> Pickup Failures: {totalPickupFailures} (req 0) | Watchdog Recoveries: {watchdogRecoveries} (req 0) | Left in Released >1.5s: {totalLeftInReleased} (req 0)");
        sb.AppendLine("------------------------------------------------------------------------------------------------------------------------");
        sb.AppendLine(string.Format("{0,-16} | {1,-11} | {2,-11} | {3,-9} | {4,-14} | {5,-9} | {6,-10} | {7,-11} | {8,-6}",
            "Trash Item", "Upright(3)", "OnSide(3)", "Drop 0.1s", "AutoSettle<=1s", "NearBin", "WrongEject", "Cycles 20x2", "Status"));
        sb.AppendLine("------------------------------------------------------------------------------------------------------------------------");

        foreach (var r in results)
        {
            bool rowPassed = r.failures == 0 && r.uprightPass == r.uprightTotal && r.sidePass == r.sideTotal &&
                             r.drop01sPass && r.autoSettlePass && r.nearBinPass && r.wrongEjectPass &&
                             r.cyclesBeforeResetPass == 20 && r.cyclesAfterResetPass == 20;

            sb.AppendLine(string.Format("{0,-16} | {1,-11} | {2,-11} | {3,-9} | {4,-14} | {5,-9} | {6,-10} | {7,-11} | {8,-6}",
                r.itemName,
                $"{r.uprightPass}/{r.uprightTotal} PASS",
                $"{r.sidePass}/{r.sideTotal} PASS",
                r.drop01sPass ? "PASS" : "FAIL",
                r.autoSettlePass ? $"PASS ({r.settleTimeSec:F2}s)" : "FAIL",
                r.nearBinPass ? "PASS" : "FAIL",
                r.wrongEjectPass ? "PASS" : "FAIL",
                $"{r.cyclesBeforeResetPass}/20,{r.cyclesAfterResetPass}/20",
                rowPassed ? "PASS" : "FAIL"));
        }
        sb.AppendLine("========================================================================================================================");

        if (overallPass)
            Debug.Log(sb.ToString());
        else
            Debug.LogError(sb.ToString());

        return overallPass;
    }

    private static int FitAllTrashCollidersInScene(TrashItem[] allItems)
    {
        int count = 0;
        foreach (var ti in allItems)
        {
            if (ti == null) continue;
            DragAndDrop dad = ti.GetComponent<DragAndDrop>();
            if (dad == null) continue;
            Undo.RecordObject(dad, "Update Settle Timers & Fit Collider");
            BoxCollider bc = ti.GetComponent<BoxCollider>();
            if (bc != null) Undo.RecordObject(bc, "Fit Trash BoxCollider to Mesh");
            dad.settleQuietDuration = 0.25f;
            dad.settleTimeout = 1.0f;
            dad.FitColliderToMesh();
            EditorUtility.SetDirty(dad);
            if (bc != null) EditorUtility.SetDirty(bc);
            count++;
        }
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        return count;
    }

    private static void PlaceItemOnArena(TrashItem ti, DragAndDrop dad, Vector3 arenaOrigin, Quaternion rotation)
    {
        if (dad.IsGrabbed) dad.ReleaseObject("reset_place");
        ti.transform.SetParent(null);
        ti.transform.rotation = rotation;
        ti.transform.position = arenaOrigin;
        Physics.SyncTransforms();

        // Place bottom of collider cleanly on the arena floor (y = arenaOrigin.y)
        Bounds wb = dad.GetWorldBounds();
        float bottomY = wb.min.y;
        float deltaY = (arenaOrigin.y + 0.005f) - bottomY;
        ti.transform.position += new Vector3(0f, deltaY, 0f);

        Rigidbody rb = dad.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        dad.EnsurePickable();
        Physics.SyncTransforms();
    }

    private static void AimCameraAtItem(Transform camTransform, DragAndDrop dad, float distance)
    {
        AimCameraAtItemFromDirection(camTransform, dad, new Vector3(0f, 0.35f, -1f).normalized, distance);
    }

    private static void AimCameraAtItemFromDirection(Transform camTransform, DragAndDrop dad, Vector3 fromDir, float distance)
    {
        Bounds wb = dad.GetWorldBounds();
        Vector3 targetCenter = wb.center;
        camTransform.position = targetCenter + fromDir.normalized * distance;
        camTransform.rotation = Quaternion.LookRotation((targetCenter - camTransform.position).normalized, Vector3.up);
    }

    private static bool AttemptPickupAndRelease(DragAndDrop dad)
    {
        dad.TryGrabObject();
        if (!dad.IsGrabbed || dad.CurrentItemState != DragAndDrop.ItemState.Held)
        {
            return false;
        }
        dad.ReleaseObject("test_release");
        dad.EnsurePickable();
        return true;
    }

    private static void SimulatePhysicsAndSettle(DragAndDrop dad, float totalTime, float step)
    {
        float t = 0f;
        while (t < totalTime)
        {
            Physics.Simulate(step);
            Physics.SyncTransforms();
            dad.StepPhysicsAndSettle(step);
            t += step;
        }
    }
}
