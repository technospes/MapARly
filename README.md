#  MapARly: Geospatial AR Navigation

MapARly is an immersive Augmented Reality (AR) navigation application that bridges the gap between digital maps and the physical world. It leverages real-time geospatial data and computer vision to deliver intuitive, heads-up pathfinding and environmental awareness on mobile devices.

---

## Table of Contents

- [Demo & Screenshots](#demo--screenshots)
- [Key Features](#key-features)
- [Tech Stack](#tech-stack)
- [System Architecture](#system-architecture)
- [Installation & Setup](#installation--setup)
- [How to Run](#how-to-run)
- [Usage Guide](#usage-guide)
- [Project Folder Structure](#project-folder-structure)
- [Configuration Details](#configuration-details)
- [Performance & Optimization](#performance--optimization)
- [Known Limitations](#known-limitations)
- [Future Scope / Roadmap](#future-scope--roadmap)
- [Contribution Guidelines](#contribution-guidelines)
- [License](#license)
- [Contact](#contact)

---

## Demo & Screenshots

> ....In_Progress

Example views:
- **AR Pathfinding** – Ground‑anchored 3D arrows and paths.  
- **Real-time Object Detection** – On‑device detection of navigation‑relevant objects.  
- **POI Interaction** – Tappable 3D icons with rich information panels.

---

## Key Features

- **Immersive AR Navigation**  
  Renders high‑fidelity 3D pathways and directional arrows directly in the user’s field of view.

- **Deep Mapbox Integration**  
  Uses dynamic vector tiles, routing APIs, and elevation data for accurate global mapping and terrain awareness.

- **AI-Powered Object Detection**  
  Integrates a YOLOv5n model via ONNX Runtime to detect obstacles and context objects in real time.

- **Geospatial Points of Interest (POI)**  
  Dynamically fetches and anchors POIs in AR with interactive panels for details such as distance and business hours.

- **Voice Guidance**  
  Google Cloud Text‑to‑Speech provides hands‑free, turn‑by‑turn audio instructions.

- **Smart Compass Alignment**  
  Synchronizes digital map orientation with real‑world heading using device sensors and ARCore pose data.

- **Efficient Resource Management**  
  Uses object pooling for AR markers and selective loading/unloading of map tiles to keep memory and draw calls under control.

---

## Tech Stack

**Core Engine & Frameworks**

- Unity 2022.3+ (LTS)  
- ARCore / ARFoundation (tracking, plane detection, anchors)  
- Mapbox SDK for Unity (geospatial data, routing, geocoding)

**Intelligence & Services**

- ONNX Runtime – Inference for YOLOv5n (Nano) object detection  
- Google Cloud Text‑to‑Speech – Synthetic voice guidance  
- C# (.NET 4.x / Standard 2.1) – Core gameplay and integration logic

**Data & Infrastructure**

- Mapbox APIs – Directions, Matrix, and Vector Tile services  
- SQLite – Local caching of tiles and user preferences for offline resilience

---

## System Architecture

MapARly follows a modular, mobile‑optimized architecture:

- **Geospatial Layer**  
  Mapbox handles coordinate conversion (WGS84 → Unity world space) and asynchronously fetches vector data and routes.

- **Navigation Engine**  
  Converts route geometry into a spline‑based 3D path and spawns AR markers from an object pool for efficient rendering.

- **Vision Pipeline**  
  Captures camera frames and feeds them to the ONNX model; detection results are re‑projected into world space and surfaced as contextual alerts.

- **Localization Module**  
  Fuses GPS with ARCore’s visual–inertial odometry (VIO) to maintain stable, world‑scale alignment between digital content and the real environment.

---

## Installation & Setup

### Prerequisites

- Unity Hub + Unity 2022.3+ (LTS)  
- Mapbox access token (https://www.mapbox.com)  
- Android SDK/NDK (for Android builds) or Xcode (for iOS builds)

### Step-by-Step Setup

1. **Clone the repository**
```
git clone https://github.com/technospes/ar_navigation_app_with_unity.git
```

2. **Open the project**

- Launch Unity Hub.  
- Add the cloned folder and open the project in Unity.

3. **Configure Mapbox**

- In the Unity Editor, go to `Mapbox > Setup`.  
- Paste your Mapbox Access Token and click **Submit**.

4. **Install dependencies**

- Wait for the External Dependency Manager (EDM4U) to resolve Android/iOS libraries.  

5. **Build settings**

- Go to `File > Build Settings`.  
- Switch platform to **Android** or **iOS**.  
- Ensure the main AR scene (e.g., `ARScene`) is included in **Scenes in Build**.

---

## How to Run

- **Editor / Unity Remote (quick testing)**  
Use Unity Remote on a connected device to validate UI flow and basic AR behavior (performance is limited).

- **Device build (recommended)**  
1. Connect your phone via USB.  
2. Click **Build and Run** in Unity.  
3. On first launch, grant Camera and Location permissions.  
4. Walk a few meters to let ARCore initialize and allow the localization module to converge.

---

## Usage Guide

- **Search** – Use the search bar to query destinations; Mapbox Geocoding returns candidate locations.  
- **Start Navigation** – Tap a destination to compute a route; 3D arrows will appear along the path.  
- **Object Identification** – Point the camera at streets or buildings to see on‑device detection overlays.  
- **POI Interaction** – Tap floating POI icons to view name, distance, and additional metadata.

---

## Project Folder Structure
```
Assets/
├── AI_Models/ # YOLOv5n ONNX model and COCO label files
├── Mapbox/ # Mapbox SDK core, materials, and prefabs
├── Plugins/ # Native Android/iOS plugins and third-party libs
├── Prefabs/ # AR markers, UI panels, path elements
├── Scenes/ # Main AR scene, intro, splash
├── Scripts/ # Core C# logic
│ ├── ObjectDetector.cs # ML inference integration
│ ├── RouteManager.cs # Pathfinding and waypoint management
│ ├── SearchManager.cs # Geocoding and search UX
│ └── GoogleTTSManager.cs # Voice guidance orchestration
└── UI Assets/ # Sprites, icons, and fonts

```
---

## Configuration Details

- **Inference** – `YoloDetection.cs` exposes confidence and IoU thresholds (default confidence: 0.5) for tuning detection sensitivity and NMS behavior.  
- **Caching** – `CacheController.cs` enables SQLite-based caching of tiles and POI results to reduce API usage and improve load times.  
- **Build** – For production, disable **Development Build** and **Script Debugging** in Build Settings to maximize FPS and reduce app size.

---

## Performance & Optimization

- **GPU-Accelerated Inference**  
  ONNX models run via the Barracuda worker where supported, offloading heavy compute from the CPU.

- **Draw Call Batching**  
  Map layers and AR elements are merged using a `MergedModifierStack` and prefab batching to minimize draw calls.

- **Frustum Culling**  
  POIs and map tiles outside the camera frustum are deactivated to save rendering and battery.

---

## Known Limitations

- **Indoor Navigation**  
  Accuracy drops indoors due to GPS drift; robust indoor use would require VPS or manual anchors.

- **Battery Consumption**  
  Continuous use of camera, GPU, and GPS leads to high power draw on mobile devices.

- **Lighting Conditions**  
  ARCore tracking is less stable in low‑light or low‑texture environments.

---

## Future Scope / Roadmap

- [ ] Multi-stop routing and waypoint management.  
- [ ] Dynamic occlusion so AR paths correctly pass “behind” real buildings.  
- [ ] Social features for sharing AR pins and routes.  
- [ ] Custom object detection models for local signage and city‑specific landmarks.

---

## Contribution Guidelines

Contributions are welcome.

1. Fork the repository.  
2. Create a feature branch:
```
git checkout -b feature/NewFeature
```

3. Commit your changes:
```
git commit -m "Add NewFeature"
```

4. Push the branch:
```
git push origin feature/NewFeature
```

5. Open a Pull Request describing your changes and testing steps.

---

## License

Distributed under the MIT License. See `LICENSE` for details.

---

## Contact

**Project Maintainer:** Technospes  
**Project Link:** https://github.com/technospes/ar_navigation_app_with_unity

