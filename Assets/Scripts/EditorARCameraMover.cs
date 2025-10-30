using UnityEngine;

// This script allows you to simulate walking in the AR scene using WASD keys,
// but ONLY when running inside the Unity Editor.
public class EditorARCameraMover : MonoBehaviour
{
#if UNITY_EDITOR // This ensures the script does nothing in a real build

    [SerializeField]
    private float speed = 3.0f; // Walking speed in meters per second

    void Update()
    {
        // Get input from WASD or arrow keys
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        if (x != 0 || z != 0)
        {
            // Move the camera's parent (the AR Session Origin)
            // so the camera's local position remains correct.
            transform.parent.position += new Vector3(x, 0, z) * speed * Time.deltaTime;
        }
    }

#endif
}