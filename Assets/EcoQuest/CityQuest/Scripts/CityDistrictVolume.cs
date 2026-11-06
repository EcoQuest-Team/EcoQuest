using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityQuest
{
    [DisallowMultipleComponent]
    public class CityDistrictVolume : MonoBehaviour
    {
        public const string AllowedScenePath = "Assets/EcoQuest/City Scene.unity";

        public CityDistrict district = CityDistrict.Park;
        public Vector3 boundsSize = new Vector3(40f, 15f, 40f);
        public Color gizmoColor = new Color(0.2f, 0.85f, 0.35f, 0.22f);
        [TextArea(1, 3)]
        public string landmarksDescription;

        private void Awake()
        {
            if (!IsInAllowedCityScene())
            {
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            if (!IsInAllowedCityScene())
            {
                enabled = false;
                return;
            }
        }

        public static bool IsInAllowedCityScene()
        {
            Scene active = SceneManager.GetActiveScene();
            return active.path == AllowedScenePath || active.name == "City Scene";
        }

        public Bounds WorldBounds => new Bounds(transform.position, boundsSize);

        public bool ContainsPoint(Vector3 worldPos)
        {
            return WorldBounds.Contains(new Vector3(worldPos.x, transform.position.y, worldPos.z));
        }

        private void OnDrawGizmos()
        {
            Color fill = gizmoColor;
            fill.a = 0.16f;
            Gizmos.color = fill;
            Gizmos.DrawCube(transform.position, boundsSize);

            Color wire = gizmoColor;
            wire.a = 0.85f;
            Gizmos.color = wire;
            Gizmos.DrawWireCube(transform.position, boundsSize);

#if UNITY_EDITOR
            GUIStyle style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            style.normal.textColor = wire;
            style.alignment = TextAnchor.MiddleCenter;
            UnityEditor.Handles.Label(transform.position + Vector3.up * (boundsSize.y * 0.5f + 1.5f),
                $"[{CityTrashDatabase.GetDistrictDisplayName(district)}]", style);
#endif
        }
    }
}
