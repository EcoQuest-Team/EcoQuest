using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityQuest
{
    [DisallowMultipleComponent]
    public class CityTrashSpawnPoint : MonoBehaviour
    {
        public CityDistrict district = CityDistrict.Park;
        public Vector3 groundNormal = Vector3.up;
        public float navMeshDistance = 0f;
        public bool isVerifiedValid = true;

        private void Awake()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene())
            {
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene())
            {
                enabled = false;
                return;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = isVerifiedValid ? new Color(0.2f, 0.95f, 0.4f, 0.85f) : Color.red;
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.15f, 0.22f);
            Gizmos.DrawLine(transform.position, transform.position + groundNormal * 0.65f);
        }
    }
}
