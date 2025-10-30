using UnityEngine;
using Mapbox.Unity.MeshGeneration.Modifiers;
using Mapbox.Unity.MeshGeneration.Components;
using Mapbox.Unity.MeshGeneration.Data;

[CreateAssetMenu(menuName = "Mapbox/Modifiers/PoiDataFetcher")]
public class PoiDataFetcher : GameObjectModifier
{
    public override void Run(VectorEntity ve, UnityTile tile)
    {
        // Add FeatureBehaviour to the parent
        var featureBehaviour = ve.GameObject.GetComponent<FeatureBehaviour>();
        if (featureBehaviour == null)
        {
            featureBehaviour = ve.GameObject.AddComponent<FeatureBehaviour>();
        }

        // Store the POI data
        if (ve.Feature.Properties.ContainsKey("name"))
        {
            string poiName = ve.Feature.Properties["name"].ToString();
            featureBehaviour.DataString = "{\"name\":\"" + poiName + "\"}";
            Debug.Log($"✓ POI Data Attached: {poiName} to {ve.GameObject.name}");
        }
        else
        {
            Debug.LogWarning($"⚠ POI {ve.GameObject.name} has no 'name' property");
        }

        // === ADD CLICKABLE COMPONENT ===
        var clickable = ve.GameObject.GetComponent<ClickableWaypoint>();
        if (clickable == null)
        {
            clickable = ve.GameObject.AddComponent<ClickableWaypoint>();
            Debug.Log($"✓ Added ClickableWaypoint to {ve.GameObject.name}");
        }

        // === FIX COLLIDERS FOR CLICKING ===
        var allColliders = ve.GameObject.GetComponentsInChildren<Collider>();

        foreach (var collider in allColliders)
        {
            // Remove capsule colliders (hard to click from top-down view)
            if (collider is CapsuleCollider)
            {
                GameObject.DestroyImmediate(collider);

                // Add a larger sphere collider
                SphereCollider sphereCol = collider.gameObject.AddComponent<SphereCollider>();
                sphereCol.radius = 15f;
                sphereCol.isTrigger = false;

                Debug.Log($"✓ Replaced CapsuleCollider with SphereCollider on {collider.gameObject.name}");
            }
            else if (collider is SphereCollider)
            {
                SphereCollider sphere = collider as SphereCollider;
                if (sphere.radius < 10f)
                {
                    sphere.radius = 15f;
                }
            }

            // Set layer to POI
            int poiLayer = LayerMask.NameToLayer("POI");
            if (poiLayer != -1)
            {
                collider.gameObject.layer = poiLayer;
            }
        }
    }
}