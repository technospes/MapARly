using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;
using Unity.Sentis;
using UnityEngine.XR.ARSubsystems;
using TMPro;
using UnityEngine.UI;
using System.Linq;

public class ObjectDetector : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARCameraManager _cameraManager;

    [Header("UI & Prefabs")]
    [SerializeField] private GameObject _boundingBoxPrefab;
    [SerializeField] private Transform _detectionUiParent;

    [Header("AI Model")]
    [SerializeField] private ModelAsset _modelAsset;
    [SerializeField] private TextAsset _classNamesAsset;

    [Header("Performance Settings")]
    [SerializeField] private int _frameSkip = 5;
    [SerializeField] private int _processingResolution = 416;
    [SerializeField] private float _confidenceThreshold = 0.3f;
    [SerializeField] private float _nmsThreshold = 0.45f;

    [Header("Filter Settings")]
    [SerializeField] private bool _filterImportantOnly = true;
    [SerializeField]
    private string[] _importantClasses = new string[]
    {
        "person", "car", "motorbike", "bicycle", "truck", "bus",
        "dog", "cat", "stop sign", "traffic light", "chair", "bottle"
    };

    // Sentis
    private Model _runtimeModel;
    private Worker _worker;
    private List<string> _classNames;

    // Pooling
    private List<GameObject> _boxPool = new List<GameObject>(); // Using GameObject for simplicity with prefab
    private HashSet<string> _importantClassesSet; // Added for filtering
    private int _frameCounter = 0;
    private Texture2D _processingTexture;

    // Color coding per class
    private Dictionary<string, Color> _classColors;
    private readonly Color[] _colorPalette = new Color[]
    {
        new Color(1f, 0.2f, 0.2f), new Color(0.2f, 0.8f, 0.2f), new Color(0.2f, 0.5f, 1f),
        new Color(1f, 0.8f, 0.2f), new Color(1f, 0.5f, 0f), new Color(0.8f, 0.3f, 1f),
        new Color(0.2f, 1f, 1f), new Color(1f, 0.4f, 0.7f)
    };

    // Performance optimization
    private bool _isProcessing = false;

    void Start()
    {
        if (_modelAsset == null || _classNamesAsset == null)
        {
            Debug.LogError("[ObjectDetector] Model Asset or Class Names Asset is not assigned!");
            this.enabled = false;
            return;
        }

        _runtimeModel = ModelLoader.Load(_modelAsset);
        _worker = new Worker(_runtimeModel, BackendType.GPUCompute);
        _classNames = new List<string>(_classNamesAsset.text.Split('\n'));
        _classNames.RemoveAll(string.IsNullOrWhiteSpace);
        _importantClassesSet = new HashSet<string>(_importantClasses); // Initialize the HashSet

        InitializeColorMapping();

        for (int i = 0; i < 15; i++)
        {
            GameObject box = Instantiate(_boundingBoxPrefab, _detectionUiParent);
            // --- ADD: Ensure BoundingBox script exists ---
            if (box.GetComponent<BoundingBox>() == null)
            {
                Debug.LogWarning($"[ObjectDetector] BoundingBoxPrefab is missing the BoundingBox script! Adding it.");
                box.AddComponent<BoundingBox>(); // Add it if missing
            }
            box.SetActive(false);
            _boxPool.Add(box);
        }

        _processingTexture = new Texture2D(_processingResolution, _processingResolution, TextureFormat.RGB24, false);
        Debug.Log($"[ObjectDetector] Initialized - {_classNames.Count} classes, Mobile Optimized");
    }

    void InitializeColorMapping()
    {
        _classColors = new Dictionary<string, Color>();
        // ... (your existing color mapping code) ...
        _classColors["person"] = new Color(1f, 0.2f, 0.2f);
        _classColors["car"] = new Color(0.2f, 0.8f, 0.2f);
        _classColors["truck"] = new Color(0.2f, 0.8f, 0.2f);
        _classColors["bus"] = new Color(0.2f, 0.8f, 0.2f);
        _classColors["motorbike"] = new Color(0.2f, 0.5f, 1f);
        _classColors["bicycle"] = new Color(0.2f, 0.5f, 1f);
        _classColors["dog"] = new Color(1f, 0.8f, 0.2f);
        _classColors["cat"] = new Color(1f, 0.8f, 0.2f);
        _classColors["stop sign"] = new Color(1f, 0f, 0f);
        _classColors["traffic light"] = new Color(1f, 0.8f, 0.2f);

        for (int i = 0; i < _classNames.Count; i++)
        {
            string className = _classNames[i].Trim();
            if (!_classColors.ContainsKey(className))
            {
                _classColors[className] = _colorPalette[i % _colorPalette.Length];
            }
        }
    }

    void OnDestroy()
    {
        _worker?.Dispose();
        if (_processingTexture != null) Destroy(_processingTexture);
    }

    void Update()
    {
        _frameCounter++;
        if (_frameCounter % _frameSkip != 0) return;
        if (_isProcessing) return;

        if (_cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            // --- DEBUG LOG ---
            // Debug.Log("[ObjectDetector] Acquired new camera image. Starting detection.");
            StartCoroutine(DetectObjects(image));
            image.Dispose();
        }
        else
        {
            // --- DEBUG LOG ---
            Debug.LogWarning("[ObjectDetector] Failed to acquire camera image this frame.");
        }
    }

    private IEnumerator DetectObjects(XRCpuImage cpuImage)
    {
        _isProcessing = true;
        // --- DEBUG LOG ---
        // Debug.Log("[ObjectDetector] Coroutine started. Converting image...");

        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(_processingResolution, _processingResolution),
            outputFormat = TextureFormat.RGB24,
            transformation = XRCpuImage.Transformation.MirrorY // Check if MirrorY is correct for your setup
        };
        var rawData = _processingTexture.GetRawTextureData<byte>();
        cpuImage.Convert(conversionParams, rawData);
        _processingTexture.Apply();

        var inputShape = new TensorShape(1, 3, _processingResolution, _processingResolution);
        using var inputTensor = new Tensor<float>(inputShape);
        TextureConverter.ToTensor(_processingTexture, inputTensor, new TextureTransform());

        // --- DEBUG LOG ---
        // Debug.Log("[ObjectDetector] Executing model...");
        _worker.Schedule(inputTensor);
        yield return new WaitForEndOfFrame(); // Wait for GPU

        var outputTensor = _worker.PeekOutput() as Tensor<float>;
        if (outputTensor == null)
        {
            Debug.LogError("[ObjectDetector] Failed to get output tensor!");
            _isProcessing = false;
            yield break;
        }

        // --- DEBUG LOG ---
        Debug.Log($"[ObjectDetector] Got output tensor with shape: {outputTensor.shape}");
        var outputData = outputTensor.ReadbackAndClone();

        var detections = ParseYoloOutput(outputData);
        outputData.Dispose(); // Dispose after cloning

        // --- DEBUG LOG ---
        Debug.Log($"[ObjectDetector] Parsed {detections.Count} potential detections.");
        // Check value range
        if (detections.Count > 0)
        {
            Debug.Log($"Sample det: X={detections[0].X}, Y={detections[0].Y}, W={detections[0].Width}, H={detections[0].Height}");
        }

        detections = ApplyNMS(detections, _nmsThreshold);
        // --- DEBUG LOG ---
        Debug.Log($"[ObjectDetector] {detections.Count} detections remain after NMS.");

        if (_filterImportantOnly)
        {
            int beforeFilterCount = detections.Count;
            // --- Ensure _importantClassesSet is initialized ---
            if (_importantClassesSet == null) _importantClassesSet = new HashSet<string>(_importantClasses);

            detections = detections.Where(d => _importantClassesSet.Contains(d.Label)).ToList();
            // --- DEBUG LOG ---
            Debug.Log($"[ObjectDetector] {detections.Count} detections remain after filtering (removed {beforeFilterCount - detections.Count}).");
        }

        UpdateBoundingBoxes(detections);

        _isProcessing = false;
    }

    private List<YoloDetection> ParseYoloOutput(Tensor<float> outputTensor)
    {
        var detections = new List<YoloDetection>();
        int numDetections = outputTensor.shape[1];
        int numClasses = Mathf.Min(_classNames.Count, outputTensor.shape[2] - 5);

        for (int i = 0; i < numDetections; i++)
        {
            float objectness = outputTensor[0, i, 4];
            if (objectness < _confidenceThreshold) continue;

            float maxClassScore = 0;
            int classId = -1;
            for (int j = 0; j < numClasses; j++)
            {
                float classScore = outputTensor[0, i, 5 + j];
                if (classScore > maxClassScore)
                {
                    maxClassScore = classScore;
                    classId = j;
                }
            }

            float finalConfidence = objectness * maxClassScore;
            if (finalConfidence < _confidenceThreshold || classId == -1 || classId >= _classNames.Count) continue;

            detections.Add(new YoloDetection
            {
                X = outputTensor[0, i, 0],
                Y = outputTensor[0, i, 1],
                Width = outputTensor[0, i, 2],
                Height = outputTensor[0, i, 3],
                Confidence = finalConfidence,
                ClassIndex = classId,
                Label = _classNames[classId].Trim()
            });
        }
        return detections;
    }

    private List<YoloDetection> ApplyNMS(List<YoloDetection> detections, float iouThreshold)
    {
        detections = detections.OrderByDescending(d => d.Confidence).ToList();
        var results = new List<YoloDetection>();
        var suppressed = new bool[detections.Count];

        for (int i = 0; i < detections.Count; i++)
        {
            if (suppressed[i]) continue;
            results.Add(detections[i]);
            Rect rectA = GetRectFromYolo(detections[i]); // Helper needed

            for (int j = i + 1; j < detections.Count; j++)
            {
                if (suppressed[j]) continue;
                if (detections[i].ClassIndex == detections[j].ClassIndex)
                {
                    Rect rectB = GetRectFromYolo(detections[j]); // Helper needed
                    if (CalculateIoU(rectA, rectB) > iouThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }
        }
        return results;
    }

    // --- ADD THIS HELPER ---
    private Rect GetRectFromYolo(YoloDetection det)
    {
        // Converts center x,y,w,h (normalized 0-1) to Rect format
        return new Rect(det.X - det.Width / 2, det.Y - det.Height / 2, det.Width, det.Height);
    }

    private float CalculateIoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin);
        float y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax);
        float y2 = Mathf.Min(a.yMax, b.yMax);
        float intersection = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        float union = a.width * a.height + b.width * b.height - intersection;
        return union > 0 ? intersection / union : 0;
    }

    private void UpdateBoundingBoxes(List<YoloDetection> detections)
    {
        // --- DEBUG LOG ---
        Debug.Log($"[ObjectDetector] Updating UI with {detections.Count} final detections.");

        int boxIndex = 0;
        foreach (var det in detections)
        {
            if (boxIndex >= _boxPool.Count)
            {
                // --- DEBUG LOG ---
                Debug.LogWarning("[ObjectDetector] Box pool exhausted. Cannot display more detections.");
                break;
            }

            GameObject boxGO = _boxPool[boxIndex];
            RectTransform rectTransform = boxGO.GetComponent<RectTransform>();
            BoundingBox boxScript = boxGO.GetComponent<BoundingBox>(); // Get the script

            if (rectTransform == null || boxScript == null)
            {
                Debug.LogError($"[ObjectDetector] BoundingBoxPrefab {boxIndex} is missing RectTransform or BoundingBox script!");
                continue; // Skip this box
            }

            // Scale to screen coordinates
            Rect yoloRect = GetRectFromYolo(det);
            float x = yoloRect.x * Screen.width;
            float y = yoloRect.y * Screen.height;
            float w = yoloRect.width * Screen.width;
            float h = yoloRect.height * Screen.height;

            rectTransform.anchoredPosition = new Vector2(x, Screen.height - y - h); // Y is inverted for UI
            rectTransform.sizeDelta = new Vector2(w, h);

            Color boxColor = _classColors.ContainsKey(det.Label) ? _classColors[det.Label] : _colorPalette[det.ClassIndex % _colorPalette.Length];

            // --- DEBUG LOG ---
            // Debug.Log($"[ObjectDetector] Showing box {boxIndex}: {det.Label} ({det.Confidence:P0}) at {rectTransform.anchoredPosition}");

            // Use the BoundingBox script to show/fade
            boxScript.Show($"{det.Label} {det.Confidence:P0}", boxColor);
            boxIndex++;
        }

        // Deactivate remaining boxes in the pool
        for (int i = boxIndex; i < _boxPool.Count; i++)
        {
            _boxPool[i].SetActive(false);
        }
    }
}

// Ensure you also have the YoloDetection struct and the BoundingBox.cs script in your project