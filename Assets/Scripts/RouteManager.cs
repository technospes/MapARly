using Mapbox.Directions;
using Mapbox.Unity;
using Mapbox.Unity.Utilities;
using Mapbox.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
//using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Helper to make Vector2d visible in inspector
[System.Serializable]
public struct Vector2dSerializable
{
    public double x;
    public double y;
    public Vector2dSerializable(double x, double y) { this.x = x; this.y = y; }
    public Vector2d ToVector2d() { return new Vector2d(x, y); }
}
public class ArrowMetadata : MonoBehaviour
{
    public int RouteIndex;
    public ARAnchor Anchor;
}

public class RouteManager : MonoBehaviour
{
    [Header("Dynamic Spawning")]
    public float spawnAheadDistance = 50f;
    public float despawnBehindDistance = 10f;
    public float updateDistanceThreshold = 2f;
    [Header("Navigation Settings")]
    public float baseArrowSpacing = 10f; // This is now our default
    private float _currentArrowSpacing;   // This will be the calculated spacing for the route
    public bool useTestRoute = false;
    public float startProximityThreshold = 25f;
    [Header("On-Screen Debug UI")] // This header might already exist
    public GameObject apiDebugPanel;
    public TextMeshProUGUI apiLogText;
    [Header("Arrow Pool Settings")]
    public int poolSize = 20;
    public GameObject arrowPrefab;
    public float arrowScale = 1.0f;
    public float arrowHeightOffset = 0.1f;
    public Material StartArrow_Mat;
    public Material MidArrow_Mat;
    public Material EndArrow_Mat;
    [Header("Dynamic Scaling Settings")]
    [Tooltip("The distance at which arrows are at their normal (1x) scale.")]
    public float minScaleDistance = 5f; // e.g., 5 meters
    [Tooltip("The distance at which arrows reach their maximum scale.")]
    public float maxScaleDistance = 50f; // e.g., 50 meters
    [Tooltip("How much larger distant arrows should be (e.g., 2.0 means 2x larger).")]
    public float farScaleMultiplier = 2.0f;
    [Header("AR Components")]
    public Camera arCamera;
    public ARPlaneManager planeManager;
    public ARRaycastManager raycastManager;
    public ARAnchorManager anchorManager;
    [Header("Text-to-Speech Settings")]
    [SerializeField] private bool _isTtsEnabled = false; // Is the feature on?
    [SerializeField] private float _ttsProximityThreshold = 20f; // Announce instruction when user is within 20 meters of the turn.
    // Internal TTS State
    private List<Step> _routeSteps = new List<Step>();
    private List<Vector3> _maneuverARPositions = new List<Vector3>();
    private int _currentStepIndex = 0;
    [Header("Navigation Settings")]
    public RoutingProfile routeProfile = RoutingProfile.Walking;
    public bool useDeviceGPS = true;

    [Header("Visual Feedback")]
    public GameObject guidingBeacon;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI debugText;
    public GameObject scanningIndicator;
    public Toggle TtsToggle;
    [Header("Dependencies")]
    public Mapbox.Unity.Map.AbstractMap map;
    // Core Navigation
    private Directions _directions;
    private List<Vector3> routeARPositions = new List<Vector3>();
    private List<Vector2d> rawRoutePoints = new List<Vector2d>();
    private Vector2d routeOrigin;
    private Quaternion northAlignment = Quaternion.identity;
    [Header("Route Coordinates (for testing)")]
    [SerializeField] private Vector2dSerializable startCoordinate = new Vector2dSerializable(28.4575, 77.4969);
    [SerializeField] private Vector2dSerializable destinationCoordinate = new Vector2dSerializable(28.4572, 77.4984);
    // Arrow Pooling
    private Queue<GameObject> arrowPool = new Queue<GameObject>();
    private List<GameObject> activeArrows = new List<GameObject>();
    private Dictionary<int, GameObject> routeIndexToArrow = new Dictionary<int, GameObject>();

    // State Tracking
    private bool locationInitialized = false;
    private bool routeReceived = false;
    private bool navigationReady = false;
    private bool _isWaitingForUserAtStart = false;
    private ARPlane primaryPlane = null;
    private bool planeLocked = false;
    private Vector3 userARPosition = Vector3.zero;
    private Vector3 lastUserPositionForUpdate;
    private int lastClosestRouteIndex = 0;
    private string currentStatus = "Initializing...";

    private void LogAR(string message) => Debug.Log($"[AR_NAV] {message}");
    private void LogErrorAR(string message) => Debug.LogError($"[AR_NAV_ERROR] {message}");

    #region Unity Lifecycle

    void Start()
    {
        InitializeComponents();
        lastUserPositionForUpdate = Vector3.positiveInfinity;

        // --- Force TTS to be OFF at the start ---
        _isTtsEnabled = false;
        if (TtsToggle != null)
        {
            TtsToggle.isOn = false;
        }

        if (DataManager.Instance != null && DataManager.Instance.startCoordinates.HasValue && DataManager.Instance.destinationCoordinates.HasValue)
        {
            // --- REAL ROUTE from IntroScene ---
            LogAR("✅ Route data found in DataManager!");
            StartCoroutine(BeginNavigationSequence(
                DataManager.Instance.startCoordinates.Value,
                DataManager.Instance.destinationCoordinates.Value
            ));
        }
        else if (useTestRoute)
        {
            // --- TEST ROUTE using coordinates from the Inspector ---
            LogAR("🧪 Starting navigation in TEST MODE.");
            StartCoroutine(BeginNavigationSequence(
                startCoordinate.ToVector2d(),
                destinationCoordinate.ToVector2d()
            ));
        }
        else
        {
            UpdateStatus("No Route Data Found.");
            LogErrorAR("Could not start navigation. No route data in DataManager and not in Test Mode.");
        }
    }

    void Update()
    {
        UpdateUserPosition();
        UpdateDebugDisplay();

        // Update the beacon if navigation is ready OR if we are waiting for the user to get to the start.
        if (navigationReady || _isWaitingForUserAtStart)
        {
            UpdateGuidingBeacon();
        }
        if (navigationReady)
        {
            float distanceMoved = Vector3.Distance(userARPosition, lastUserPositionForUpdate);
            if (distanceMoved > updateDistanceThreshold)
            {
                UpdateDynamicArrowSpawning();
                lastUserPositionForUpdate = userARPosition;
            }
            UpdateArrowScales();
            CheckForTtsAnnouncement();
            //UpdateGuidingBeacon();
        }
    }

    void OnEnable()
    {
        if (planeManager != null) planeManager.planesChanged += OnPlanesChanged;
    }

    void OnDisable()
    {
        if (planeManager != null) planeManager.planesChanged -= OnPlanesChanged;
    }

    #endregion

    #region Initialization & Arrow Pooling

    private void InitializeComponents()
    {
        if (arrowPrefab == null || planeManager == null || raycastManager == null)
        {
            LogErrorAR("❌ Missing required components!");
            enabled = false;
            return;
        }

        if (arCamera == null) arCamera = Camera.main;
        if (anchorManager == null) anchorManager = FindObjectOfType<ARAnchorManager>();

        _directions = MapboxAccess.Instance.Directions;
        LogAR("✅ Components initialized");
    }

    private void InitializeArrowPool()
    {
        LogAR($"🎯 Creating arrow pool with {poolSize} arrows...");

        for (int i = 0; i < poolSize; i++)
        {
            GameObject arrow = Instantiate(arrowPrefab);
            arrow.SetActive(false);
            arrow.name = $"PooledArrow_{i}";
            arrowPool.Enqueue(arrow);
        }

        LogAR($"✅ Arrow pool created: {arrowPool.Count} arrows ready");
    }
    private void CheckForTtsAnnouncement()
    {
        if (!_isTtsEnabled || !navigationReady || _currentStepIndex >= _routeSteps.Count)
        {
            return;
        }

        // In a real build, this uses GPS. In the editor, it uses our keyboard movement.
        Vector3 targetPosition = _maneuverARPositions[_currentStepIndex];
        float distanceToManeuver = Vector3.Distance(userARPosition, targetPosition);

        if (distanceToManeuver < _ttsProximityThreshold)
        {
            Step nextStep = _routeSteps[_currentStepIndex];

            // --- THE FIX: Build the full sentence ---
            string instructionText = nextStep.Maneuver.Instruction;
            double distanceInMeters = nextStep.Distance;

            // Combine the text and the distance into one sentence.
            string fullInstruction = $"{instructionText}, in {distanceInMeters:F0} meters.";

            LogAR($"TTS TRIGGERED! Speaking: '{fullInstruction}'");

            if (GoogleTTSManager.Instance != null)
            {
                // Send the complete sentence to the TTS manager.
                GoogleTTSManager.Instance.Speak(fullInstruction);
            }

            _currentStepIndex++;
        }
    }
    private GameObject GetArrowFromPool()
    {
        if (arrowPool.Count > 0)
        {
            GameObject arrow = arrowPool.Dequeue();
            arrow.SetActive(true);
            activeArrows.Add(arrow);
            return arrow;
        }
        else
        {
            // Pool exhausted, create new arrow
            LogAR("⚠ Pool exhausted, creating new arrow");
            GameObject arrow = Instantiate(arrowPrefab);
            arrow.name = $"ExtraArrow_{activeArrows.Count}";
            activeArrows.Add(arrow);
            return arrow;
        }
    }
    public void ToggleTts(bool isOn)
    {
        _isTtsEnabled = isOn;
        LogAR($"TTS has been {(_isTtsEnabled ? "ENABLED" : "DISABLED")}");
    }
    private void ReturnArrowToPool(GameObject arrow)
    {
        if (arrow == null) return;

        // Destroy anchor if exists
        ArrowMetadata meta = arrow.GetComponent<ArrowMetadata>();
        if (meta != null && meta.Anchor != null)
        {
            Destroy(meta.Anchor.gameObject);
            meta.Anchor = null;
        }

        arrow.SetActive(false);
        arrow.transform.SetParent(null);
        activeArrows.Remove(arrow);

        if (!arrowPool.Contains(arrow))
        {
            arrowPool.Enqueue(arrow);
        }
    }

    #endregion
    private void UpdateArrowScales()
    {
        if (activeArrows.Count == 0) return;

        foreach (var arrow in activeArrows)
        {
            if (arrow == null) continue;

            // 1. Calculate the distance from the AR camera to the arrow.
            float distance = Vector3.Distance(arCamera.transform.position, arrow.transform.position);

            // 2. Determine the scaling factor based on distance.
            // This calculates a percentage (0 to 1) of how far the arrow is between our min and max distances.
            float t = Mathf.Clamp01((distance - minScaleDistance) / (maxScaleDistance - minScaleDistance));

            // 3. Linearly interpolate between our normal scale and our maximum "far" scale.
            float currentMultiplier = Mathf.Lerp(1.0f, farScaleMultiplier, t);

            // 4. Apply the final calculated scale.
            arrow.transform.localScale = Vector3.one * arrowScale * currentMultiplier;
        }
    }
    private void UpdateGuidingBeacon()
    {
        if (guidingBeacon == null) return;

        RectTransform beaconRect = guidingBeacon.GetComponent<RectTransform>();
        Vector3 targetWorldPosition;

        // --- NEW LOGIC: Decide WHAT to point at ---
        if (_isWaitingForUserAtStart && routeARPositions.Count > 0)
        {
            // If we are waiting for the user, ALWAYS point to the first point of the route.
            targetWorldPosition = primaryPlane.transform.TransformPoint(routeARPositions[0]);
        }
        else if (navigationReady && activeArrows.Count > 0)
        {
            // If navigation is running, find the next active arrow to point towards.
            targetWorldPosition = activeArrows[0].transform.position; // The first one in the active list is the next target
        }
        else
        {
            // If neither, hide the beacon and stop.
            guidingBeacon.SetActive(false);
            return;
        }

        // --- The rest of the logic remains the same ---
        Vector3 screenPos = arCamera.WorldToScreenPoint(targetWorldPosition);

        if (screenPos.z > 0 && screenPos.x > 0 && screenPos.x < Screen.width && screenPos.y > 0 && screenPos.y < Screen.height)
        {
            // Target is on screen, hide the beacon.
            guidingBeacon.SetActive(false);
        }
        else
        {
            // Target is off-screen, show and position the beacon.
            guidingBeacon.SetActive(true);
            if (screenPos.z < 0) { screenPos *= -1; }

            Vector2 screenCenter = new Vector2(Screen.width, Screen.height) / 2;
            screenPos -= new Vector3(screenCenter.x, screenCenter.y, 0);

            float angle = Mathf.Atan2(screenPos.y, screenPos.x);
            angle -= 90 * Mathf.Deg2Rad;

            beaconRect.position = screenCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Screen.width * 0.4f);
            beaconRect.rotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
        }
    }

    #region Navigation Sequence

    private IEnumerator BeginNavigationSequence(Vector2d start, Vector2d dest)
    {
        // This coroutine now takes control of the GPS for the AR Scene
        yield return StartCoroutine(InitializeLocationServices());

        // --- THE FIX: Only perform AR steps if we are NOT in test mode ---
        if (!useTestRoute)
        {
            UpdateStatus("Step 1/4: Detecting ground...");
            bool planeFound = false;
            yield return StartCoroutine(WaitForGroundPlane((success) => { planeFound = success; }));
            if (!planeFound)
            {
                LogErrorAR("Aborting sequence: No ground plane was found.");
                yield break; // STOP the entire sequence
            }

            UpdateStatus("Step 2/4: Aligning compass...");
            yield return StartCoroutine(SetupCompassAlignment());
        }

        UpdateStatus("Step 3/4: Fetching route...");
        yield return StartCoroutine(RequestNavigationRoute(start, dest));
        if (rawRoutePoints.Count == 0)
        {
            LogErrorAR("Aborting sequence: No route was received from Mapbox.");
            yield break;
        }

        // Since we're in the editor, we'll skip the "wait for user" step as well
        if (useTestRoute)
        {
            UpdateStatus("Processing route...");
            yield return StartCoroutine(ProcessRouteData(rawRoutePoints));
        }
        else
        {
            UpdateStatus("Step 4/4: Waiting for user at start...");
            bool userIsAtStart = false;
            yield return StartCoroutine(WaitForUserAtStart((success) => { userIsAtStart = success; }));
            if (!userIsAtStart)
            {
                LogErrorAR("Aborting sequence: User is not at the starting point.");
                yield break;
            }
            UpdateStatus("Processing route...");
            yield return StartCoroutine(ProcessRouteData(rawRoutePoints));
        }
    }
    private IEnumerator WaitForUserAtStart(System.Action<bool> callback)
    {
        if (useTestRoute)
        {
            Debug.Log("[AR_NAV] In Test Mode, skipping 'Wait For User At Start'.");
            callback(true); // Report success immediately
            yield break;
        }
        _isWaitingForUserAtStart = true;
        if (Input.location.status != LocationServiceStatus.Running)
        {
            UpdateStatus("GPS not running. Cannot verify start position.");
            _isWaitingForUserAtStart = false;
            callback(false); // Report failure
            yield break;
        }

        Vector2d startOfRoute = rawRoutePoints[0];
        double distance = double.MaxValue;

        while (distance > startProximityThreshold)
        {
            var currentGps = Input.location.lastData;
            Vector2d currentUserLocation = new Vector2d(currentGps.latitude, currentGps.longitude);

            distance = CalculateDistance(currentUserLocation, startOfRoute);

            UpdateStatus($"Proceed to starting point\n({distance:F0}m away)");
            yield return new WaitForSeconds(1);
        }

        Debug.Log("[AR_NAV] ✅ User has reached the starting point!");
        _isWaitingForUserAtStart = false;
        callback(true); // Report success
    }
    // This helper method calculates the distance between two GPS coordinates in meters.
    private double CalculateDistance(Vector2d latLon1, Vector2d latLon2)
    {
        const double R = 6371000; // Earth's radius in meters

        double lat1Rad = latLon1.x * Math.PI / 180;
        double lat2Rad = latLon2.x * Math.PI / 180;
        double deltaLat = (latLon2.x - latLon1.x) * Math.PI / 180;
        double deltaLon = (latLon2.y - latLon1.y) * Math.PI / 180;

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c; // Distance in meters
    }
    private IEnumerator WaitForGroundPlane(System.Action<bool> callback)
    {
        if (scanningIndicator != null) scanningIndicator.SetActive(true);

        // Wait for up to 30 seconds to find a suitable plane
        float timeout = 30f;
        while (!planeLocked && timeout > 0)
        {
            yield return null;
            timeout -= Time.deltaTime;
        }

        if (scanningIndicator != null) scanningIndicator.SetActive(false);

        // Check the result and report back using the callback
        if (!planeLocked)
        {
            LogErrorAR("❌ Failed to detect ground plane after timeout.");
            UpdateStatus("Failed to detect ground. Please restart and point camera down.");
            callback(false); // Report failure
        }
        else
        {
            callback(true); // Report success
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Hide all plane visuals
        foreach (var plane in args.added) HidePlaneVisuals(plane);
        foreach (var plane in args.updated) HidePlaneVisuals(plane);

        // Lock onto first suitable plane
        if (!planeLocked)
        {
            foreach (var plane in planeManager.trackables)
            {
                if (IsSuitableForNavigation(plane))
                {
                    LogAR($"✅ Locked onto ground plane: {plane.trackableId}");
                    primaryPlane = plane;
                    planeLocked = true;
                    break;
                }
            }
        }
    }

    private void HidePlaneVisuals(ARPlane plane)
    {
        var rend = plane.GetComponent<Renderer>();
        if (rend) rend.enabled = false;

        var lineRend = plane.GetComponent<LineRenderer>();
        if (lineRend) lineRend.enabled = false;
    }

    private bool IsSuitableForNavigation(ARPlane plane)
    {
        return plane.alignment == PlaneAlignment.HorizontalUp &&
               plane.trackingState == TrackingState.Tracking &&
               (plane.size.x * plane.size.y) > 0.5f;
    }

    private IEnumerator SetupCompassAlignment()
    {
        Input.compass.enabled = true;
        yield return new WaitForSeconds(1f);

        if (useDeviceGPS && Input.compass.trueHeading > 0)
        {
            northAlignment = Quaternion.Euler(0, -Input.compass.trueHeading, 0);
            LogAR($"✅ Compass aligned: {Input.compass.trueHeading:F1}°");
        }
        else
        {
            northAlignment = Quaternion.Euler(0, -arCamera.transform.eulerAngles.y, 0);
            LogAR("⚠ Using camera direction as north");
        }
    }

    #endregion
    #region API Debugging

    // This is the public method you will link your "Run API Test" button to.
    public void RunAPITest()
    {
        if (DataManager.Instance != null && DataManager.Instance.startCoordinates.HasValue && DataManager.Instance.destinationCoordinates.HasValue)
        {
            StartCoroutine(TestAPIResponseOnScreen(
                DataManager.Instance.startCoordinates.Value,
                DataManager.Instance.destinationCoordinates.Value
            ));
        }
        else
        {
            Debug.LogError("No route data in DataManager to test with!");
            if (apiDebugPanel != null) apiDebugPanel.SetActive(true);
            if (apiLogText != null) apiLogText.text = "<color=red>ERROR:</color> No Start/Destination coordinates found in DataManager. Go back to the map scene and select a route.";
        }
    }

    private IEnumerator TestAPIResponseOnScreen(Vector2d start, Vector2d end)
    {
        if (apiDebugPanel == null || apiLogText == null)
        {
            Debug.LogError("API Debug UI has not been assigned in the Inspector!");
            yield break;
        }

        apiDebugPanel.SetActive(true);
        apiLogText.text = "Requesting route from Mapbox...";

        var waypoints = new Vector2d[] { start, end };
        var directionResource = new DirectionResource(waypoints, routeProfile) { Steps = true, Overview = Overview.Full };

        bool completed = false;
        DirectionsResponse response = null;
        _directions.Query(directionResource, (res) => { response = res; completed = true; });

        yield return new WaitUntil(() => completed);

        var logBuilder = new System.Text.StringBuilder();
        logBuilder.AppendLine("--- MAPBOX API RESPONSE ---");

        if (response != null && response.Routes != null && response.Routes.Count > 0)
        {
            var route = response.Routes[0];
            logBuilder.AppendLine($"<color=green>SUCCESS!</color>");
            logBuilder.AppendLine($"Distance: {(route.Distance / 1000):F2} km");
            logBuilder.AppendLine($"Duration: {TimeSpan.FromSeconds(route.Duration):g}");
            logBuilder.AppendLine($"Geometry Points: {route.Geometry?.Count ?? 0}");
            logBuilder.AppendLine("\n--- TURN-BY-TURN STEPS ---");

            if (route.Legs != null && route.Legs.Count > 0 && route.Legs[0].Steps != null)
            {
                int stepCount = 1;
                foreach (var step in route.Legs[0].Steps)
                {
                    logBuilder.AppendLine($"{stepCount++}. {step.Maneuver.Instruction} (in {step.Distance:F0}m)");
                }
            }
        }
        else
        {
            logBuilder.AppendLine("<color=red>API CALL FAILED</color>");
            logBuilder.AppendLine("Check API Key, Internet Connection, and that coordinates are valid for the selected route profile.");
        }

        apiLogText.text = logBuilder.ToString();
    }

    // This is the public method you will link your "Close" button to.
    public void CloseDebugPanel()
    {
        if (apiDebugPanel != null)
        {
            apiDebugPanel.SetActive(false);
        }
    }

    #endregion
    #region Route Management

    private IEnumerator RequestNavigationRoute(Vector2d start, Vector2d dest)
    {
        var waypoints = new Vector2d[] { start, dest };
        var directionResource = new DirectionResource(waypoints, routeProfile)
        {
            Steps = true,
            Overview = Overview.Full
        };

        bool queryCompleted = false;
        DirectionsResponse response = null;

        _directions.Query(directionResource, (res) =>
        {
            response = res;
            queryCompleted = true;
        });

        // Wait for the API call to complete
        yield return new WaitUntil(() => queryCompleted);

        // This method's ONLY job is to get the route points and store them.
        if (response != null && response.Routes != null && response.Routes.Count > 0)
        {
            var route = response.Routes[0];
            LogAR($"✅ Route received: {response.Routes[0].Geometry.Count} points");
            rawRoutePoints = response.Routes[0].Geometry;
            if (route.Legs != null && route.Legs.Count > 0)
            {
                _routeSteps = route.Legs[0].Steps;
                _currentStepIndex = 0; // Reset for the new route
                _maneuverARPositions.Clear();
                routeOrigin = rawRoutePoints[0];
                foreach (var step in _routeSteps)
                {
                    _maneuverARPositions.Add(ConvertGPSToAR(step.Maneuver.Location));
                }
                LogAR($"✅ Stored {_routeSteps.Count} turn-by-turn steps and {_maneuverARPositions.Count} maneuver points for TTS.");
            }
            ProcessRouteData(rawRoutePoints);
        }
        else
        {
            LogErrorAR("❌ Route request failed!");
            rawRoutePoints.Clear(); // Ensure the list is empty on failure
        }
    }
    // Add this new method to RouteManager.cs
    // REPLACE your InitializeLocationServices method in RouteManager.cs
    private IEnumerator InitializeLocationServices()
    {
        // We no longer start the service here. We just wait for it to be ready.
        if (useTestRoute) yield break;

        float maxWait = 20f;
        while (Input.location.status != LocationServiceStatus.Running && maxWait > 0)
        {
            UpdateStatus($"Waiting for GPS lock... ({maxWait:F0})");
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            UpdateStatus("GPS signal lost or timed out.");
        }
    }
    private IEnumerator ProcessRouteData(List<Vector2d> rawRoutePoints)
    {
        if (rawRoutePoints == null || rawRoutePoints.Count < 2)
        {
            UpdateStatus("Invalid route data.");
            yield break;
        }

        // --- SAFETY CHECK #1: Ensure arrow spacing is a positive number ---
        if (_currentArrowSpacing <= 0.1f)
        {
            LogErrorAR($"Invalid Arrow Spacing: {_currentArrowSpacing}. Defaulting to 10m.");
            _currentArrowSpacing = 10f;
        }

        routeOrigin = rawRoutePoints[0];
        routeARPositions.Clear();

        List<Vector3> sparseARPoints = new List<Vector3>();
        foreach (var point in rawRoutePoints)
        {
            sparseARPoints.Add(ConvertGPSToAR(point));
        }

        float distanceSinceLastArrow = 0f;
        routeARPositions.Add(sparseARPoints[0]);

        for (int i = 0; i < sparseARPoints.Count - 1; i++)
        {
            Vector3 segmentStart = sparseARPoints[i];
            Vector3 segmentEnd = sparseARPoints[i + 1];
            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            Vector3 direction = (segmentEnd - segmentStart).normalized;

            int safetyCounter = 0; // Prevents infinite loops on weird data
            int maxPointsPerSegment = 1000; // A very high limit

            while (distanceSinceLastArrow + _currentArrowSpacing < segmentLength)
            {
                // --- SAFETY CHECK #2: Break if the loop runs too many times ---
                if (safetyCounter++ > maxPointsPerSegment)
                {
                    LogErrorAR($"Infinite loop detected in densification on segment {i}. Aborting segment.");
                    break; // Exit the while loop
                }

                distanceSinceLastArrow += _currentArrowSpacing;
                Vector3 newPoint = segmentStart + direction * distanceSinceLastArrow;
                routeARPositions.Add(newPoint);

                yield return null;
            }
            distanceSinceLastArrow -= segmentLength;
        }

        routeARPositions.Add(sparseARPoints[sparseARPoints.Count - 1]);
        LogAR($"Densified route from {rawRoutePoints.Count} to {routeARPositions.Count} points.");

        navigationReady = true;
        lastUserPositionForUpdate = userARPosition;
        UpdateDynamicArrowSpawning();
        UpdateStatus("Navigation Ready. Follow the arrows!");
    }

    private Vector3 ConvertGPSToAR(Vector2d gpsPoint)
    {
        double latDiff = gpsPoint.x - routeOrigin.x;
        double lonDiff = gpsPoint.y - routeOrigin.y;

        const double metersPerDegreeLat = 111320.0;
        double metersPerDegreeLon = 111320.0 * Math.Cos(routeOrigin.x * Math.PI / 180.0);

        double xMeters = lonDiff * metersPerDegreeLon;
        double zMeters = latDiff * metersPerDegreeLat;

        Vector3 result = new Vector3((float)xMeters, 0, (float)zMeters);
        return northAlignment * result;
    }

    #endregion
    public void GoToIntroScene()
    {
        
        UnityEngine.SceneManagement.SceneManager.LoadScene("IntroScene");
    }
    #region Dynamic Arrow Spawning

    private void UpdateDynamicArrowSpawning()
    {
        if (routeARPositions.Count < 2 || primaryPlane == null) return;

        // --- 1. Find the user's progress along the path ---
        int closestIndex = FindClosestRouteIndex();
        lastClosestRouteIndex = closestIndex;
        float distanceAlongPath = CalculateDistanceAlongPath(closestIndex);

        // --- 2. Define the "visibility window" of arrows around the user ---
        float visibleStart = distanceAlongPath - despawnBehindDistance;
        float visibleEnd = distanceAlongPath + spawnAheadDistance;

        // --- 3. Determine which arrows are needed (SIMPLIFIED LOGIC) ---
        HashSet<int> requiredIndices = new HashSet<int>();
        float cumulativeDistance = 0;
        for (int i = 0; i < routeARPositions.Count; i++)
        {
            if (i > 0)
            {
                cumulativeDistance += Vector3.Distance(routeARPositions[i - 1], routeARPositions[i]);
            }

            // If this arrow's position is within the visible window, we need it.
            if (cumulativeDistance >= visibleStart && cumulativeDistance <= visibleEnd)
            {
                requiredIndices.Add(i);
            }
        }

        // --- 4. Despawn arrows that are no longer required ---
        DespawnOutsideArrows(requiredIndices);

        // --- 5. Spawn new arrows that are now required ---
        SpawnRequiredArrows(requiredIndices);
    }

    private int FindClosestRouteIndex()
    {
        float closestDist = float.MaxValue;
        int closestIndex = lastClosestRouteIndex;
        Vector3 userPos2D = new Vector3(userARPosition.x, 0, userARPosition.z);

        int searchStart = Mathf.Max(0, lastClosestRouteIndex - 5);
        int searchEnd = Mathf.Min(routeARPositions.Count - 1, lastClosestRouteIndex + 20);

        for (int i = searchStart; i < searchEnd; i++)
        {
            Vector3 routePos2D = new Vector3(routeARPositions[i].x, 0, routeARPositions[i].z);
            float dist = Vector3.Distance(userPos2D, routePos2D);

            if (dist < closestDist)
            {
                closestDist = dist;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private float CalculateDistanceAlongPath(int upToIndex)
    {
        float distance = 0;
        for (int i = 0; i < upToIndex && i < routeARPositions.Count - 1; i++)
        {
            distance += Vector3.Distance(routeARPositions[i], routeARPositions[i + 1]);
        }
        return distance;
    }
    private void DespawnOutsideArrows(HashSet<int> requiredIndices)
    {
        var toRemove = new List<int>();

        foreach (var kvp in routeIndexToArrow)
        {
            if (!requiredIndices.Contains(kvp.Key))
            {
                ReturnArrowToPool(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }

        foreach (int index in toRemove)
        {
            routeIndexToArrow.Remove(index);
        }
    }
    private void SpawnRequiredArrows(HashSet<int> requiredIndices)
    {
        foreach (int index in requiredIndices)
        {
            if (!routeIndexToArrow.ContainsKey(index))
            {
                SpawnArrowAtIndex(index);
            }
        }
    }
    private void SpawnArrowAtIndex(int routeIndex)
    {
        // Safety check to ensure the index is valid for our route data
        if (routeIndex >= rawRoutePoints.Count) return;

        // Get an arrow from the pool
        GameObject arrow = GetArrowFromPool();

        // --- NEW CONTINUOUS RAYCASTING LOGIC ---

        // 1. Get the target GPS coordinate for this specific arrow.
        Vector2d targetGps = rawRoutePoints[routeIndex];

        // 2. Convert the GPS coordinate to a 3D world position. 
        // The initial Y height is just an estimate; we'll correct it next.
        Vector3 initialWorldPosition = map.GeoToWorldPosition(targetGps);

        // 3. Project this 3D point onto the 2D screen to find where to raycast from.
        Vector2 screenPoint = arCamera.WorldToScreenPoint(initialWorldPosition);

        // 4. Perform the raycast to find the real ground.
        List<ARRaycastHit> hits = new List<ARRaycastHit>();
        Pose hitPose;
        if (raycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon))
        {
            // SUCCESS: We found a plane. Use its exact position and orientation.
            hitPose = hits[0].pose;
        }
        else
        {
            // FALLBACK: No plane detected at that spot.
            // We'll create a pose at the estimated position, flat on the horizon.
            Vector3 fallbackPosition = initialWorldPosition;
            fallbackPosition.y = arCamera.transform.position.y - 1.5f; // Estimate ground is 1.5m below camera
            hitPose = new Pose(fallbackPosition, Quaternion.identity);
        }

        // --- Anchor creation and parenting now uses the accurate 'hitPose' ---

        // Create the anchor using the new, correct method
        GameObject anchorObject = new GameObject($"RouteAnchor_{routeIndex}");
        anchorObject.transform.position = hitPose.position;
        anchorObject.transform.rotation = hitPose.rotation; // Align the anchor with the detected ground surface!
        ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();

        if (anchor == null)
        {
            LogErrorAR($"Failed to create ARAnchor for index {routeIndex}");
            ReturnArrowToPool(arrow);
            Destroy(anchorObject);
            return;
        }

        // Parent the arrow to the anchor for rock-solid stability
        arrow.transform.SetParent(anchor.transform, false);
        arrow.transform.localScale = Vector3.one * arrowScale;

        // Add the height offset along the anchor's "up" direction (normal to the ground)
        arrow.transform.localPosition = new Vector3(0, arrowHeightOffset, 0);

        // Calculate rotation based on the next point in the route
        if (routeIndex < rawRoutePoints.Count - 1)
        {
            Vector3 nextWorldPos = map.GeoToWorldPosition(rawRoutePoints[routeIndex + 1]);
            Vector3 direction = nextWorldPos - anchor.transform.position; // Direction from this anchor to the next point

            // Project the direction onto the anchor's plane to keep the arrow flat
            Vector3 directionOnPlane = Vector3.ProjectOnPlane(direction, anchor.transform.up);

            if (directionOnPlane.sqrMagnitude > 0.001f)
            {
                arrow.transform.rotation = Quaternion.LookRotation(directionOnPlane, anchor.transform.up);
            }
        }

        // (Your existing code for metadata and registration)
        ArrowMetadata meta = arrow.GetComponent<ArrowMetadata>();
        if (meta == null) meta = arrow.AddComponent<ArrowMetadata>();
        meta.RouteIndex = routeIndex;
        meta.Anchor = anchor;
        routeIndexToArrow[routeIndex] = arrow;
        // --- COLOR CODING LOGIC ---
        var arrowRenderer = arrow.GetComponent<MeshRenderer>();
        if (arrowRenderer != null)
        {
            if (routeIndex == 0) // First arrow
            {
                arrowRenderer.material = StartArrow_Mat; // You'll need to create a public Material field for this
            }
            else if (routeIndex >= routeARPositions.Count - 2) // Last one or two arrows
            {
                arrowRenderer.material = EndArrow_Mat;
            }
            else // All arrows in between
            {
                arrowRenderer.material = MidArrow_Mat;
            }
        }
    }
    #endregion

    #region Utility Methods

    private void UpdateUserPosition()
    {
        if (arCamera != null)
        {
            userARPosition = arCamera.transform.position;
        }
    }

    public void UpdateStatus(string status)
    {
        currentStatus = status;
        LogAR($"📊 {status}");
        if (statusText != null) statusText.text = status;
    }

    private void UpdateDebugDisplay()
    {
        if (debugText != null)
        {
            debugText.text = $"AR Navigation Debug\n" +
                             $"Status: {currentStatus}\n" +
                             $"Plane: {(planeLocked ? "✓" : "✗")}\n" +
                             $"Route Points: {routeARPositions.Count}\n" +
                             $"Active Arrows: {activeArrows.Count}/{poolSize}\n" +
                             $"Pool Available: {arrowPool.Count}\n" +
                             $"User Pos: {userARPosition.ToString("F1")}\n" +
                             $"Closest Index: {lastClosestRouteIndex}";
        }
    }

    public void ReturnToMap()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("IntroScene");
    }

    #endregion
}