using UnityEngine;
using Mapbox.Unity.Map;

public class MapDebugLogger : MonoBehaviour
{
    // Drag your map object here in the Inspector
    public AbstractMap map;

    private float timer;

    void Update()
    {
        // Only log once every 2 seconds to avoid spam
        timer += Time.deltaTime;
        if (timer > 2f)
        {
            if (map == null)
            {
                Debug.LogError("MAP DEBUG: Map reference is NOT set in the Inspector!");
                return;
            }

            // This is the most important value to check.
            int activeTiles = map.MapVisualizer.ActiveTiles.Count;
            Debug.Log($"MAP DEBUG: Active Tiles = {activeTiles}");

            if (activeTiles == 0)
            {
                if (Camera.main == null)
                {
                    Debug.LogError("MAP DEBUG CRITICAL: No camera in the scene is tagged 'MainCamera'. Mapbox cannot find a camera to follow!");
                }
                else
                {
                    Debug.LogWarning("MAP DEBUG WARNING: 0 active tiles. This usually means the map initialized before the camera was ready.");
                }
            }

            timer = 0f; // Reset timer
        }
    }
}