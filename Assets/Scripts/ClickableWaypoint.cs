using UnityEngine;
using UnityEngine.EventSystems;
using Mapbox.Unity.MeshGeneration.Components;

public class ClickableWaypoint : MonoBehaviour, IPointerClickHandler
{
    private SearchManager searchManager;

    void Start()
    {
        // Find the SearchManager in the scene
        searchManager = FindObjectOfType<SearchManager>();

        if (searchManager == null)
        {
            Debug.LogError("SearchManager not found in scene!");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"✓ WAYPOINT CLICKED: {gameObject.name}");

        // Get the FeatureBehaviour from this object or parent
        var featureBehaviour = GetComponentInParent<FeatureBehaviour>();

        if (featureBehaviour != null && !string.IsNullOrEmpty(featureBehaviour.DataString))
        {
            Debug.Log($"✓ Found data: {featureBehaviour.DataString}");

            // Tell the SearchManager to handle this click
            if (searchManager != null)
            {
                searchManager.OnWaypointClicked(featureBehaviour, transform.position);
            }
        }
        else
        {
            Debug.LogWarning("No FeatureBehaviour data found on clicked waypoint!");
        }
    }
}