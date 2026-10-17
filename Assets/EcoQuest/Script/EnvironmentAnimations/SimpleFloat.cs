using UnityEngine;

public class SimpleFloat : MonoBehaviour
{
    public float floatSpeed = 0.5f;
    public float floatHeight = 2.0f;
    private Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        transform.position = startPos + new Vector3(0, Mathf.Sin(Time.time * floatSpeed) * floatHeight, 0);
    }
}
