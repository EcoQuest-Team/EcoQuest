#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class HeldItemTestHarness
{
    public struct TestResult
    {
        public string dummyName;
        public bool passed;
        public string details;
    }

    [MenuItem("Tools/EcoQuest/Run Held Item Test Harness")]
    public static void RunTestsMenu()
    {
        RunTests(true);
    }

    public static void RunTests(bool showDialog = false)
    {
        List<TestResult> results = new List<TestResult>();
        Debug.Log("<b><color=#43A84A>[HeldItemTestHarness] Starting test suite across 13 dummy types...</color></b>");

        // 1. Tiny dummy
        RunSingleDummyTest("Tiny", dummy =>
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            var rb = cube.GetComponent<Rigidbody>();
            if (rb == null) rb = cube.AddComponent<Rigidbody>();
            rb.mass = 0.1f;
            return cube;
        }, HoldSizeCategory.Small, false, results);

        // 2. Small dummy
        RunSingleDummyTest("Small", dummy =>
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.localScale = new Vector3(0.18f, 0.18f, 0.18f);
            var rb = sphere.GetComponent<Rigidbody>();
            if (rb == null) rb = sphere.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            return sphere;
        }, HoldSizeCategory.Small, false, results);

        // 3. Medium dummy
        RunSingleDummyTest("Medium", dummy =>
        {
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.transform.localScale = new Vector3(0.35f, 0.175f, 0.35f);
            var rb = cyl.GetComponent<Rigidbody>();
            if (rb == null) rb = cyl.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            return cyl;
        }, HoldSizeCategory.Medium, false, results);

        // 4. Large dummy
        RunSingleDummyTest("Large", dummy =>
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = new Vector3(0.70f, 0.70f, 0.70f);
            var rb = box.GetComponent<Rigidbody>();
            if (rb == null) rb = box.AddComponent<Rigidbody>();
            rb.mass = 1.5f;
            return box;
        }, HoldSizeCategory.Large, false, results);

        // 5. Huge dummy (> 0.9m limit, triggers downscaling)
        RunSingleDummyTest("Huge", dummy =>
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = new Vector3(1.80f, 1.80f, 1.80f);
            var rb = box.GetComponent<Rigidbody>();
            if (rb == null) rb = box.AddComponent<Rigidbody>();
            rb.mass = 3.0f;
            return box;
        }, HoldSizeCategory.Large, true, results);

        // 6. Flat dummy (paper-like thinness)
        RunSingleDummyTest("Flat", dummy =>
        {
            var flat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flat.transform.localScale = new Vector3(0.30f, 0.005f, 0.20f);
            var rb = flat.GetComponent<Rigidbody>();
            if (rb == null) rb = flat.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            return flat;
        }, HoldSizeCategory.Medium, false, results);

        // 7. Tall dummy (pole)
        RunSingleDummyTest("Tall", dummy =>
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.transform.localScale = new Vector3(0.10f, 1.20f, 0.10f);
            var rb = pole.GetComponent<Rigidbody>();
            if (rb == null) rb = pole.AddComponent<Rigidbody>();
            rb.mass = 1.0f;
            return pole;
        }, HoldSizeCategory.Large, true, results);

        // 8. Wide dummy (plank)
        RunSingleDummyTest("Wide", dummy =>
        {
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.transform.localScale = new Vector3(1.10f, 0.08f, 0.25f);
            var rb = plank.GetComponent<Rigidbody>();
            if (rb == null) rb = plank.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            return plank;
        }, HoldSizeCategory.Large, true, results);

        // 9. Off-center pivot dummy
        RunSingleDummyTest("OffCenterPivot", dummy =>
        {
            var root = new GameObject("Dummy_OffCenter");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0.40f, 0.20f, -0.30f);
            visual.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 0.8f;
            return root;
        }, HoldSizeCategory.Medium, false, results);

        // 10. Heavy dummy (mass 5kg)
        RunSingleDummyTest("Heavy", dummy =>
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
            var rb = box.GetComponent<Rigidbody>();
            if (rb == null) rb = box.AddComponent<Rigidbody>();
            rb.mass = 5.0f;
            return box;
        }, HoldSizeCategory.Medium, false, results);

        // 11. Light dummy (mass 0.05kg)
        RunSingleDummyTest("Light", dummy =>
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
            var rb = box.GetComponent<Rigidbody>();
            if (rb == null) rb = box.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            return box;
        }, HoldSizeCategory.Medium, false, results);

        // 12. No Rigidbody dummy
        RunSingleDummyTest("NoRigidbody", dummy =>
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = new Vector3(0.20f, 0.20f, 0.20f);
            var rb = box.GetComponent<Rigidbody>();
            if (rb != null) Object.DestroyImmediate(rb);
            return box;
        }, HoldSizeCategory.Small, false, results);

        // 13. Multi-collider dummy
        RunSingleDummyTest("MultiCollider", dummy =>
        {
            var root = new GameObject("Dummy_MultiCol");
            var part1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part1.transform.SetParent(root.transform, false);
            part1.transform.localPosition = new Vector3(-0.08f, 0f, 0f);
            part1.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

            var part2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part2.transform.SetParent(root.transform, false);
            part2.transform.localPosition = new Vector3(0.08f, 0f, 0f);
            part2.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            return root;
        }, HoldSizeCategory.Medium, false, results);

        // Summary
        int passCount = 0;
        foreach (var r in results)
        {
            if (r.passed) passCount++;
            Debug.Log($"[TestHarness] {r.dummyName}: {(r.passed ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")} ({r.details})");
        }

        bool allPassed = passCount == results.Count;
        string summaryMsg = $"Test Harness Finished: {passCount}/{results.Count} PASSED";
        if (allPassed)
        {
            Debug.Log($"<b><color=green>[HeldItemTestHarness] {summaryMsg} - All generic dummies verified!</color></b>");
        }
        else
        {
            Debug.LogError($"[HeldItemTestHarness] {summaryMsg}");
        }

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Held Item Test Harness", summaryMsg, "OK");
        }
    }

    private static void RunSingleDummyTest(
        string testName,
        System.Func<GameObject, GameObject> spawner,
        HoldSizeCategory expectedCategory,
        bool expectScaleDown,
        List<TestResult> results)
    {
        GameObject dummy = null;
        try
        {
            dummy = spawner(null);
            dummy.name = "TestDummy_" + testName;

            Vector3 initialScale = dummy.transform.localScale;
            Transform initialParent = dummy.transform.parent;
            var rb = dummy.GetComponent<Rigidbody>();
            bool hadRb = rb != null;
            bool initialKinematic = hadRb ? rb.isKinematic : false;
            bool initialGravity = hadRb ? rb.useGravity : false;

            var dnd = dummy.AddComponent<DragAndDrop>();

            // Test 1: Grab
            dnd.GrabObject();
            if (!dnd.IsGrabbed)
            {
                results.Add(new TestResult { dummyName = testName, passed = false, details = "Failed to grab object" });
                return;
            }

            // Test 2: Category
            if (dnd.CurrentSizeCategory != expectedCategory)
            {
                Bounds b = dnd.CurrentBounds;
                results.Add(new TestResult
                {
                    dummyName = testName,
                    passed = false,
                    details = $"Expected size {expectedCategory}, got {dnd.CurrentSizeCategory} (bounds={b.size}, scale={dummy.transform.localScale})"
                });
                return;
            }

            // Test 3: Scale down if expected
            if (expectScaleDown)
            {
                if (dummy.transform.localScale.magnitude >= initialScale.magnitude)
                {
                    results.Add(new TestResult
                    {
                        dummyName = testName,
                        passed = false,
                        details = $"Expected downscale from {initialScale}, got {dummy.transform.localScale}"
                    });
                    return;
                }
            }

            // Test 4: Grip point validity
            Bounds bounds = dnd.CurrentBounds;
            Vector3 grip = dnd.GripPointWorld;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f || bounds.size.z <= 0f)
            {
                results.Add(new TestResult { dummyName = testName, passed = false, details = "Invalid combined bounds size" });
                return;
            }

            // Test 5: Release and state restoration
            dnd.ReleaseObject();
            if (dnd.IsGrabbed)
            {
                results.Add(new TestResult { dummyName = testName, passed = false, details = "Failed to release object" });
                return;
            }

            // Verify scale restored exactly
            if (Vector3.Distance(dummy.transform.localScale, initialScale) > 0.0001f)
            {
                results.Add(new TestResult
                {
                    dummyName = testName,
                    passed = false,
                    details = $"Scale not restored: was {initialScale}, now {dummy.transform.localScale}"
                });
                return;
            }

            // Verify Rigidbody restored
            if (hadRb && rb != null)
            {
                if (rb.isKinematic != initialKinematic || rb.useGravity != initialGravity)
                {
                    results.Add(new TestResult
                    {
                        dummyName = testName,
                        passed = false,
                        details = $"Rigidbody state not restored: kinematic={rb.isKinematic}, gravity={rb.useGravity}"
                    });
                    return;
                }
            }

            results.Add(new TestResult
            {
                dummyName = testName,
                passed = true,
                details = $"Category={dnd.CurrentSizeCategory}, Grip={grip}, ScaleRestored={dummy.transform.localScale}"
            });
        }
        catch (System.Exception ex)
        {
            results.Add(new TestResult { dummyName = testName, passed = false, details = "Exception: " + ex.Message });
        }
        finally
        {
            if (dummy != null) Object.DestroyImmediate(dummy);
        }
    }
}
#endif
