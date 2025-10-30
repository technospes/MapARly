using Mapbox.Directions;
using Mapbox.Geocoding;
using Mapbox.Unity;
using Mapbox.Unity.MeshGeneration.Components;
using Mapbox.Unity.MeshGeneration.Data;
using Mapbox.Unity.Utilities;
using Mapbox.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using UnityEngine.UI;

public class SearchManager : MonoBehaviour
{
    [Header("API Key")]
    [SerializeField] private string googleApiKey; // Paste your Google API Key here
    [Header("Google API Settings")]
    [SerializeField] private int searchRadiusMeters = 1;
    [Header("Route Drawing")]
    public GameObject routeLinePrefab;
    private GameObject currentRouteLine;
    public GameObject tempPinPrefab;
    private Vector2d? _currentTempPinCoordinates;
    private GameObject currentTempPin;
    public Button startSearchButton;
    public Button destinationSearchButton;
    [Header("User Location")]
    public GameObject userLocationMarkerPrefab; // The blue dot prefab we just created
    [SerializeField] private float zoomToLevel = 16f; // How close to zoom in (16 is a good street-level view)
    private GameObject currentUserLocationMarker; // A reference to the spawned marker
    private Vector2d? _currentUserCoordinates;
    private bool _isFollowingUser = false;
    private bool _isAutoCentering = false;
    [SerializeField] private float _minTrackingDistance = 2.0f;
    [Header("UI Elements")]
    public TMP_InputField startInput;
    public TMP_InputField destinationInput;
    public Button startNavigationButton;
    public GameObject suggestionButtonPrefab;
    public Button useCurrentLocationButton;
    public GameObject GpsPromptPanel;
    [Header("POI Info Panel")]
    public GameObject poiInfoPanelPrefab;
    private GameObject currentInfoPanel;
    [Header("Suggestion UI (Start)")]
    public GameObject suggestionScrollViewStart;
    public Transform suggestionContentStart;
    [Header("Editor Testing")]
    public Vector2d editorGpsSimulation = new Vector2d(28.457529975582364, 77.49697412950252);
    [Header("Suggestion UI (Destination)")]
    public GameObject suggestionScrollViewDest;
    public Transform suggestionContentDest;
    public Button findRouteButton;
    [Header("Dependencies")]
    public Mapbox.Unity.Map.AbstractMap map;
    private List<Vector2d> _currentRouteGeometry;
    [SerializeField] private float _routeBaseWidth = 0.0008f;
    [Header("Search Logic")]
    [SerializeField] private float debounceTime = 0.5f;
    // Private variables to hold selected coordinates and IDs
    private Vector2d? startCoordinates;
    private string startPlaceId;
    private Vector2d? destinationCoordinates;
    private string destinationPlaceId;

    private Coroutine debounceCoroutine;

    // --- Data classes for parsing Google's JSON responses ---
    [System.Serializable]
    private class SimpleFeatureProperties { public string name; }
    [System.Serializable]
    private class GooglePlacesResponse { public Prediction[] predictions; }
    [System.Serializable]
    private class Prediction { public string description; public string place_id; }
    [System.Serializable]
    private class GoogleDetailsResponse { public PlaceDetailsResult result; }
    [System.Serializable]
    private class PlaceDetailsResult { public Geometry geometry; }
    [System.Serializable]
    private class Geometry { public Location location; }
    [System.Serializable]
    private class Location { public double lat; public double lng; }
    // Add these inside your SearchManager class
    [System.Serializable]
    private class GoogleDetailsWithPhotosResponse { public PlaceDetailsWithPhotosResult result; }
    [System.Serializable]
    private class PlaceDetailsWithPhotosResult { public string name; public double rating; public Photo[] photos; }
    [System.Serializable]
    private class Photo { public string photo_reference; }
    void Start()
    {
        if (string.IsNullOrEmpty(googleApiKey) || googleApiKey == "YOUR_GOOGLE_API_KEY")
        {
            Debug.LogError("Google API Key is missing!");
        }
        suggestionScrollViewStart.SetActive(false);
        suggestionScrollViewDest.SetActive(false);
        startInput.onValueChanged.AddListener(OnStartInputChanged);
        destinationInput.onValueChanged.AddListener(OnDestinationInputChanged);
        startNavigationButton.onClick.AddListener(OnStartNavigationClicked);
        useCurrentLocationButton.onClick.AddListener(OnUseCurrentLocationClicked);
        findRouteButton.onClick.AddListener(OnFindRouteClicked);
        map.OnUpdated += UpdateMapElements;
        startSearchButton.onClick.AddListener(OnStartSearchClicked);
        destinationSearchButton.onClick.AddListener(OnDestinationSearchClicked);
        startSearchButton.gameObject.SetActive(false);
        destinationSearchButton.gameObject.SetActive(false);
        //profileSelectionPanel.SetActive(false);
        //walkingProfileButton.onClick.AddListener(OnWalkingProfileSelected);
        //drivingProfileButton.onClick.AddListener(OnDrivingProfileSelected);
    }
    void Update()
    {
        // Left click for map/waypoint selection
        if (Input.GetMouseButtonDown(0))
        {
            if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                HandleMapClick();
            }
            else
            {
                Debug.Log("Click blocked by UI element");
            }
        }
        // Right click for temporary pins
        if (Input.GetMouseButtonDown(1))
        {
            if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                HandleMapRightClick();
            }
        }
    }
    void HandleMapRightClick()
    {
        // Clean up any old temporary pin or info panel
        if (currentTempPin != null) { Destroy(currentTempPin); }
        if (currentInfoPanel != null) { Destroy(currentInfoPanel); }

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // Raycast to find where the user clicked on the map's collider
        if (Physics.Raycast(ray, out hit))
        {
            // We hit the map! Spawn a temporary pin at the hit location.
            currentTempPin = Instantiate(tempPinPrefab, hit.point, Quaternion.identity);

            // Convert the 3D world point to a GPS coordinate
            Vector2d tappedCoordinates = map.WorldToGeoPosition(hit.point);

            // Now, show the info panel at the same spot to let the user choose
            // We'll use a generic name since we don't know what this location is
            ShowPoiInfoPanel("Selected Location", tappedCoordinates, Input.mousePosition);
        }
    }
    private void OnDestroy()
    {
        // ADD THIS METHOD
        if (map != null)
        {
            //map.OnUpdated -= RedrawCurrentRoute;
            map.OnUpdated -= UpdateMapElements;
        }
    }
    // This method is called automatically by the map whenever it's panned or zoomed.
    private void RedrawCurrentRoute()
    {
        // Check if we have a route's geometry stored
        if (_currentRouteGeometry != null && _currentRouteGeometry.Count > 0)
        {
            // If we do, simply redraw it.
            // The existing DrawRouteLine method already handles the coordinate conversion correctly.
            DrawRouteLine(_currentRouteGeometry);
        }
    }
    // This is our new "master" handler that is called every time the map pans or zooms.
    private void UpdateMapElements()
    {
        RedrawCurrentRoute();    // Redraws the route line
        RepositionUserMarker();  // Repositions the user marker
        RepositionTemporaryPin();
    }
    // This method updates the temporary pin's position.
    private void RepositionTemporaryPin()
    {
        // Check if a pin exists and if we have its location stored
        if (currentTempPin != null && _currentTempPinCoordinates.HasValue)
        {
            // Calculate the new world position from the stored GPS coordinate
            Vector3 newWorldPosition = map.GeoToWorldPosition(_currentTempPinCoordinates.Value, true);

            // Apply the new position to the pin's transform
            currentTempPin.transform.position = newWorldPosition + Vector3.up * 5f; // Maintain the height offset
        }
    }

    // This method contains the logic to update the user marker's position.
    private void RepositionUserMarker()
    {
        // Check if we have a marker and a stored location
        if (currentUserLocationMarker != null && _currentUserCoordinates.HasValue)
        {
            // Calculate the new world position based on the stored GPS coordinate
            Vector3 newWorldPosition = map.GeoToWorldPosition(_currentUserCoordinates.Value, true);

            // Apply the new position to the marker
            currentUserLocationMarker.transform.position = newWorldPosition;
        }
    }
    private void OnStartInputChanged(string query)
    {
        if (debounceCoroutine != null) StopCoroutine(debounceCoroutine);
        if (string.IsNullOrWhiteSpace(query))
        {
            suggestionScrollViewStart.SetActive(false);
            return;
        }
        debounceCoroutine = StartCoroutine(DebounceSearch(query, (finalQuery) => {
            StartCoroutine(GetGoogleAutocompleteSuggestions(finalQuery, suggestionScrollViewStart, suggestionContentStart, true));
        }));
    }

    // THIS IS THE FINAL, DEFINITIVE METHOD for v2.1.1
    void HandleMapClick()
    {
        Debug.Log("🔍 HandleMapClick called (for non-waypoint clicks)");

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // Only try to raycast if we're NOT over a UI element
        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            Debug.Log($"✓ Raycast HIT: {hit.collider.gameObject.name}");

            // Try to find FeatureBehaviour in parents
            var featureBehaviour = hit.collider.GetComponentInParent<FeatureBehaviour>();

            if (featureBehaviour != null && !string.IsNullOrEmpty(featureBehaviour.DataString))
            {
                OnWaypointClicked(featureBehaviour, hit.point);
            }
            else
            {
                Debug.Log("Hit something but it's not a waypoint");
            }
        }
        else
        {
            Debug.Log("Raycast didn't hit anything");
        }
    }
    void ShowPoiInfoPanel(string name, Vector2d coordinates, Vector3 screenPosition)
    {
        if (currentInfoPanel != null) { Destroy(currentInfoPanel); }
        if (poiInfoPanelPrefab == null) { return; }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) { return; }

        currentInfoPanel = Instantiate(poiInfoPanelPrefab, canvas.transform);
        RectTransform rectTransform = currentInfoPanel.GetComponent<RectTransform>();
        rectTransform.anchoredPosition = Vector2.zero;

        // THE FIX: Get the UI component instead of using Find()
        PoiPanelUI panelUI = currentInfoPanel.GetComponent<PoiPanelUI>();
        if (panelUI == null)
        {
            Debug.LogError("PoiInfoPanel prefab is missing the PoiPanelUI script!");
            return;
        }

        panelUI.nameText.text = name;
        panelUI.ratingText.text = "Loading..."; // Set a default
        panelUI.photoImage.gameObject.SetActive(false);

        // Setup buttons
        panelUI.setStartButton.onClick.AddListener(() => SetLocationFromPanel(name, coordinates, true));
        panelUI.setDestinationButton.onClick.AddListener(() => SetLocationFromPanel(name, coordinates, false));
        panelUI.closeButton.onClick.AddListener(() => { if (currentInfoPanel != null) Destroy(currentInfoPanel); });
    }
    public void OnWaypointClicked(FeatureBehaviour featureBehaviour, Vector3 worldPosition)
    {
        Debug.Log($"🎯 Waypoint clicked. Fetching all details from Google...");

        string fallbackName = "Selected Location";
        if (featureBehaviour != null && !string.IsNullOrEmpty(featureBehaviour.DataString))
        {
            var properties = JsonUtility.FromJson<SimpleFeatureProperties>(featureBehaviour.DataString);
            if (properties != null && !string.IsNullOrEmpty(properties.name))
            {
                fallbackName = properties.name;
            }
        }

        Vector2d poiCoordinates = map.WorldToGeoPosition(worldPosition);

        // Start the new, combined coroutine
        StartCoroutine(FetchDetailsAndShowPanel(poiCoordinates, fallbackName));
    }
    private IEnumerator FetchDetailsAndShowPanel(Vector2d coordinates, string fallbackName)
    {
        string placeId = null;
        string bestName = fallbackName;
        double rating = 0;
        string photoReference = null;

        // === STRATEGY 1: Try Text Search First (Most Accurate for Named Places) ===
        Debug.Log($"🔍 Strategy 1: Text Search for '{fallbackName}'");

        string query = Uri.EscapeDataString(fallbackName);
        string textSearchUrl = $"https://maps.googleapis.com/maps/api/place/textsearch/json" +
                               $"?query={query}" +
                               $"&location={coordinates.x},{coordinates.y}" +
                               $"&radius=500" +
                               $"&key={googleApiKey}";

        using (UnityWebRequest textSearchRequest = UnityWebRequest.Get(textSearchUrl))
        {
            yield return textSearchRequest.SendWebRequest();

            if (textSearchRequest.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<GoogleNearbySearchResponseDetailed>(textSearchRequest.downloadHandler.text);

                if (response != null && response.status == "OK" && response.results != null && response.results.Length > 0)
                {
                    var firstResult = response.results[0];
                    placeId = firstResult.place_id;
                    bestName = firstResult.name;
                    rating = firstResult.rating;

                    if (firstResult.photos != null && firstResult.photos.Length > 0)
                    {
                        photoReference = firstResult.photos[0].photo_reference;
                    }

                    Debug.Log($"✓ Text Search found: '{bestName}' with {(photoReference != null ? "photo" : "no photo")}");
                }
            }
        }

        // === STRATEGY 2: If no photo, try Nearby Search (Finds closest place by location) ===
        if (string.IsNullOrEmpty(photoReference))
        {
            Debug.Log($"🔍 Strategy 2: Nearby Search at exact coordinates");

            string nearbyUrl = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
                               $"?location={coordinates.x},{coordinates.y}" +
                               $"&radius=100" +
                               $"&key={googleApiKey}";

            using (UnityWebRequest nearbyRequest = UnityWebRequest.Get(nearbyUrl))
            {
                yield return nearbyRequest.SendWebRequest();

                if (nearbyRequest.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<GoogleNearbySearchResponseDetailed>(nearbyRequest.downloadHandler.text);

                    if (response != null && response.status == "OK" && response.results != null && response.results.Length > 0)
                    {
                        // Find the closest place with a photo
                        foreach (var result in response.results)
                        {
                            if (result.photos != null && result.photos.Length > 0)
                            {
                                // Calculate distance to verify it's actually close
                                double distance = CalculateDistance(
                                    coordinates.x, coordinates.y,
                                    result.geometry.location.lat, result.geometry.location.lng
                                );

                                if (distance < 200) // Within 200 meters
                                {
                                    placeId = result.place_id;
                                    bestName = result.name;
                                    rating = result.rating;
                                    photoReference = result.photos[0].photo_reference;

                                    Debug.Log($"✓ Nearby Search found: '{bestName}' at {distance:F0}m with photo");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // === STRATEGY 3: If we have place_id but no photo, get full details ===
        if (!string.IsNullOrEmpty(placeId) && string.IsNullOrEmpty(photoReference))
        {
            Debug.Log($"🔍 Strategy 3: Fetching full Place Details for more photos");

            string detailsUrl = $"https://maps.googleapis.com/maps/api/place/details/json" +
                                $"?place_id={placeId}" +
                                $"&fields=name,rating,photos" +
                                $"&key={googleApiKey}";

            using (UnityWebRequest detailsRequest = UnityWebRequest.Get(detailsUrl))
            {
                yield return detailsRequest.SendWebRequest();

                if (detailsRequest.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<GooglePlaceDetailsResponse>(detailsRequest.downloadHandler.text);

                    if (response?.result != null)
                    {
                        if (response.result.photos != null && response.result.photos.Length > 0)
                        {
                            photoReference = response.result.photos[0].photo_reference;
                            Debug.Log($"✓ Place Details found photo!");
                        }

                        // Update other info too
                        if (!string.IsNullOrEmpty(response.result.name))
                        {
                            bestName = response.result.name;
                        }
                        if (response.result.rating > 0)
                        {
                            rating = response.result.rating;
                        }
                    }
                }
            }
        }

        // === FINAL RESULT ===
        if (string.IsNullOrEmpty(photoReference))
        {
            Debug.LogWarning($"⚠ No photo found after trying all strategies for '{fallbackName}'");
        }
        else
        {
            Debug.Log($"✅ Final result: '{bestName}', Rating: {rating}, Has Photo: Yes");
        }

        // === Show the panel ===
        ShowPoiInfoPanel(bestName, coordinates, Vector3.zero);

        if (currentInfoPanel == null) yield break;

        PoiPanelUI panelUI = currentInfoPanel.GetComponent<PoiPanelUI>();
        if (panelUI == null) yield break;

        // Set rating
        if (rating > 0)
        {
            panelUI.ratingText.gameObject.SetActive(true);
            panelUI.ratingText.text = $"⭐ {rating:F1} / 5.0";
        }
        else
        {
            panelUI.ratingText.gameObject.SetActive(false);
        }

        // === Download and display photo ===
        if (!string.IsNullOrEmpty(photoReference))
        {
            string photoUrl = $"https://maps.googleapis.com/maps/api/place/photo?maxwidth=400&photoreference={photoReference}&key={googleApiKey}";

            using (UnityWebRequest photoRequest = UnityWebRequestTexture.GetTexture(photoUrl))
            {
                yield return photoRequest.SendWebRequest();

                if (photoRequest.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(photoRequest);

                    if (texture != null)
                    {
                        panelUI.photoImage.texture = texture;
                        panelUI.photoImage.gameObject.SetActive(true);
                        Debug.Log($"✅ Photo displayed!");
                    }
                }
                else
                {
                    Debug.LogError($"❌ Photo download failed: {photoRequest.error}");
                }
            }
        }
        else
        {
            // Show placeholder instead of hiding
            panelUI.photoImage.texture = CreateNoPhotoPlaceholder();
            panelUI.photoImage.gameObject.SetActive(true);

            // Optionally add a text overlay
            if (panelUI.photoImage.transform.Find("NoPhotoText") != null)
            {
                var noPhotoText = panelUI.photoImage.transform.Find("NoPhotoText").GetComponent<TMP_Text>();
                if (noPhotoText != null)
                {
                    noPhotoText.text = "No Photo Available";
                    noPhotoText.gameObject.SetActive(true);
                }
            }

            Debug.Log("📷 Showing placeholder - no photo available");
        }
    }
    private Texture2D CreateNoPhotoPlaceholder()
    {
        // Create a simple gray texture with text overlay effect
        int width = 400;
        int height = 300;
        Texture2D placeholder = new Texture2D(width, height);

        // Fill with gray color
        Color grayColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                placeholder.SetPixel(x, y, grayColor);
            }
        }

        placeholder.Apply();
        return placeholder;
    }

    // Add this helper method to calculate distance between two GPS coordinates
    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // Haversine formula to calculate distance in meters
        const double R = 6371000; // Earth's radius in meters

        double lat1Rad = lat1 * Math.PI / 180;
        double lat2Rad = lat2 * Math.PI / 180;
        double deltaLat = (lat2 - lat1) * Math.PI / 180;
        double deltaLon = (lon2 - lon1) * Math.PI / 180;

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c; // Distance in meters
    }
    private void SetLocationFromPanel(string placeName, Vector2d coordinates, bool isStart)
    {
        if (isStart)
        {
            startInput.text = placeName;
            startCoordinates = coordinates;
            startPlaceId = null; // Clear the Google Place ID
        }
        else
        {
            destinationInput.text = placeName;
            destinationCoordinates = coordinates;
            destinationPlaceId = null; // Clear the Google Place ID
        }

        // Close the panel after a selection is made
        if (currentInfoPanel != null)
        {
            Destroy(currentInfoPanel);
        }
        if (currentTempPin != null)
        {
            Destroy(currentTempPin);
        }
    }
    // THIS IS THE CORRECT METHOD FOR SearchManager.cs
    private void OnUseCurrentLocationClicked()
    {
        // It simply checks the status of the GPS service that ServicesManager is managing.
        if (Input.location.status == LocationServiceStatus.Running)
        {
            startInput.onValueChanged.RemoveListener(OnStartInputChanged);

            var locationData = Input.location.lastData;
            var userCoordinates = new Vector2d(locationData.latitude, locationData.longitude);

            startInput.text = "My Current Location";
            startCoordinates = userCoordinates;
            _currentUserCoordinates = userCoordinates;
            suggestionScrollViewStart.SetActive(false);

            map.UpdateMap(userCoordinates, zoomToLevel);
            PlaceUserMarker();

            startInput.onValueChanged.AddListener(OnStartInputChanged);
        }
        else
        {
            // If the service isn't ready yet, it just informs the user.
            Debug.LogWarning("Location service is not running. Please wait for GPS signal.");
            startInput.text = "GPS not ready...";
        }
    }
    private void PlaceUserMarker()
{
    // If a marker doesn't exist yet, create it.
    if (currentUserLocationMarker == null)
    {
        if (userLocationMarkerPrefab != null)
        {
            // Spawn the prefab at a temporary position (0,0,0).
            // Its correct position will be set instantly by RepositionUserMarker.
            currentUserLocationMarker = Instantiate(userLocationMarkerPrefab);
            
            // CRITICAL: DO NOT PARENT THE MARKER TO THE MAP.
            // It needs to live in world space so we can control its position directly.
        }
        else
        {
            Debug.LogWarning("User Location Marker Prefab is not assigned!");
            return;
        }
    }

    // Now that we know the marker exists, force an immediate reposition.
    RepositionUserMarker();
}
    private void OnDestinationInputChanged(string query)
    {
        if (debounceCoroutine != null) StopCoroutine(debounceCoroutine);
        if (string.IsNullOrWhiteSpace(query))
        {
            suggestionScrollViewDest.SetActive(false);
            return;
        }
        debounceCoroutine = StartCoroutine(DebounceSearch(query, (finalQuery) => {
            StartCoroutine(GetGoogleAutocompleteSuggestions(finalQuery, suggestionScrollViewDest, suggestionContentDest, false));
        }));
    }

    private IEnumerator DebounceSearch(string query, Action<string> onDebounceComplete)
    {
        yield return new WaitForSeconds(debounceTime);
        onDebounceComplete(query);
    }

    private IEnumerator GetGoogleAutocompleteSuggestions(string query, GameObject scrollView, Transform content, bool isStart)
    {
        // Proximity biasing to get better local results (uses your device's actual GPS on a phone)
        string proximity = "";
        if (Input.location.status == LocationServiceStatus.Running)
        {
            var loc = Input.location.lastData;
            proximity = $"&location={loc.latitude}%2C{loc.longitude}&radius=50000"; // 50km radius bias
        }

        string url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(query)}&key={googleApiKey}{proximity}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<GooglePlacesResponse>(request.downloadHandler.text);
                if (response != null && response.predictions != null)
                {
                    DisplayGoogleSuggestions(response.predictions, scrollView, content, isStart);
                }
            }
            else
            {
                Debug.LogError("Google Places API Error: " + request.error);
            }
        }
    }

    private void DisplayGoogleSuggestions(Prediction[] predictions, GameObject scrollView, Transform content, bool isStart)
    {
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }

        scrollView.SetActive(predictions.Length > 0);

        foreach (var prediction in predictions)
        {
            GameObject buttonGO = Instantiate(suggestionButtonPrefab, content);
            TMP_Text buttonText = buttonGO.GetComponentInChildren<TMP_Text>();
            buttonText.text = prediction.description;

            Button button = buttonGO.GetComponent<Button>();
            button.onClick.AddListener(() => OnSuggestionClicked(prediction, isStart));
        }
    }

    private void OnSuggestionClicked(Prediction selectedPrediction, bool isStartLocation)
    {
        if (isStartLocation)
        {
            startInput.onValueChanged.RemoveListener(OnStartInputChanged);
            startInput.text = selectedPrediction.description;
            startPlaceId = selectedPrediction.place_id;
            startCoordinates = null;
            suggestionScrollViewStart.SetActive(false);
            startInput.onValueChanged.AddListener(OnStartInputChanged);

            // Show the search button!
            startSearchButton.gameObject.SetActive(true);
        }
        else
        {
            destinationInput.onValueChanged.RemoveListener(OnDestinationInputChanged);
            destinationInput.text = selectedPrediction.description;
            destinationPlaceId = selectedPrediction.place_id;
            destinationCoordinates = null;
            suggestionScrollViewDest.SetActive(false);
            destinationInput.onValueChanged.AddListener(OnDestinationInputChanged);

            // Show the search button!
            destinationSearchButton.gameObject.SetActive(true);
        }
    }
    // Called when the start search button is clicked
    public void OnStartSearchClicked()
    {
        // If we don't have coordinates yet, fetch them first, then zoom.
        if (!startCoordinates.HasValue)
        {
            StartCoroutine(FetchAndZoom(startPlaceId, true));
        }
        else // Otherwise, just zoom.
        {
            map.UpdateMap(startCoordinates.Value, zoomToLevel);
            PlaceTemporaryPin(startCoordinates.Value);
        }
    }

    // Called when the destination search button is clicked
    public void OnDestinationSearchClicked()
    {
        if (!destinationCoordinates.HasValue)
        {
            StartCoroutine(FetchAndZoom(destinationPlaceId, false));
        }
        else
        {
            map.UpdateMap(destinationCoordinates.Value, zoomToLevel);
            PlaceTemporaryPin(destinationCoordinates.Value);
        }
    }

    // Helper coroutine to get coordinates AND then zoom
    private IEnumerator FetchAndZoom(string placeId, bool isStart)
    {
        // We already have this method, so we can reuse it!
        yield return StartCoroutine(GetPlaceDetails(placeId, isStart));

        // After it's done, the coordinates will be filled in.
        if (isStart && startCoordinates.HasValue)
        {
            map.UpdateMap(startCoordinates.Value, zoomToLevel);
            PlaceTemporaryPin(startCoordinates.Value);
        }
        else if (!isStart && destinationCoordinates.HasValue)
        {
            map.UpdateMap(destinationCoordinates.Value, zoomToLevel);
            PlaceTemporaryPin(destinationCoordinates.Value);
        }
    }

    // Helper method to place our temporary red pin
    private void PlaceTemporaryPin(Vector2d location)
    {
        // Destroy any old pin
        if (currentTempPin != null)
        {
            Destroy(currentTempPin);
        }

        // Store the new pin's geographical coordinates
        _currentTempPinCoordinates = location;

        if (tempPinPrefab != null)
        {
            // Instantiate the new pin at a temporary spot (it will be moved instantly)
            currentTempPin = Instantiate(tempPinPrefab);

            // CRITICAL: Ensure the pin is NOT a child of the map. It must live in world space.
            // The RepositionTemporaryPin method will handle its position perfectly.

            // Force an immediate reposition to place it correctly right away.
            RepositionTemporaryPin();
        }
    }
    private void OnFindRouteClicked()
    {
    Debug.Log("🗺️ Find Route button clicked (2D preview)!");
    StartCoroutine(FetchCoordinatesAndDrawRoute());
    }
    private void OnStartNavigationClicked()
    {
        Debug.Log("🚀 Start Navigation button clicked!");

        // Check if we have both coordinates
        if (!startCoordinates.HasValue || !destinationCoordinates.HasValue)
        {
            Debug.LogWarning("⚠ Need to fetch coordinates first");
            StartCoroutine(FetchCoordinatesAndLoadARScene());
        }
        else
        {
            // We already have coordinates, load AR scene directly
            LoadARScene();
        }
    }
    private void LoadARScene()
    {
        if (!startCoordinates.HasValue || !destinationCoordinates.HasValue)
        {
            Debug.LogError("❌ Cannot load AR scene without start and destination coordinates!");
            return;
        }

        Debug.Log($"✅ Loading AR Scene with route:");
        Debug.Log($"   Start: {startCoordinates.Value}");
        Debug.Log($"   Destination: {destinationCoordinates.Value}");

        // Save to DataManager
        DataManager.Instance.startCoordinates = startCoordinates;
        DataManager.Instance.destinationCoordinates = destinationCoordinates;

        // Load the AR scene
        UnityEngine.SceneManagement.SceneManager.LoadScene("ARScene");
    }

    private IEnumerator FetchCoordinatesAndDrawRoute()
    {
        Debug.Log("📍 FetchCoordinatesAndDrawRoute started");

        // Clear any old route line
        if (currentRouteLine != null)
        {
            Destroy(currentRouteLine);
        }

        // Check if we have coordinates
        if (!startCoordinates.HasValue || !destinationCoordinates.HasValue)
        {
            Debug.LogWarning("⚠ Start or destination coordinates are missing!");

            // Try to fetch them if we have place IDs
            if (!startCoordinates.HasValue && !string.IsNullOrEmpty(startPlaceId))
            {
                yield return StartCoroutine(GetPlaceDetails(startPlaceId, true));
            }
            if (!destinationCoordinates.HasValue && !string.IsNullOrEmpty(destinationPlaceId))
            {
                yield return StartCoroutine(GetPlaceDetails(destinationPlaceId, false));
            }
        }

        // Verify we now have both coordinates
        if (!startCoordinates.HasValue || !destinationCoordinates.HasValue)
        {
            Debug.LogError("❌ Could not get coordinates for start or destination!");
            yield break;
        }

        Debug.Log($"✓ Start: {startCoordinates.Value}");
        Debug.Log($"✓ Destination: {destinationCoordinates.Value}");

        // Get the route from Mapbox Directions API
        var directions = MapboxAccess.Instance.Directions;
        var waypoints = new Vector2d[] { startCoordinates.Value, destinationCoordinates.Value };

        // You can change RoutingProfile to Driving, Cycling, Walking
        var directionResource = new DirectionResource(waypoints, RoutingProfile.Driving)
        {
            Overview = Overview.Full
        };

        // === CRITICAL FIX: Use flags to track completion ===
        List<Vector2d> routeGeometry = null;
        bool requestComplete = false;

        directions.Query(directionResource, (DirectionsResponse res) =>
        {
            if (res != null && res.Routes != null && res.Routes.Count > 0)
            {
                // SAVE THE ROUTE DATA TO OUR NEW VARIABLE
                _currentRouteGeometry = res.Routes[0].Geometry;

                Debug.Log($"✓ Got route with {_currentRouteGeometry.Count} points");

                // This variable is no longer needed since we are using the callback
                requestComplete = true;
            }
            else
            {
                Debug.LogError("❌ Mapbox Directions API returned no routes!");
                _currentRouteGeometry = null; // Clear any old route
                requestComplete = true;
            }
        });

        // Wait until the request is actually complete
        Debug.Log("⏳ Waiting for Mapbox Directions API response...");
        while (!requestComplete)
        {
            yield return null; // Wait one frame
        }
        Debug.Log("✓ Mapbox API response received!");

        // Now draw the route
        if (routeGeometry != null && routeGeometry.Count > 0)
        {
            DrawRouteLine(routeGeometry);
        }
        else
        {
            Debug.LogError("❌ No route geometry to draw!");
        }
    }

    void DrawRouteLine(List<Vector2d> points)
    {
        if (currentRouteLine != null)
        {
            Destroy(currentRouteLine);
        }
        if (points == null || points.Count < 2)
        {
            return;
        }

        // Create route line in world space (no parent)
        currentRouteLine = Instantiate(routeLinePrefab);
        var lineRenderer = currentRouteLine.GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            Debug.LogError("❌ RouteLine prefab doesn't have a LineRenderer!");
            return;
        }

        // --- CRITICAL SETTINGS ---
        lineRenderer.useWorldSpace = true;       // The line exists in the world
        lineRenderer.alignment = LineAlignment.View; // It always faces the camera

        // --- QUALITY SETTINGS for a smooth line ---
        lineRenderer.numCornerVertices = 5;
        lineRenderer.numCapVertices = 5;
        lineRenderer.textureMode = LineTextureMode.Tile; // Use Tile for consistent texture
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.startColor = new Color(0f, 0.7f, 1f, 1f); // Bright cyan
        lineRenderer.endColor = new Color(0f, 0.7f, 1f, 1f);

        // --- APPLY THE WIDTH DIRECTLY ---
        // The width is now a simple, direct world unit value.
        lineRenderer.startWidth = _routeBaseWidth;
        lineRenderer.endWidth = _routeBaseWidth;

        // --- SET THE ROUTE'S POINTS ---
        Vector3[] worldPositions = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            // Add a 2-unit offset to lift the line above the map terrain
            worldPositions[i] = map.GeoToWorldPosition(points[i], true) + Vector3.up * 2f;
        }
        lineRenderer.positionCount = worldPositions.Length;
        lineRenderer.SetPositions(worldPositions);

        Debug.Log($"✓ Route line drawn in world space with {_routeBaseWidth} width.");
    }
    private IEnumerator FetchCoordinatesAndLoadARScene()
    {
        Debug.Log("📍 Fetching coordinates before loading AR scene...");

        // Fetch coordinates if we have place IDs but not coordinates
        if (!startCoordinates.HasValue && !string.IsNullOrEmpty(startPlaceId))
        {
            yield return StartCoroutine(GetPlaceDetails(startPlaceId, true));
        }
        if (!destinationCoordinates.HasValue && !string.IsNullOrEmpty(destinationPlaceId))
        {
            yield return StartCoroutine(GetPlaceDetails(destinationPlaceId, false));
        }

        // Now load the scene
        LoadARScene();
    }

    private IEnumerator GetPlaceDetails(string placeId, bool isStartLocation)
    {
        string url = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={placeId}&fields=geometry&key={googleApiKey}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<GoogleDetailsResponse>(request.downloadHandler.text);
                if (response.result != null)
                {
                    double lat = response.result.geometry.location.lat;
                    double lng = response.result.geometry.location.lng;

                    if (isStartLocation)
                    {
                        startCoordinates = new Vector2d(lat, lng);
                    }
                    else
                    {
                        destinationCoordinates = new Vector2d(lat, lng);
                    }
                }
            }
            else
            {
                Debug.LogError("Google Place Details API Error: " + request.error);
            }
        }
    }
}
// Add these classes to the bottom of your SearchManager.cs file

[System.Serializable]
public class GoogleNearbySearchResponseDetailed
{
    public NearbyPlaceResult[] results;
    public string status;
}

[System.Serializable]
public class NearbyPlaceResult
{
    public string name;
    public string place_id;
    public double rating;
    public NearbyGeometry geometry;
    public PhotoInfo[] photos;
}

[System.Serializable]
public class NearbyGeometry
{
    public NearbyLocation location;
}

[System.Serializable]
public class NearbyLocation
{
    public double lat;
    public double lng;
}
// ---- GOOGLE API HELPER CLASSES (Add to the bottom of SearchManager.cs) ----

[System.Serializable]
public class GoogleFindPlaceResponse
{
    public PlaceCandidate[] candidates;
    public string status;
}

[System.Serializable]
public class PlaceCandidate
{
    public string name;
    public string place_id;
    public double rating;
}

// For the more detailed "Place Details" response
[System.Serializable]
public class GooglePlaceDetailsResponse
{
    public PlaceDetailsResult result;
    public string status;
}

[System.Serializable]
public class PlaceDetailsResult
{
    public string name;
    public double rating;
    public PhotoInfo[] photos;
}

[System.Serializable]
public class PhotoInfo
{
    public string photo_reference; // The key to downloading the image
}