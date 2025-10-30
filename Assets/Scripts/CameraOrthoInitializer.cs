using UnityEngine;
using Mapbox.Unity.Map;
using System.Collections;

[RequireComponent(typeof(Camera))] // Ensures this script is on a camera
public class CameraOrthoInitializer : MonoBehaviour
{
    [Tooltip("The AbstractMap you want the camera to focus on.")]
    public AbstractMap map;

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        StartCoroutine(SetInitialOrthographicSize());
    }

    private IEnumerator SetInitialOrthographicSize()
    {
        // Wait until the map has finished its initial placement and scaling.
        yield return new WaitUntil(() => map.MapVisualizer.State == ModuleState.Finished);

        // Give it one extra frame for safety.
        yield return new WaitForEndOfFrame();

        // This is the key calculation.
        // We set the camera's orthographic size based on the map's real-world scale in Unity units.
        // Mapbox's WorldRelativeScale gives us the scale factor for the current zoom level.
        // We multiply by 100 as a good starting factor for a standard tile size.
        float initialSize = map.WorldRelativeScale * 100f;

        if (cam.orthographic)
        {
            cam.orthographicSize = initialSize;
            Debug.Log($"CAMERA INITIALIZER: Orthographic Size set to {initialSize} to match map scale.");
        }
        else
        {
            Debug.LogError("CAMERA INITIALIZER: Camera is not set to Orthographic. Please change the Projection in the Inspector.");
        }
    }
}