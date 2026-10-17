using UnityEngine;

public class SimpleDrive : MonoBehaviour
{
    public float speed = 15f;
    public float maxDistance = 200f;
    private Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
        if (Vector3.Distance(startPos, transform.position) > maxDistance)
        {
            transform.position = startPos;
        }
    }
}
