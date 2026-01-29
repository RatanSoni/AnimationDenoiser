# AnimationDenoiser

A lightweight Unity Editor tool for smoothing noisy animation curves. Perfect for cleaning up motion capture data, procedural animations, or any animation with unwanted high-frequency noise.

![Unity](https://img.shields.io/badge/Unity-2019.4%2B-black?logo=unity)
![License](https://img.shields.io/badge/License-MIT-green)

---

## ✨ Features

- **Spline-based smoothing** — Removes noise while preserving the original motion's character
- **Amplitude control** — Exaggerate or reduce motion intensity after smoothing
- **Time range selection** — Smooth specific portions of your animation
- **Curve selection** — Choose which curves to process (position, rotation, scale, or individual axes)
- **Real-time preview** — See changes before applying
- **Non-destructive** — Undo support and preview cancellation

---

## 📦 Installation

### Option 1: Manual Installation

1. Download the latest release or clone this repository
2. Copy the `Editor` folder into your Unity project's `Assets` folder
   ```
   YourProject/
   └── Assets/
       └── Editor/
           └── AnimationDenoiserWindow.cs
   ```
3. Unity will auto-compile. Done!

### Option 2: Unity Package Manager (Git URL)

1. Open **Window** → **Package Manager**
2. Click **+** → **Add package from git URL**
3. Enter: `https://github.com/RatanSoni/AnimationDenoiser.git`
4. Click **Add**

---

## 🚀 Quick Start

### Opening the Tool

Navigate to **Window** → **Animation** → **Animation Smoother**

### Basic Usage

1. **Load your clip** — Drag an AnimationClip into the Clip field
2. **Select curves** — Choose which curves to smooth (defaults to all)
3. **Set time range** *(optional)* — Drag on the preview to select a specific range
4. **Adjust parameters:**
   - **Control Points:** Fewer = smoother (try 4-8)
   - **Curve Tension:** 0 = flowing curves, 1 = linear
   - **Amplitude:** Scale the motion intensity
5. **Preview** — Toggle preview to see changes in real-time
6. **Apply** — Click Apply to save changes

---

## ⚙️ Parameters

| Parameter | Description | Recommended |
|-----------|-------------|-------------|
| **Control Points** | Number of points used to fit the smooth curve. Fewer points = smoother result. | 4-10 |
| **Curve Tension** | Controls curve flow. 0 = smooth flowing curves, 1 = straight lines between points. | 0-0.3 |
| **Amplitude** | Scales peaks and valleys. <1 reduces motion, >1 exaggerates motion. | 0.5-2.0 |
| **Preserve Endpoints** | Keeps first/last keyframes unchanged to maintain animation start/end poses. | Enabled |

---

## 📖 How It Works

The tool uses a **Catmull-Rom spline fitting** approach:

1. **Sample** — Extracts control points along the curve using local averaging to capture the trend
2. **Fit** — Creates a smooth spline that passes through the control points
3. **Scale** — Applies amplitude adjustment relative to a linear baseline
4. **Rebuild** — Recalculates keyframe values and tangents for smooth playback

This approach removes high-frequency noise while preserving the overall shape and timing of the animation.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Made with ❤️ for the Unity community**
