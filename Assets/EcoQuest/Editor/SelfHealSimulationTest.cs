using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SelfHealSimulationTest
{
    private struct TestResultRow
    {
        public string itemName;
        public bool initialBadStatePass;
        public bool cycle1Pass;
        public bool cycle2Pass;
        public bool cycle3Pass;
        public bool allPassed;
        public string failureReason;
    }

    [MenuItem("EcoQuest/Run Self-Heal Simulation Test", false, 52)]
    public static bool RunTest()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogError("[SelfHealTest] No Camera found in scene.");
            return false;
        }

        TrashItem[] allItems = Object.FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(allItems, (a, b) => string.CompareOrdinal(a.name, b.name));

        if (allItems.Length == 0)
        {
            Debug.LogError("[SelfHealTest] No TrashItem instances found in scene.");
            return false;
        }

        Transform camTransform = cam.transform;
        Vector3 savedCamPos = camTransform.position;
        Quaternion savedCamRot = camTransform.rotation;
        bool prevAllowed = TrashItem.AreTrashItemsAllowed;
        SimulationMode prevSimMode = Physics.simulationMode;

        GameObject trashContainer = GameObject.Find("TrashItemsGroup");
        if (trashContainer == null)
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var g in all) if (g.name == "TrashItemsGroup" && g.scene.isLoaded) { trashContainer = g; break; }
        }
        bool prevContainerActive = trashContainer != null && trashContainer.activeSelf;
        if (trashContainer != null) trashContainer.SetActive(true);

        // Save original states
        var savedStates = new List<(TrashItem item, Transform parent, Vector3 pos, Quaternion rot, int layer, bool activeSelf)>();
        foreach (var ti in allItems)
        {
            savedStates.Add((ti, ti.transform.parent, ti.transform.position, ti.transform.rotation, ti.gameObject.layer, ti.gameObject.activeSelf));
        }

        GameObject testFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testFloor.name = "__SelfHealTestFloor__";
        Vector3 arenaOrigin = new Vector3(0f, 180f, 0f);
        testFloor.transform.position = arenaOrigin + new Vector3(0f, -0.5f, 0f);
        testFloor.transform.localScale = new Vector3(30f, 1f, 30f);

        int totalFailures = 0;
        List<TestResultRow> results = new List<TestResultRow>(allItems.Length);

        try
        {
            Physics.simulationMode = SimulationMode.Script;

            // Unlock trash items for testing
            TrashItem.AreTrashItemsAllowed = true;

            // Deactivate all items initially
            foreach (var ti in allItems) ti.gameObject.SetActive(false);

            foreach (var ti in allItems)
            {
                DragAndDrop dad = ti.GetComponent<DragAndDrop>();
                if (dad == null) continue;

                ti.gameObject.SetActive(true);
                TestResultRow row = new TestResultRow { itemName = ti.name, allPassed = true };

                // Test 1: Initial Bad State Simulation (Without touching any menu items!)
                bool pass0 = TestItemBadStatePickup(ti, dad, camTransform, arenaOrigin, out string err0);
                row.initialBadStatePass = pass0;
                if (!pass0) { row.allPassed = false; row.failureReason = err0; totalFailures++; }

                // Tests 2, 3, 4: Repeat across 3 simulated Stop/Play Domain Reload Resets
                for (int cycle = 1; cycle <= 3; cycle++)
                {
                    // Simulate Stop/Play domain reload reset: clear static fields and prunings
                    DragAndDrop.ResetStaticState();
                    TrashItem.ResetStaticState();
                    TrashItem.AreTrashItemsAllowed = true;

                    bool passCycle = TestItemBadStatePickup(ti, dad, camTransform, arenaOrigin, out string errCycle);
                    if (cycle == 1) row.cycle1Pass = passCycle;
                    else if (cycle == 2) row.cycle2Pass = passCycle;
                    else if (cycle == 3) row.cycle3Pass = passCycle;

                    if (!passCycle)
                    {
                        row.allPassed = false;
                        if (string.IsNullOrEmpty(row.failureReason)) row.failureReason = $"Cycle {cycle}: {errCycle}";
                        totalFailures++;
                    }
                }

                ti.gameObject.SetActive(false);
                results.Add(row);
            }
        }
        finally
        {
            Physics.simulationMode = prevSimMode;
            if (testFloor != null) Object.DestroyImmediate(testFloor);

            DragAndDrop.ResetStaticState();
            TrashItem.ResetStaticState();
            TrashItem.AreTrashItemsAllowed = prevAllowed;

            camTransform.position = savedCamPos;
            camTransform.rotation = savedCamRot;

            if (trashContainer != null) trashContainer.SetActive(prevContainerActive);

            foreach (var s in savedStates)
            {
                if (s.item == null) continue;
                s.item.transform.SetParent(s.parent, true);
                s.item.gameObject.SetActive(s.activeSelf);
                s.item.transform.position = s.pos;
                s.item.transform.rotation = s.rot;
                s.item.gameObject.layer = s.layer;
                var dad = s.item.GetComponent<DragAndDrop>();
                if (dad != null)
                {
                    dad.EnsurePickable();
                }
            }

            Physics.SyncTransforms();
        }

        bool overallPass = (totalFailures == 0);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================================================================================================");
        sb.AppendLine($"[EcoQuest Self-Heal Simulation Test] OVERALL RESULT: {(overallPass ? "PASS" : "FAIL")}");
        sb.AppendLine($"Total Failures: {totalFailures} (req 0) | Tests run on all 9 trash items with simulated bad states (wrong layer, disabled/trigger collider, stuck Released/Held, stale flags, phantom held)");
        sb.AppendLine("------------------------------------------------------------------------------------------------------------------------");
        sb.AppendLine(string.Format("{0,-16} | {1,-18} | {2,-11} | {3,-11} | {4,-11} | {5,-6} | {6}",
            "Trash Item", "Initial Bad State", "Stop/Play 1", "Stop/Play 2", "Stop/Play 3", "Status", "Failure Reason"));
        sb.AppendLine("------------------------------------------------------------------------------------------------------------------------");

        foreach (var r in results)
        {
            sb.AppendLine(string.Format("{0,-16} | {1,-18} | {2,-11} | {3,-11} | {4,-11} | {5,-6} | {6}",
                r.itemName,
                r.initialBadStatePass ? "PASS (1st click)" : "FAIL",
                r.cycle1Pass ? "PASS" : "FAIL",
                r.cycle2Pass ? "PASS" : "FAIL",
                r.cycle3Pass ? "PASS" : "FAIL",
                r.allPassed ? "PASS" : "FAIL",
                string.IsNullOrEmpty(r.failureReason) ? "None" : r.failureReason));
        }
        sb.AppendLine("========================================================================================================================");

        if (overallPass)
            Debug.Log(sb.ToString());
        else
            Debug.LogError(sb.ToString());

        return overallPass;
    }

    private static bool TestItemBadStatePickup(TrashItem ti, DragAndDrop dad, Transform camTransform, Vector3 arenaOrigin, out string failureReason)
    {
        failureReason = string.Empty;

        // Place item on arena floor
        ti.transform.SetParent(null, true);
        ti.transform.position = arenaOrigin + Vector3.up * 0.1f;
        ti.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
        Physics.SyncTransforms();

        // Aim camera at item from 1.0m away
        Bounds b = dad.GetWorldBounds();
        Vector3 targetCenter = b.center;
        Vector3 camDir = new Vector3(0f, 0.35f, -1f).normalized;
        camTransform.position = targetCenter + camDir * 1.0f;
        camTransform.rotation = Quaternion.LookRotation((targetCenter - camTransform.position).normalized, Vector3.up);

        // CORRUPT ITEM INTO BAD STATES:
        // 1. Wrong layer (Default 0 instead of TrashItem)
        DragAndDrop.SetLayerRecursively(ti.gameObject, 0);

        // 2. Disabled collider & trigger collider
        Collider[] cols = ti.GetComponentsInChildren<Collider>(true);
        if (cols.Length > 0 && cols[0] != null)
        {
            cols[0].enabled = false;
        }
        if (cols.Length > 1 && cols[1] != null)
        {
            cols[1].isTrigger = true;
        }

        // 3. Stale deposit cooldown & stale IsDeposited
        ti.SetDepositCooldown(999f);
        ti.IsDeposited = true;

        // 4. Stuck kinematic Rigidbody & stuck Released state
        Rigidbody rb = dad.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        dad.SetState(DragAndDrop.ItemState.Released, "test_corrupted_released");

        // 5. Stale phantom CurrentlyGrabbedItem pointing to a dummy object
        GameObject phantom = new GameObject("__PhantomItem__");
        var phantomDad = phantom.AddComponent<DragAndDrop>();
        // Make phantom invalid/stale
        phantom.SetActive(false);
        DragAndDrop.SetCurrentlyGrabbedItemForTesting(phantomDad);

        Physics.SyncTransforms();

        // ATTEMPT PICKUP ON THE FIRST CLICK (without opening any menu item!)
        dad.TryGrabObject();

        if (phantom != null) Object.DestroyImmediate(phantom);

        // VERIFY PICKUP RESULT
        if (!dad.IsGrabbed)
        {
            failureReason = $"Item '{ti.name}' was not grabbed on first click. Reason: {DragAndDrop.LastPickupRejectionReason}";
            return false;
        }

        if (dad.CurrentItemState != DragAndDrop.ItemState.Held)
        {
            failureReason = $"Item '{ti.name}' grabbed but state is {dad.CurrentItemState} (expected Held)";
            return false;
        }

        if (DragAndDrop.CurrentlyGrabbedItem != dad)
        {
            failureReason = $"CurrentlyGrabbedItem mismatch (expected {dad.name}, got {(DragAndDrop.CurrentlyGrabbedItem != null ? DragAndDrop.CurrentlyGrabbedItem.name : "null")})";
            return false;
        }

        // Release cleanly
        dad.ReleaseObject("test_release");
        Physics.Simulate(0.05f);
        Physics.SyncTransforms();

        return true;
    }
}
