using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class CityWalkGraphVisualizer : MonoBehaviour
{
    public CityWalkGraph graph;
    public bool showOverlay = false;
    public bool showRoadCells = true;
    public bool showGraphEdges = true;

    [System.Serializable]
    public struct SampledCell
    {
        public Vector3 position;
        public int cellType; // 1 = Sidewalk (Green), 2 = Crosswalk (Yellow), 3 = Road (Red)
    }

    [HideInInspector]
    public List<SampledCell> sampledCells = new List<SampledCell>();

    private void OnDrawGizmosSelected()
    {
        if (!showOverlay) return;

        // Draw sampled ground cells
        if (sampledCells != null)
        {
            for (int i = 0; i < sampledCells.Count; i++)
            {
                var cell = sampledCells[i];
                if (cell.cellType == 1) // Sidewalk = green
                {
                    Gizmos.color = new Color(0.1f, 0.9f, 0.2f, 0.6f);
                    Gizmos.DrawCube(cell.position, new Vector3(0.8f, 0.05f, 0.8f));
                }
                else if (cell.cellType == 2) // Crosswalk = yellow
                {
                    Gizmos.color = new Color(1.0f, 0.85f, 0.1f, 0.8f);
                    Gizmos.DrawCube(cell.position, new Vector3(0.8f, 0.08f, 0.8f));
                }
                else if (cell.cellType == 3 && showRoadCells) // Road = red
                {
                    Gizmos.color = new Color(0.9f, 0.15f, 0.15f, 0.35f);
                    Gizmos.DrawCube(cell.position, new Vector3(0.8f, 0.02f, 0.8f));
                }
            }
        }

        // Draw graph nodes and connections
        if (graph != null && graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                var n = graph.nodes[i];
                Gizmos.color = n.isCrosswalk ? Color.yellow : Color.green;
                Gizmos.DrawSphere(n.position + Vector3.up * 0.1f, 0.2f);

                if (showGraphEdges && n.neighbors != null)
                {
                    Gizmos.color = n.isCrosswalk ? new Color(1f, 0.9f, 0.3f, 0.7f) : new Color(0.2f, 1f, 0.4f, 0.5f);
                    for (int j = 0; j < n.neighbors.Count; j++)
                    {
                        int nIdx = n.neighbors[j];
                        if (nIdx > i && nIdx < graph.nodes.Count)
                        {
                            Gizmos.DrawLine(n.position + Vector3.up * 0.1f, graph.nodes[nIdx].position + Vector3.up * 0.1f);
                        }
                    }
                }
            }
        }
    }
}
