using UnityEngine;
using System.Collections;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class ServicesManager : MonoBehaviour
{
    public static ServicesManager Instance;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            StartCoroutine(InitializeServices());
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator InitializeServices()
    {
        // First, handle permissions
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(1); // Give a moment for the dialog
        }
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1);
        }
#endif

        // Second, if we have permission, start the location service
        if (Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            yield return StartCoroutine(StartLocationServiceRoutine());
        }
    }

    private IEnumerator StartLocationServiceRoutine()
    {
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("GPS is disabled in device settings.");
            yield break;
        }

        Input.location.Start();

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (Input.location.status == LocationServiceStatus.Running)
        {
            Debug.Log("✅ GPS Service Started Successfully by ServicesManager.");
        }
        else
        {
            Debug.LogWarning("GPS Service failed to start or timed out.");
        }
    }
}