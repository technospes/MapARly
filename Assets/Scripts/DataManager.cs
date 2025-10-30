using UnityEngine;
using Mapbox.Utils;

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    // This is the "package" our courier will carry
    public Vector2d? startCoordinates;
    public Vector2d? destinationCoordinates;

    void Awake()
    {
        // This is the Singleton pattern. It ensures there is only ever one DataManager.
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // This is the VIP pass!
        }
        else
        {
            Destroy(gameObject);
        }
    }
}