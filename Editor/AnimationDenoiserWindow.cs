using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

public class AnimationDenoiserWindow : EditorWindow
{
    // Smoothing Parameters
    private int controlPoints = 8;
    private float curveTension = 0.5f;
    private float amplitudeScale = 1.0f;
    private bool preserveEndpoints = true;
    private int preserveCount = 1;
    private bool keepKeysOnFrame = true;

    // State
    private bool preview = false;
    private AnimationClip selectedClip;
    private AnimationClip previewBackup;

    // Curve Selection
    private List<CurveInfo> curveInfoList = new List<CurveInfo>();
    private Vector2 curveScrollPos;
    private int curveListHeight = 200;
    private string searchFilter = "";
    private Dictionary<string, bool> groupFoldouts = new Dictionary<string, bool>();

    // Preview Display
    private bool showCurvePreview = true;
    private int curvePreviewHeight = 200;
    private Texture2D curvePreviewTexture;
    private bool needsPreviewUpdate = true;
    private int lastSelectedCount = 0;

    // Time Selection
    private float selectionStartTime = -1f;
    private float selectionEndTime = -1f;
    private bool hasTimeSelection = false;

    // UI
    private Vector2 scrollPos;
    private GUIStyle headerStyle;
    private bool stylesInitialized = false;

    // Cache for Optimization
    private float[] splineCache;
    private int splineCacheSize;

    private class CurveInfo
    {
        public EditorCurveBinding binding;
        public bool selected;
        public string displayName;
        public string groupName;

        public CurveInfo(EditorCurveBinding b)
        {
            binding = b;
            selected = true;
            displayName = b.propertyName
                .Replace("m_LocalPosition", "Position")
                .Replace("m_LocalRotation", "Rotation")
                .Replace("m_LocalScale", "Scale")
                .Replace("localEulerAnglesRaw", "Rotation (Euler)");
            groupName = string.IsNullOrEmpty(b.path) ? "Root" : b.path;
        }
    }

    [MenuItem("Window/Animation/Animation Denoiser")]
    public static void ShowWindow()
    {
        var w = GetWindow<AnimationDenoiserWindow>("Animation Denoiser");
        w.minSize = new Vector2(450, 550);
    }

    private void OnEnable() => needsPreviewUpdate = true;

    private void OnDisable()
    {
        if (curvePreviewTexture) { DestroyImmediate(curvePreviewTexture); curvePreviewTexture = null; }
    }

    private void OnDestroy()
    {
        if (previewBackup) RestorePreview();
        OnDisable();
    }

    private void InitStyles()
    {
        if (stylesInitialized) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        stylesInitialized = true;
    }

    private void RefreshCurveList()
    {
        curveInfoList.Clear();
        groupFoldouts.Clear();
        if (!selectedClip) return;

        var bindings = AnimationUtility.GetCurveBindings(selectedClip);
        curveInfoList.Capacity = bindings.Length;

        foreach (var b in bindings)
        {
            var info = new CurveInfo(b);
            curveInfoList.Add(info);
            if (!groupFoldouts.ContainsKey(info.groupName))
                groupFoldouts[info.groupName] = true;
        }

        curveInfoList = curveInfoList.OrderBy(c => c.groupName).ThenBy(c => c.displayName).ToList();
        needsPreviewUpdate = true;
        ClearTimeSelection();
    }

    private void ClearTimeSelection()
    {
        hasTimeSelection = false;
        selectionStartTime = selectionEndTime = -1f;
        needsPreviewUpdate = true;
    }

    private void GetTimeRange(out float start, out float end)
    {
        if (hasTimeSelection)
        {
            start = Mathf.Min(selectionStartTime, selectionEndTime);
            end = Mathf.Max(selectionStartTime, selectionEndTime);
        }
        else
        {
            start = 0;
            end = selectedClip ? selectedClip.length : 1f;
        }
    }

    private List<EditorCurveBinding> GetSelectedBindings()
    {
        return curveInfoList.Where(c => c.selected).Select(c => c.binding).ToList();
    }

    private void OnGUI()
    {
        InitStyles();
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        EditorGUILayout.Space(10);

        DrawClipSection();
        DrawSeparator();
        DrawCurveSelectionSection();
        DrawSeparator();
        DrawSmoothingSettings();
        DrawSeparator();
        DrawButtons();

        EditorGUILayout.EndScrollView();
    }

    private void DrawClipSection()
    {
        EditorGUILayout.LabelField("Animation Clip", headerStyle);
        EditorGUI.BeginChangeCheck();
        selectedClip = (AnimationClip)EditorGUILayout.ObjectField("Clip", selectedClip, typeof(AnimationClip), false);
        if (EditorGUI.EndChangeCheck())
        {
            if (preview) { RestorePreview(); preview = false; }
            RefreshCurveList();
        }

        if (selectedClip)
        {
            EditorGUILayout.LabelField($"Duration: {selectedClip.length:F2}s  |  FPS: {selectedClip.frameRate}  |  Curves: {curveInfoList.Count}", EditorStyles.miniLabel);
            if (GUILayout.Button("Refresh Curves", GUILayout.Width(100))) RefreshCurveList();
        }
        EditorGUILayout.Space(10);
    }

    private void DrawCurveSelectionSection()
    {
        EditorGUILayout.LabelField("Curve Selection", headerStyle);
        if (!selectedClip || curveInfoList.Count == 0) return;

        // Search & Quick Select Buttons
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
        searchFilter = EditorGUILayout.TextField(searchFilter);
        if (GUILayout.Button("All", GUILayout.Width(35))) SetAllSelected(true);
        if (GUILayout.Button("None", GUILayout.Width(40))) SetAllSelected(false);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Position", GUILayout.Width(60))) SelectByType("Position");
        if (GUILayout.Button("Rotation", GUILayout.Width(60))) SelectByType("Rotation");
        if (GUILayout.Button("Scale", GUILayout.Width(50))) SelectByType("Scale");
        GUILayout.Space(10);
        if (GUILayout.Button("X", GUILayout.Width(25))) SelectByAxis(".x");
        if (GUILayout.Button("Y", GUILayout.Width(25))) SelectByAxis(".y");
        if (GUILayout.Button("Z", GUILayout.Width(25))) SelectByAxis(".z");
        if (GUILayout.Button("W", GUILayout.Width(25))) SelectByAxis(".w");
        EditorGUILayout.EndHorizontal();

        // Curve List
        curveListHeight = Mathf.RoundToInt(EditorGUILayout.Slider("Height", curveListHeight, 100, 400));

        curveScrollPos = EditorGUILayout.BeginScrollView(curveScrollPos, GUILayout.Height(curveListHeight));
        var filtered = string.IsNullOrEmpty(searchFilter) ? curveInfoList
            : curveInfoList.Where(c => c.displayName.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || c.groupName.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        foreach (var group in filtered.GroupBy(c => c.groupName))
        {
            string key = group.Key;
            groupFoldouts.TryAdd(key, true);

            EditorGUILayout.BeginHorizontal();
            groupFoldouts[key] = EditorGUILayout.Foldout(groupFoldouts[key], key, true);
            bool grpSel = group.All(c => c.selected);
            if (EditorGUILayout.Toggle(grpSel, GUILayout.Width(20)) != grpSel)
            {
                foreach (var c in group) c.selected = !grpSel;
                needsPreviewUpdate = true;
            }
            EditorGUILayout.EndHorizontal();
            
            if (!groupFoldouts[key])
            {
                EditorGUI.indentLevel++;
                foreach (var ci in group)
                {
                    EditorGUI.BeginChangeCheck();
                    ci.selected = EditorGUILayout.ToggleLeft(ci.displayName, ci.selected);
                    if (EditorGUI.EndChangeCheck()) needsPreviewUpdate = true;
                }
                EditorGUI.indentLevel--;
            }
        }
        EditorGUILayout.EndScrollView();

        int selCount = curveInfoList.Count(c => c.selected);
        EditorGUILayout.LabelField($"Selected: {selCount} / {curveInfoList.Count}", EditorStyles.centeredGreyMiniLabel);
        if (selCount != lastSelectedCount) { lastSelectedCount = selCount; needsPreviewUpdate = true; }

        // Curve Preview
        EditorGUILayout.Space(8);
        DrawCurvePreview(selCount);
    }

    private void DrawCurvePreview(int selCount)
    {
        EditorGUILayout.BeginHorizontal();
        showCurvePreview = EditorGUILayout.Foldout(showCurvePreview, "Curve Preview", true);
        if (GUILayout.Button("Refresh", GUILayout.Width(55))) needsPreviewUpdate = true;
        EditorGUILayout.EndHorizontal();

        if (!showCurvePreview || selCount == 0) return;

        curvePreviewHeight = Mathf.RoundToInt(EditorGUILayout.Slider("Height", curvePreviewHeight, 100, 400));

        // Time Selection UI
        EditorGUILayout.BeginHorizontal();
        if (hasTimeSelection)
        {
            float dispStart = Mathf.Min(selectionStartTime, selectionEndTime);
            float dispEnd = Mathf.Max(selectionStartTime, selectionEndTime);
            EditorGUILayout.LabelField($"Range: {dispStart:F2}s - {dispEnd:F2}s", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear", GUILayout.Width(50))) ClearTimeSelection();
        }
        else
        {
            EditorGUILayout.LabelField("Drag to select range (or smooths entire clip)", EditorStyles.miniLabel);
        }
        if (GUILayout.Button("Select All", GUILayout.Width(70)))
        {
            selectionStartTime = 0;
            selectionEndTime = selectedClip.length;
            hasTimeSelection = true;
            needsPreviewUpdate = true;
        }
        EditorGUILayout.EndHorizontal();

        // Manual Time Input
        EditorGUILayout.BeginHorizontal();
        float manualStart = hasTimeSelection ? Mathf.Min(selectionStartTime, selectionEndTime) : 0;
        float manualEnd = hasTimeSelection ? Mathf.Max(selectionStartTime, selectionEndTime) : selectedClip.length;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("From:", GUILayout.Width(35));
        manualStart = EditorGUILayout.FloatField(manualStart, GUILayout.Width(50));
        EditorGUILayout.LabelField("To:", GUILayout.Width(22));
        manualEnd = EditorGUILayout.FloatField(manualEnd, GUILayout.Width(50));
        EditorGUILayout.LabelField("sec", GUILayout.Width(25));
        if (EditorGUI.EndChangeCheck())
        {
            selectionStartTime = Mathf.Clamp(manualStart, 0, selectedClip.length);
            selectionEndTime = Mathf.Clamp(manualEnd, 0, selectedClip.length);
            hasTimeSelection = true;
            needsPreviewUpdate = true;
        }
        EditorGUILayout.EndHorizontal();

        // Preview Rect
        Rect previewRect = GUILayoutUtility.GetRect(100, curvePreviewHeight);
        previewRect = EditorGUI.IndentedRect(previewRect);
        HandlePreviewMouse(previewRect);

        // Draw Texture
        int texWidth = (int)previewRect.width;
        if (needsPreviewUpdate || !curvePreviewTexture || curvePreviewTexture.width != texWidth || curvePreviewTexture.height != curvePreviewHeight)
        {
            UpdateCurvePreviewTexture(texWidth, curvePreviewHeight);
            needsPreviewUpdate = false;
        }

        if (curvePreviewTexture)
        {
            GUI.DrawTexture(previewRect, curvePreviewTexture);
            EditorGUIUtility.AddCursorRect(previewRect, MouseCursor.SlideArrow);
        }

        // Selection Overlay
        if (hasTimeSelection && selectedClip.length > 0)
        {
            float startNorm = Mathf.Min(selectionStartTime, selectionEndTime) / selectedClip.length;
            float endNorm = Mathf.Max(selectionStartTime, selectionEndTime) / selectedClip.length;
            float startX = previewRect.xMin + startNorm * previewRect.width;
            float endX = previewRect.xMin + endNorm * previewRect.width;

            EditorGUI.DrawRect(new Rect(previewRect.xMin, previewRect.yMin, startX - previewRect.xMin, previewRect.height), new Color(0, 0, 0, 0.5f));
            EditorGUI.DrawRect(new Rect(endX, previewRect.yMin, previewRect.xMax - endX, previewRect.height), new Color(0, 0, 0, 0.5f));
            EditorGUI.DrawRect(new Rect(startX - 1, previewRect.yMin, 2, previewRect.height), Color.yellow);
            EditorGUI.DrawRect(new Rect(endX - 1, previewRect.yMin, 2, previewRect.height), Color.yellow);
        }

        // Border
        Handles.color = new Color(0.4f, 0.4f, 0.4f);
        Handles.DrawLine(new Vector3(previewRect.xMin, previewRect.yMin), new Vector3(previewRect.xMax, previewRect.yMin));
        Handles.DrawLine(new Vector3(previewRect.xMin, previewRect.yMax), new Vector3(previewRect.xMax, previewRect.yMax));
        Handles.DrawLine(new Vector3(previewRect.xMin, previewRect.yMin), new Vector3(previewRect.xMin, previewRect.yMax));
        Handles.DrawLine(new Vector3(previewRect.xMax, previewRect.yMin), new Vector3(previewRect.xMax, previewRect.yMax));

        // Time Labels
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("0s", EditorStyles.miniLabel, GUILayout.Width(20));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"{selectedClip.length:F1}s", EditorStyles.miniLabel, GUILayout.Width(35));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSmoothingSettings()
    {
        EditorGUILayout.LabelField("Smoothing Settings", headerStyle);
        EditorGUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Control Points");
        controlPoints = EditorGUILayout.IntSlider(controlPoints, 2, 30);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Fewer Points = Smoother Curve", EditorStyles.miniLabel);

        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Curve Tension");
        curveTension = EditorGUILayout.Slider(curveTension, 0f, 1f);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("0 = Very Smooth Curves, 1 = Straight Lines Between Points", EditorStyles.miniLabel);

        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Amplitude");
        amplitudeScale = EditorGUILayout.Slider(amplitudeScale, 0f, 3f);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Scale Curve Height: <1 = Flatten, 1 = Unchanged, >1 = Exaggerate", EditorStyles.miniLabel);

        EditorGUILayout.Space(5);

        preserveEndpoints = EditorGUILayout.Toggle("Preserve Endpoints", preserveEndpoints);
        if (preserveEndpoints)
        {
            EditorGUI.indentLevel++;
            preserveCount = EditorGUILayout.IntSlider("Keys to Preserve", preserveCount, 1, 10);
            EditorGUI.indentLevel--;
        }

        keepKeysOnFrame = EditorGUILayout.Toggle("Snap Keys to Frames", keepKeysOnFrame);

        EditorGUILayout.Space(8);

        // Preview Controls
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        preview = EditorGUILayout.Toggle("Preview", preview);
        if (EditorGUI.EndChangeCheck())
        {
            if (preview) ApplyPreview();
            else RestorePreview();
            needsPreviewUpdate = true;
        }

        GUI.enabled = preview;
        if (GUILayout.Button("Refresh Preview", GUILayout.Width(110)))
        {
            RefreshPreview();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
    }

    private void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("Apply", GUILayout.Height(30))) { ApplySmoothing(); needsPreviewUpdate = true; }
        GUI.backgroundColor = new Color(0.4f, 0.6f, 0.9f);
        if (GUILayout.Button("Apply & Close", GUILayout.Height(30))) { ApplySmoothing(); Close(); }
        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
        if (GUILayout.Button("Cancel", GUILayout.Height(30))) { RestorePreview(); Close(); }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        if (GUILayout.Button("Reset to Defaults")) ResetSettings();
    }

    private void DrawSeparator()
    {
        EditorGUILayout.Space(3);
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.5f, 0.5f, 0.5f));
        EditorGUILayout.Space(3);
    }

    private void HandlePreviewMouse(Rect previewRect)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && previewRect.Contains(e.mousePosition):
                GUIUtility.hotControl = controlId;
                float normX = Mathf.Clamp01((e.mousePosition.x - previewRect.xMin) / previewRect.width);
                selectionStartTime = selectionEndTime = normX * selectedClip.length;
                hasTimeSelection = true;
                needsPreviewUpdate = true;
                e.Use();
                break;

            case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                normX = Mathf.Clamp01((e.mousePosition.x - previewRect.xMin) / previewRect.width);
                selectionEndTime = normX * selectedClip.length;
                needsPreviewUpdate = true;
                e.Use();
                Repaint();
                break;

            case EventType.MouseUp when GUIUtility.hotControl == controlId:
                GUIUtility.hotControl = 0;
                if (Mathf.Abs(selectionEndTime - selectionStartTime) < 0.05f) ClearTimeSelection();
                needsPreviewUpdate = true;
                e.Use();
                break;
        }
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var c in curveInfoList) c.selected = selected;
        needsPreviewUpdate = true;
    }

    private void SelectByType(string type)
    {
        string typeLower = type.ToLowerInvariant();
        foreach (var c in curveInfoList)
            c.selected = c.displayName.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0
                || c.binding.propertyName.IndexOf(typeLower, StringComparison.OrdinalIgnoreCase) >= 0;
        needsPreviewUpdate = true;
    }

    private void SelectByAxis(string axis)
    {
        foreach (var c in curveInfoList)
            c.selected = c.binding.propertyName.EndsWith(axis, StringComparison.OrdinalIgnoreCase);
        needsPreviewUpdate = true;
    }

    private void ResetSettings()
    {
        controlPoints = 8;
        curveTension = 0.5f;
        amplitudeScale = 1.0f;
        preserveEndpoints = true;
        preserveCount = 1;
        keepKeysOnFrame = true;
        ClearTimeSelection();
    }

    private void UpdateCurvePreviewTexture(int width, int height)
    {
        if (!selectedClip || width <= 0 || height <= 0) return;

        var selectedCurves = curveInfoList.Where(c => c.selected).ToList();
        if (selectedCurves.Count == 0) return;

        if (!curvePreviewTexture || curvePreviewTexture.width != width || curvePreviewTexture.height != height)
        {
            if (curvePreviewTexture) DestroyImmediate(curvePreviewTexture);
            curvePreviewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        }

        Color32 bgColor = new Color32(38, 38, 38, 255);
        Color32 gridColor = new Color32(64, 64, 64, 255);
        Color32[] pixels = new Color32[width * height];

        // Fill Background
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bgColor;

        // Draw Grid
        for (int i = 1; i < 5; i++) { int y = height * i / 5; for (int x = 0; x < width; x++) pixels[y * width + x] = gridColor; }
        for (int i = 1; i < 10; i++) { int xi = width * i / 10; for (int y = 0; y < height; y++) pixels[y * width + xi] = gridColor; }

        // Calculate Value Range
        float maxTime = selectedClip.length;
        float globalMin = float.MaxValue, globalMax = float.MinValue;

        foreach (var ci in selectedCurves)
        {
            var curve = AnimationUtility.GetEditorCurve(selectedClip, ci.binding);
            if (curve == null) continue;
            foreach (var k in curve.keys)
            {
                if (k.value < globalMin) globalMin = k.value;
                if (k.value > globalMax) globalMax = k.value;
            }
        }

        float valRange = globalMax - globalMin;
        if (valRange < 0.001f) valRange = 1f;
        float pad = valRange * 0.1f;
        globalMin -= pad; globalMax += pad;
        valRange = globalMax - globalMin;

        // Draw Curves
        int idx = 0;
        foreach (var ci in selectedCurves)
        {
            var curve = AnimationUtility.GetEditorCurve(selectedClip, ci.binding);
            if (curve == null || curve.keys.Length < 2) { idx++; continue; }

            Color32 col = GetCurveColor32(ci, idx);
            int prevY = -1;

            for (int x = 0; x < width; x++)
            {
                float t = (float)x / width * maxTime;
                float v = curve.Evaluate(t);
                int y = Mathf.Clamp((int)((v - globalMin) / valRange * height), 0, height - 1);
                y = height - 1 - y;

                if (prevY >= 0)
                {
                    int minY = Mathf.Min(y, prevY), maxY = Mathf.Max(y, prevY);
                    for (int py = minY; py <= maxY; py++)
                    {
                        int pi = py * width + x;
                        if (pi >= 0 && pi < pixels.Length) pixels[pi] = col;
                    }
                }
                prevY = y;
            }

            // Draw Keyframe Dots
            Color32 white = new Color32(255, 255, 255, 255);
            foreach (var key in curve.keys)
            {
                int kx = Mathf.Clamp((int)(key.time / maxTime * width), 0, width - 1);
                int ky = Mathf.Clamp((int)((key.value - globalMin) / valRange * height), 0, height - 1);
                ky = height - 1 - ky;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int px = kx + dx, py = ky + dy;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                            pixels[py * width + px] = white;
                    }
            }
            idx++;
        }

        curvePreviewTexture.SetPixels32(pixels);
        curvePreviewTexture.Apply();
    }

    private Color32 GetCurveColor32(CurveInfo ci, int index)
    {
        string p = ci.binding.propertyName;
        if (p.EndsWith(".x") || p.EndsWith("x")) return new Color32(255, 77, 77, 255);
        if (p.EndsWith(".y") || p.EndsWith("y")) return new Color32(77, 255, 77, 255);
        if (p.EndsWith(".z") || p.EndsWith("z")) return new Color32(77, 128, 255, 255);
        if (p.EndsWith(".w") || p.EndsWith("w")) return new Color32(255, 179, 77, 255);
        Color c = Color.HSVToRGB((index * 0.618f) % 1f, 0.7f, 0.9f);
        return new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);
    }

    #region Smoothing

    private AnimationCurve SmoothCurve(AnimationCurve originalCurve, float timeStart, float timeEnd)
    {
        Keyframe[] originalKeys = originalCurve.keys;
        int totalKeys = originalKeys.Length;

        // Find Keys within Time Range
        List<int> selectedIndices = new List<int>(totalKeys);
        List<float> selectedTimes = new List<float>(totalKeys);
        List<float> selectedValues = new List<float>(totalKeys);

        for (int i = 0; i < totalKeys; i++)
        {
            if (originalKeys[i].time >= timeStart && originalKeys[i].time <= timeEnd)
            {
                selectedIndices.Add(i);
                selectedTimes.Add(originalKeys[i].time);
                selectedValues.Add(originalKeys[i].value);
            }
        }

        int n = selectedIndices.Count;
        if (n < 3) return originalCurve;

        int numCP = Mathf.Min(controlPoints, n);

        // Sample Control Points with Local Averaging
        float[] cpValues = new float[numCP];
        int windowSize = Mathf.Max(5, n / numCP);
        int halfWin = windowSize / 2;

        for (int i = 0; i < numCP; i++)
        {
            float t = (float)i / (numCP - 1);
            int centerIdx = Mathf.RoundToInt(t * (n - 1));

            float sum = 0f;
            int count = 0;
            int start = Mathf.Max(0, centerIdx - halfWin);
            int end = Mathf.Min(n - 1, centerIdx + halfWin);

            for (int j = start; j <= end; j++)
            {
                sum += selectedValues[j];
                count++;
            }
            cpValues[i] = sum / count;
        }

        // Pre-Calculate Spline Values for all Positions
        float[] smoothedValues = new float[n];
        float[] tangents = new float[n];

        // First Pass: Get all Smoothed Values
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            smoothedValues[i] = EvaluateSpline(cpValues, numCP, t);
        }

        // Apply Amplitude Scaling if not 1.0
        if (Mathf.Abs(amplitudeScale - 1f) > 0.001f)
        {
            // Calculate the Linear Baseline from First to Last Point
            float startVal = smoothedValues[0];
            float endVal = smoothedValues[n - 1];

            // Scale Deviations from the Baseline
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);
                float baseline = Mathf.Lerp(startVal, endVal, t);
                float deviation = smoothedValues[i] - baseline;
                smoothedValues[i] = baseline + deviation * amplitudeScale;
            }
        }

        // Calculate Tangents
        for (int i = 0; i < n; i++)
        {
            float dt;

            if (i == 0)
            {
                dt = selectedTimes[1] - selectedTimes[0];
                tangents[i] = dt > 0.0001f ? (smoothedValues[1] - smoothedValues[0]) / dt : 0f;
            }
            else if (i == n - 1)
            {
                dt = selectedTimes[n - 1] - selectedTimes[n - 2];
                tangents[i] = dt > 0.0001f ? (smoothedValues[n - 1] - smoothedValues[n - 2]) / dt : 0f;
            }
            else
            {
                dt = selectedTimes[i + 1] - selectedTimes[i - 1];
                tangents[i] = dt > 0.0001f ? (smoothedValues[i + 1] - smoothedValues[i - 1]) / dt : 0f;
            }

            tangents[i] *= (1f - curveTension);
        }

        // Build New Keyframes
        Keyframe[] newKeys = new Keyframe[totalKeys];
        Array.Copy(originalKeys, newKeys, totalKeys);

        for (int i = 0; i < n; i++)
        {
            int keyIdx = selectedIndices[i];
            newKeys[keyIdx].value = smoothedValues[i];
            newKeys[keyIdx].inTangent = tangents[i];
            newKeys[keyIdx].outTangent = tangents[i];
            newKeys[keyIdx].weightedMode = WeightedMode.None;
        }

        // Preserve Endpoints
        if (preserveEndpoints && preserveCount > 0)
        {
            int pCount = Mathf.Min(preserveCount, n / 2);
            for (int i = 0; i < pCount; i++)
            {
                newKeys[selectedIndices[i]] = originalKeys[selectedIndices[i]];
                newKeys[selectedIndices[n - 1 - i]] = originalKeys[selectedIndices[n - 1 - i]];
            }
        }

        return new AnimationCurve(newKeys);
    }

    private float EvaluateSpline(float[] cpValues, int numPoints, float t)
    {
        if (numPoints < 2) return cpValues[0];
        if (numPoints == 2) return Mathf.Lerp(cpValues[0], cpValues[1], t);

        float segmentFloat = t * (numPoints - 1);
        int p1 = Mathf.Clamp(Mathf.FloorToInt(segmentFloat), 0, numPoints - 2);
        float localT = segmentFloat - p1;

        int p0 = Mathf.Max(p1 - 1, 0);
        int p2 = Mathf.Min(p1 + 1, numPoints - 1);
        int p3 = Mathf.Min(p1 + 2, numPoints - 1);

        float v0 = cpValues[p0], v1 = cpValues[p1], v2 = cpValues[p2], v3 = cpValues[p3];

        float t2 = localT * localT;
        float t3 = t2 * localT;

        return 0.5f * ((2f * v1) + (-v0 + v2) * localT + (2f * v0 - 5f * v1 + 4f * v2 - v3) * t2 + (-v0 + 3f * v1 - 3f * v2 + v3) * t3);
    }

    #endregion

    #region Apply

    private void ApplySmoothing()
    {
        if (!selectedClip) { EditorUtility.DisplayDialog("Error", "Select an Animation Clip.", "OK"); return; }

        var sel = GetSelectedBindings();
        if (sel.Count == 0) { EditorUtility.DisplayDialog("Error", "Select at least one curve.", "OK"); return; }

        GetTimeRange(out float timeStart, out float timeEnd);

        Undo.RecordObject(selectedClip, "Smooth Animation");
        int proc = 0;

        foreach (var b in sel)
        {
            var curve = AnimationUtility.GetEditorCurve(selectedClip, b);
            if (curve == null || curve.keys.Length < 3) continue;

            AnimationCurve smoothedCurve = SmoothCurve(curve, timeStart, timeEnd);

            if (keepKeysOnFrame)
            {
                Keyframe[] keys = smoothedCurve.keys;
                float fps = selectedClip.frameRate;
                for (int i = 0; i < keys.Length; i++)
                    keys[i].time = Mathf.Round(keys[i].time * fps) / fps;
                smoothedCurve.keys = keys;
            }

            AnimationUtility.SetEditorCurve(selectedClip, b, smoothedCurve);
            proc++;
        }

        EditorUtility.SetDirty(selectedClip);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Denoiser] Smoothed {proc} curves" + (hasTimeSelection ? $" ({timeStart:F2}s - {timeEnd:F2}s)" : ""));

        previewBackup = null;
        preview = false;
    }

    #endregion

    #region Preview

    private void RefreshPreview()
    {
        if (!preview || !selectedClip) return;
        RestorePreview();
        ApplyPreview();
        needsPreviewUpdate = true;
    }

    private void ApplyPreview()
    {
        if (!selectedClip) return;
        var sel = GetSelectedBindings();
        if (sel.Count == 0) return;

        GetTimeRange(out float timeStart, out float timeEnd);

        if (!previewBackup)
            previewBackup = Instantiate(selectedClip);
        else
            foreach (var b in sel)
            {
                var c = AnimationUtility.GetEditorCurve(previewBackup, b);
                if (c != null) AnimationUtility.SetEditorCurve(selectedClip, b, c);
            }

        foreach (var b in sel)
        {
            var curve = AnimationUtility.GetEditorCurve(selectedClip, b);
            if (curve == null || curve.keys.Length < 3) continue;

            AnimationCurve smoothedCurve = SmoothCurve(curve, timeStart, timeEnd);
            AnimationUtility.SetEditorCurve(selectedClip, b, smoothedCurve);
        }

        SceneView.RepaintAll();
    }

    private void RestorePreview()
    {
        if (!previewBackup || !selectedClip) return;

        foreach (var b in AnimationUtility.GetCurveBindings(previewBackup))
            AnimationUtility.SetEditorCurve(selectedClip, b, AnimationUtility.GetEditorCurve(previewBackup, b));

        DestroyImmediate(previewBackup);
        previewBackup = null;
        EditorUtility.SetDirty(selectedClip);
        SceneView.RepaintAll();
    }

    #endregion
}
