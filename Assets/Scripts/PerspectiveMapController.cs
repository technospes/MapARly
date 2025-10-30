using UnityEngine;
using Mapbox.Unity.Map;
using System.Collections;
using Mapbox.Utils;

[RequireComponent(typeof(Camera))]
public class PerspectiveMapController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag your Map GameObject here.")]
    public AbstractMap map;

    [Header("Movement Settings")]
    [Tooltip("How fast the map pans. Lower is faster.")]
    public float panSpeed = 100f;
    [Tooltip("How fast the map zooms.")]
    public float zoomSpeed = 10f;

    private Camera _camera;
    private Vector3 _lastPanPosition;
    private Vector2d _initialCoordinates;

    void Start()
    {
        _camera = GetComponent<Camera>();
        if (map == null)
        {
            Debug.LogError("FATAL: Map reference is not set in the PerspectiveMapController!");
            enabled = false;
            return;
        }

        map.InitializeOnStart = false;
        // Store the coordinates BEFORE the map has a chance to reset them to 0,0
        _initialCoordinates = map.CenterLatitudeLongitude;
        StartCoroutine(InitializeMap());
    }

    private IEnumerator InitializeMap()
    {
        // Wait until the end of the first frame to ensure all objects are ready
        yield return new WaitForEndOfFrame();

        Debug.Log("PerspectiveMapController: Initializing map with stored coordinates.");
        // Initialize the map with the coordinates we safely stored
        map.Initialize(_initialCoordinates, (int)map.Zoom);
    }

    // LateUpdate is called after all Update functions. This is the correct place
    // to tell Mapbox to load new tiles based on the camera's new position.
    void LateUpdate()
    {
        if (map.MapVisualizer.State != ModuleState.Finished) return;

        HandlePanning();
        HandleZooming();

        map.UpdateMap(); // This is the crucial line for continuous loading
    }

    private void HandlePanning()
    {
        // Pan with the Right Mouse Button in the Editor
        if (Input.GetMouseButtonDown(1))
        {
            _lastPanPosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(1))
        {
            Vector3 delta = Input.mousePosition - _lastPanPosition;
            float moveSpeed = transform.position.y / panSpeed;
            transform.Translate(-delta.x * moveSpeed, -delta.y * moveSpeed, 0, Space.Self);
            _lastPanPosition = Input.mousePosition;
        }
    }

    private void HandleZooming()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float zoomAmount = scroll * zoomSpeed * (transform.position.y / 10f);
            transform.Translate(0, 0, zoomAmount, Space.Self);
        }
    }
}