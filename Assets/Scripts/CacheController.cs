using Mapbox.Unity; // Required for MapboxAccess
using UnityEngine;
using UnityEngine.UI;

public class CacheController : MonoBehaviour
{
    [Header("UI Elements (Optional)")]
    public Button clearCacheButton;
    public Text statusText;

    void Start()
    {
        if (clearCacheButton != null)
        {
            clearCacheButton.onClick.AddListener(ClearCache);
        }

        // We can assume caching is always active in this SDK version.
        if (statusText != null)
        {
            statusText.text = "Status: Caching is Active";
        }
    }

    /// <summary>
    /// Calls the built-in MapboxAccess method to clear all cache files.
    /// </summary>
    public void ClearCache()
    {
        Debug.Log("Calling built-in MapboxAccess.ClearAllCacheFiles()...");

        // This is the correct, built-in method from your own MapboxAccess script.
        MapboxAccess.Instance.ClearAllCacheFiles();

        // The original method already logs "done clearing caches" to the console.
        if (statusText != null)
        {
            statusText.text = "Status: Cache Cleared";
        }
    }
}