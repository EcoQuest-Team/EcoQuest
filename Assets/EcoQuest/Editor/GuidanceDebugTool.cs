using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor diagnostic tool for inspecting and verifying the objective guidance system state.
/// Accessible via menu item 'EcoQuest > Debug Guidance State'.
/// </summary>
public static class GuidanceDebugTool
{
    [MenuItem("EcoQuest/Debug Guidance State")]
    public static void LogGuidanceState()
    {
        Debug.Log("==========================================================================");
        Debug.Log("                      ECOQUEST GUIDANCE STATE DIAGNOSTICS                 ");
        Debug.Log("==========================================================================");

        var orchestrator = UIOrchestrator.Instance;
        string stepStr = orchestrator != null ? orchestrator.CurrentFlowStep.ToString() : "N/A (No Orchestrator)";
        string phaseStr = orchestrator != null ? orchestrator.CurrentPhase.ToString() : "N/A (No Orchestrator)";
        Debug.Log($"[Guidance Debug] Flow Step: {stepStr} | Game Phase: {phaseStr}");

        var service = ObjectiveMarkerService.Instance;
        if (service != null)
        {
            string targetName = service.CurrentTarget != null ? service.CurrentTarget.name : "None (Null)";
            Debug.Log($"[Guidance Debug] Active Target: '{targetName}' | Label: '{service.CurrentLabel}'");
            Debug.Log($"[Guidance Debug] Gating Reason: {service.CurrentReason}");
        }
        else
        {
            Debug.LogWarning("[Guidance Debug] ObjectiveMarkerService is NOT instantiated in the current scene!");
        }

        Debug.Log("--------------------------------------------------------------------------");
        Debug.Log("                       ALL MARKER OBJECTS IN SCENE                        ");
        Debug.Log("--------------------------------------------------------------------------");

        var allGameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int markerCount = 0;
        int unmanagedActiveCount = 0;

        foreach (var go in allGameObjects)
        {
            if (go == null) continue;

            string name = go.name;
            bool isMarkerRelated = name.Contains("Arrow") || name.Contains("Marker") || name.Contains("Pointer") || name.Contains("EdgeIndicator");

            if (isMarkerRelated)
            {
                markerCount++;
                string ownerScript = DetermineOwnerScript(go);
                bool isOwnedByService = ownerScript.Contains("ObjectiveMarkerService");
                bool isLegitimateMinimap = name.StartsWith("MinimapIcon");

                string parentName = go.transform.parent != null ? go.transform.parent.name : "None (Root)";
                Debug.Log($" - Marker #{markerCount}: '{name}' | ActiveInHierarchy: {go.activeInHierarchy} | Parent: '{parentName}' | Owner: {ownerScript}");

                // If marker is active but NOT owned by ObjectiveMarkerService and not legitimate UI
                if (go.activeInHierarchy && !isOwnedByService && !isLegitimateMinimap && (name.Contains("Arrow") || name.Contains("Pointer")))
                {
                    unmanagedActiveCount++;
                    Debug.LogWarning($"[Guidance Warning] UNMANAGED ACTIVE MARKER DETECTED! GameObject '{name}' is active, parent '{parentName}', owner '{ownerScript}'. All guidance arrows must be managed by ObjectiveMarkerService!");
                }
            }
        }

        Debug.Log("--------------------------------------------------------------------------");
        Debug.Log($"Summary: {markerCount} marker objects scanned. {unmanagedActiveCount} unmanaged active markers.");
        Debug.Log("==========================================================================");
    }

    private static string DetermineOwnerScript(GameObject go)
    {
        if (go.GetComponent<ObjectiveMarkerService>() != null || (go.transform.parent != null && go.transform.parent.GetComponent<ObjectiveMarkerService>() != null))
            return "ObjectiveMarkerService";

        if (go.GetComponent<NPCWaypointPointer>() != null || (go.transform.parent != null && go.transform.parent.GetComponent<NPCWaypointPointer>() != null))
            return "NPCWaypointPointer";

        if (go.GetComponent<NPCInteraction>() != null || (go.transform.parent != null && go.transform.parent.GetComponent<NPCInteraction>() != null))
            return "NPCInteraction";

        if (go.GetComponent<MinimapIcon>() != null)
            return "MinimapIcon";

        return "Unmanaged / Scene Static Object";
    }
}
