using UnityEngine;
using Mapbox.Unity.Map;
using System.Collections;
using Mapbox.Utils;

public class FinalMapController : MonoBehaviour
{
    [Header("References")]
    public AbstractMap map;

    [Header("Movement")]
    public float panSpeed = 1.0f;
    public float zoomSpeed = 20f;

    private Camera _camera;
    private Vector3 _lastPanPosition;

    void Start()
    {
        _camera = GetComponent<Camera>();
        if (map == null) { Debug.LogError("Map reference is not set!"); return; }
        map.InitializeOnStart = false;
        StartCoroutine(InitializeMap());
    }

    private IEnumerator InitializeMap()
    {
        yield return new WaitForEndOfFrame();
        map.Initialize(map.CenterLatitudeLongitude, (int)map.Zoom);
    }

    void LateUpdate()
    {
        if (map.MapVisualizer.State != ModuleState.Finished) return;
        HandlePanning();
        HandleZooming();
        map.UpdateMap();
    }

    private void HandlePanning()
    {
        // Use Right Mouse Button for panning
        if (Input.GetMouseButtonDown(1)) { _lastPanPosition = Input.mousePosition; }
        if (Input.GetMouseButton(1))
        {
            Vector3 delta = Input.mousePosition - _lastPanPosition;
            float moveSpeed = _camera.orthographic ? (_camera.orthographicSize / Screen.height) * panSpeed * 2f : transform.position.y / (panSpeed * 10f);
            transform.Translate(-delta.x * moveSpeed, -delta.y * moveSpeed, 0);
            _lastPanPosition = Input.mousePosition;
        }
    }

    private void HandleZooming()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            if (_camera.orthographic)
            {
                _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize - scroll * zoomSpeed, 1f, 10000f);
            }
            else
            {
                transform.Translate(0, 0, scroll * zoomSpeed * (transform.position.y / 10f), Space.Self);
            }
        }
    }
}