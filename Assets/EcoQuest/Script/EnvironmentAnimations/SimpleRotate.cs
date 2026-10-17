using UnityEngine;

public class SimpleRotate : MonoBehaviour
{
    public Vector3 rotationSpeed = new Vector3(0, 5f, 0);

    void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}
