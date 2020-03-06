// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System;
using System.Runtime.InteropServices;

[DisallowMultipleComponent]
public class AcousticsManager : MonoBehaviour
{
    #region AcousticsManager definition
    public static AcousticsManager Instance
    {
        get { return instance; }
        private set { instance = value; }
    }
    static AcousticsManager instance = null;

    private AcousticsInterop acousticsInterop = null;

    [Tooltip("The ACE file is produced by the bake process. It needs the '.bytes' suffix to be loaded properly.")]
    public TextAsset AceFile = null;
    string last_AceFile = string.Empty;

#if UNITY_EDITOR
    // Gizmo information
    private AcousticsProbes probeRenderer;
    private AcousticsVoxels voxelRenderer;
    private VoxelMapSection lastVoxelMapSection;
    private Vector3 lastStartPosition;
    private Vector3 lastEndPosition;

    public struct ProbeList
    {
        public Vector3 center;
        public Color color;
        public ProbeList(Vector3 c, Color col)
        {
            center = c;
            color = col;
        }
    }
#endif

    static Matrix4x4 tritonToWorld;
    static Matrix4x4 worldToTriton;

    private const float c_AceTileLoadMargin = 0.8f;
    private Vector3 m_PlayerPos = new Vector3(0, 0, 0);
    public Vector3 ProbeLoadRegion = new Vector3(50, 50, 50);


    void InitAcoustics()
    {
        // Get rid of the old runtime instance
        if (acousticsInterop != null)
        {
            acousticsInterop.Dispose();
        }

        var dataPath = System.IO.Path.Combine(Application.temporaryCachePath, AceFile.name);
        last_AceFile = AceFile.name;

        if (File.Exists(dataPath))
        {
            File.Delete(dataPath);
        }
        File.WriteAllBytes(dataPath, AceFile.bytes);

        // Create the new runtime instance
        acousticsInterop = new AcousticsInterop(true, dataPath);
        var currentPlayerPos = Camera.main.transform.position;
        acousticsInterop.LoadProbes(WorldToTriton(currentPlayerPos), ProbeLoadRegion);
        m_PlayerPos = currentPlayerPos;

#if UNITY_EDITOR
        // Setup the probe drawing gizmo
        if (probeRenderer == null)
        {
            probeRenderer = gameObject.AddComponent<AcousticsProbes>();
            probeRenderer.ProbesRenderer = ScriptableObject.CreateInstance<AcousticsManagerProbesRenderer>();
        }

        // Setup the voxel drawing gizmo
        if (voxelRenderer == null)
        {
            voxelRenderer = gameObject.AddComponent<AcousticsVoxels>();
            voxelRenderer.VoxelRenderer = ScriptableObject.CreateInstance<AcousticsManagerVoxelsRenderer>();
        }
#endif
    }

    Vector3 TruncateVector(Vector3 playPose)
    {
        playPose.x = (int)playPose.x;
        playPose.y = (int)playPose.y;
        playPose.z = (int)playPose.z;
        return playPose;
    }

    Vector4 WorldToTriton(Vector4 position)
    {
        return worldToTriton * transform.InverseTransformPoint(position);
    }

    Vector4 TritonToWorld(Vector4 position)
    {
        return transform.TransformPoint(tritonToWorld * position);
    }

#if UNITY_EDITOR
    void UpdateVoxelsForGizmo()
    {
        float offset = 5f;

        // Queries into triton don't work well with floating point values
        // Truncate to integer to keep the voxel coordinate system stable
        var cameraLoc = TruncateVector(Camera.main.transform.position);
        var playPose = (Vector3)WorldToTriton(cameraLoc);
        var startPos = playPose - new Vector3(offset, offset, offset);
        var endPos = playPose + new Vector3(offset, offset, offset);

        // Get the new voxel map section only if the region has changed significantly.
        var startDiff = (startPos - lastStartPosition).magnitude;
        var endDiff = (endPos - lastEndPosition).magnitude;
        if (startDiff > offset / 2 || endDiff > offset / 2 || lastVoxelMapSection == null)
        {
            lastVoxelMapSection = acousticsInterop.GetVoxelMapSection(startPos, endPos);
            lastStartPosition = startPos;
            lastEndPosition = endPos;

            var voxels = lastVoxelMapSection.Voxels;
            var voxelSize = lastVoxelMapSection.VoxelSize;
            var numVoxels = lastVoxelMapSection.VoxelCount;

            if (numVoxels > 0)
            {
                Vector3[] voxelCenters = new Vector3[numVoxels];

                // Loop through all the voxel data and add them to the renderer
                for (int i = 0; i < voxels.Length / 3; i++)
                {
                    Vector3 center = TritonToWorld(new Vector3(voxels[i * 3], voxels[i * 3 + 1], voxels[i * 3 + 2]));
                    voxelCenters[i] = center;
                }

                ((AcousticsManagerVoxelsRenderer)voxelRenderer.VoxelRenderer).SetVoxels(voxelCenters, voxelSize);
            }
        }
    }

    void UpdateProbesForGizmo()
    {
        List<ProbeList> probePoints = new List<ProbeList>();
        var numProbes = acousticsInterop.GetProbeCount();
        for (int i = 0; i < numProbes; i++)
        {
            var location = new Vector3(0, 0, 0);
            Color color;
            acousticsInterop.GetProbeMetadata(i, ref location, out color);

            var center = TritonToWorld(location);
            probePoints.Add(new ProbeList(center, color));
        }
        ((AcousticsManagerProbesRenderer)probeRenderer.ProbesRenderer).SetProbes(probePoints);
    }
#endif

    // MonoBehavior methods.
    void Awake()
    {
        // Handle static setup
        if (Instance == null)
        {
            DontDestroyOnLoad(this);
            Instance = this;

            // Save off the transforms once
            // this is converting from Maya Z+ to Unity's default space
            tritonToWorld = new Matrix4x4();
            tritonToWorld.SetRow(0, new Vector4(1, 0, 0, 0));
            tritonToWorld.SetRow(1, new Vector4(0, 0, 1, 0));
            tritonToWorld.SetRow(2, new Vector4(0, 1, 0, 0));
            tritonToWorld.SetRow(3, new Vector4(0, 0, 0, 1));

            // Right now this techincally doesn't change anything, but leaving it like this in case the transform ever changes
            worldToTriton = Matrix4x4.Inverse(tritonToWorld);
        }
        else if (Instance != this)
        {
            Destroy(this);
        }
    }

    // OnDisable is called both when the script is disabled, and when the component is destroyed
    void OnDisable()
    {
        if (acousticsInterop != null)
        {
            acousticsInterop.Dispose();
        }
#if UNITY_EDITOR
        // Clear out the cache on teardown
        ClearDebugCache();
        ((AcousticsManagerProbesRenderer)probeRenderer.ProbesRenderer).SetProbes(null);
        ((AcousticsManagerVoxelsRenderer)voxelRenderer.VoxelRenderer).SetVoxels(null, 0);
#endif
    }

    void OnEnable()
    {
        InitAcoustics();
#if UNITY_EDITOR
        // Make sure the cache is clear on startup
        ClearDebugCache();
#endif
    }

    void Update()
    {
        // If the transform changes during operation, send the new one to the spatializer
        if (acousticsInterop != null && transform.hasChanged)
        {
            acousticsInterop.SetTransforms(transform.worldToLocalMatrix, transform.localToWorldMatrix);
        }

        if (AceFile != null)
        {
            if (AceFile.name != last_AceFile)
            {
                InitAcoustics();
            }

            // Figure out if we need to stream the next tile in
            var currentPlayerPos = Camera.main.transform.position;
            var differenceFromLastLoad = (currentPlayerPos - m_PlayerPos);
            differenceFromLastLoad.x = Mathf.Abs(differenceFromLastLoad.x);
            differenceFromLastLoad.y = Mathf.Abs(differenceFromLastLoad.y);
            differenceFromLastLoad.z = Mathf.Abs(differenceFromLastLoad.z);
            var loadThreshold = ProbeLoadRegion * c_AceTileLoadMargin * 0.5f;
            bool shouldUpdate = differenceFromLastLoad.x > loadThreshold.x || 
                                differenceFromLastLoad.y > loadThreshold.y ||
                                differenceFromLastLoad.z > loadThreshold.z;
            if (shouldUpdate)
            {
                acousticsInterop.LoadProbes(WorldToTriton(currentPlayerPos), ProbeLoadRegion);
                m_PlayerPos = currentPlayerPos;
            }

            // Update debug info
#if UNITY_EDITOR
            UpdateProbesForGizmo();

            // Updating voxels is expensive. Only update them if the gizmo is actively drawing
            // If the gizmo is not actively drawing, HasRendered will remain false.
            if (((AcousticsManagerVoxelsRenderer)voxelRenderer.VoxelRenderer).HasRendered)
            {
                UpdateVoxelsForGizmo();
            }
#endif
        }
        else
        {
            OnDisable();
        }
    }
    #endregion

#if UNITY_EDITOR
    #region Gizmo drawing
    private float m_timeSinceLastQuery = 0;
    private Vector3 tritonDirection;
    private IntPtr m_debugInfoCache = IntPtr.Zero;
    private int m_debugInfoCacheSize = 0;

    private void ClearDebugCache()
    {
        if (m_debugInfoCache != IntPtr.Zero)
        {
            AcousticsPAL.Spatializer_FreeDebugInfo(m_debugInfoCache);

        }
        m_debugInfoCache = IntPtr.Zero;
        m_debugInfoCacheSize = 0;
    }

    Vector3 PolarToCartesian(float Azimuth, float Elevation)
    {
        var retVal = new Vector3();
        var el = Elevation * Mathf.Deg2Rad;
        var az = Azimuth * Mathf.Deg2Rad;
        retVal.z = Mathf.Cos(el);
        var horiz = Mathf.Sin(el);
        retVal.x = horiz * Mathf.Cos(az);
        retVal.y = horiz * Mathf.Sin(az);
        return retVal;
    }

    public void OnDrawGizmos()
    {
        // Nothing to do if not in play mode, as triton won't be available for queries
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        m_timeSinceLastQuery += Time.deltaTime;
        // Do a new query once per second
        if (m_timeSinceLastQuery > 1)
        {
            // Get acoustic parameters for this source out of triton
            m_timeSinceLastQuery = 0;
            // Free the previous structure before getting the new one
            ClearDebugCache();
            AcousticsPAL.Spatializer_GetDebugInfo(out m_debugInfoCache, out m_debugInfoCacheSize);
        }

        // debugInfo is an array of type TritonAcousticParametersDebug. We need to do some C# marshalling to convert from an IntPtr
        int structSize = Marshal.SizeOf(typeof(AcousticsPAL.TritonAcousticParametersDebug));
        for (int i = 0; i < m_debugInfoCacheSize; i++)
        {
            var data = new IntPtr(m_debugInfoCache.ToInt64() + structSize * i);
            var parameters = (AcousticsPAL.TritonAcousticParametersDebug)Marshal.PtrToStructure(data, typeof(AcousticsPAL.TritonAcousticParametersDebug));

            // Now that we have the parameters, display them in the UI
            string debugText = $"Acoustics Parameters for Source: {parameters.SourceId}\n" +
            $"Source Position: {parameters.SourcePosition.x}, {parameters.SourcePosition.y}, {parameters.SourcePosition.z}\n" +
            $"Listener Position: {parameters.ListenerPosition.x}, {parameters.ListenerPosition.y}, {parameters.ListenerPosition.z}\n" +
            $"Listener Outdoorness: {parameters.Outdoorness}\n" +
            $"Direct Azimuth: {parameters.AcousticParameters.DirectAzimuth}\n" +
            $"Direct Elevation: {parameters.AcousticParameters.DirectElevation}\n" +
            $"Direct Loudness DB: {parameters.AcousticParameters.DirectLoudnessDB}\n" +
            $"Reflections Loudness DB: {parameters.AcousticParameters.ReflectionsLoudnessDB}\n" +
            $"Early Decay Time: {parameters.AcousticParameters.EarlyDecayTime}\n" +
            $"Reverb Time: {parameters.AcousticParameters.ReverbTime}";

            tritonDirection = TritonToWorld(PolarToCartesian(parameters.AcousticParameters.DirectAzimuth, parameters.AcousticParameters.DirectElevation));

            // Scale up the direction to be a 5m line instead of a 1m line to make it easier to see
            tritonDirection *= 5;

            // Draw the line illustrating the primary arrival direction
            Handles.color = Color.yellow;
            var points = new Vector3[2];
            points[0] = Camera.main.transform.position;
            // If the line is at eye-level, you won't actually be able to see it. Lower the start point a bit
            points[0].y -= 0.2f;
            // Apply camera rotation/position
            points[1] = -tritonDirection + Camera.main.transform.position;
            Handles.DrawAAPolyLine(30, points);

            // Display acoustic parameter values in scene
            Handles.BeginGUI();
            GUI.backgroundColor = Color.black;
            GUI.color = Color.green;
            GUI.contentColor = Color.green;
            Gizmos.color = Color.green;

            var view = UnityEditor.SceneView.currentDrawingSceneView;
            var camera = view ? view.camera : Camera.main;
            if (camera != null)
            {
                // Frustrum culling
                var screenPos = camera.WorldToScreenPoint(TritonToWorld(new Vector3(parameters.SourcePosition.x, parameters.SourcePosition.y, parameters.SourcePosition.z)));
                if (screenPos.y < 0 || screenPos.y > Screen.height || screenPos.x < 0 || screenPos.x > Screen.width || screenPos.z < 0)
                {
                    Handles.EndGUI();
                    continue;
                }
                var size = GUI.skin.label.CalcSize(new GUIContent(debugText));
                var r = new Rect(screenPos.x - (size.x / 2), -screenPos.y + camera.scaledPixelHeight, size.x, size.y);
                GUI.Box(r, debugText, EditorStyles.numberField);
                GUI.Label(r, debugText);
            }
            
            Handles.EndGUI();
        }
    }


    class AcousticsManagerProbesRenderer : AcousticsActualRenderer
    {
        List<ProbeList> probePoints;

        public override void Render()
        {
            Gizmos.color = Color.cyan;

            if (probePoints != null)
            {
                foreach (var probe in probePoints)
                {
                    Gizmos.color = probe.color;
                    Gizmos.DrawCube(probe.center, new Vector3(0.2f, 0.2f, 0.2f));
                }
            }
        }

        public void SetProbes(List<ProbeList> probes)
        {
            probePoints = probes;
        }
    }

    class AcousticsManagerVoxelsRenderer : AcousticsActualRenderer
    {
        Vector3[] VoxelCenters;
        float VoxelSize;
        public bool HasRendered = false;

        public override void Render()
        {
            Gizmos.color = Color.green;

            if (VoxelCenters != null && VoxelCenters.Length > 0)
            {
                // Loop through all the voxel data and draw them all
                for (int i = 0; i < VoxelCenters.Length; i++)
                {
                    // Frustrum culling
                    var cameraPoint = Camera.main.WorldToViewportPoint(VoxelCenters[i]);
                    if (cameraPoint.magnitude > 8 || cameraPoint.x < 0 || cameraPoint.x > 1 || cameraPoint.y < 0 || cameraPoint.y > 1 || cameraPoint.z < 0)
                    {
                        continue;
                    }
                    var minCorner = VoxelCenters[i] - new Vector3(VoxelSize, VoxelSize, VoxelSize);
                    var corner1 = minCorner + new Vector3(VoxelSize, 0, 0);
                    var corner2 = minCorner + new Vector3(0, VoxelSize, 0);
                    var corner3 = minCorner + new Vector3(0, 0, VoxelSize);

                    // Because there are so many voxels in a row, we can get away with only drawing a flat corner of them and still display a decent looking result
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(minCorner, corner1);
                    Gizmos.DrawLine(minCorner, corner2);
                    Gizmos.DrawLine(minCorner, corner3);
                }
            }

            HasRendered = true;
        }

        public void SetVoxels(Vector3[] voxels, float voxelSize)
        {
            VoxelCenters = voxels;
            VoxelSize = voxelSize;

            HasRendered = false;
        }
    }
    #endregion
#endif
}
