// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using UnityEngine;
using UnityEditor;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class AcousticsAdjust : MonoBehaviour
{
    private const float c_ReverbPowerAdjustMin = -20;
    private const float c_ReverbPowerAdjustMax = 20;
    private const float c_ReverbPowerAdjustMaxDistance = 50;

    private const string c_ReverbPowerAdjustLabel = "Wetness (dB)";

    // Spatializer parameter indices
    public enum SpatializerParams
    {
        ReverbPowerAdjust = 0,
        DecayTimeScale = 1,
        EnableTriton = 2,
        OcclusionFactor = 3,
        PerceptualDistanceWarp = 4,
        TransmissionDb = 5,
        OutdoornessAdjustment = 6,
        Count
    }

    [Range(0, 2)]
    [Tooltip("Adjusts the occlusion effect multiplicatively. Default=1, Range[0,2]")]
    public float Occlusion = 1;
    private float last_occlusion = 1;

    [Range(-60, 0)]
    [Tooltip("Specify additional dry signal propagated directly from source to listener, in dB. Default=-60 (disabled), Range[-60,0]")]
    public float Transmission = -60;
    private float last_transmissionDb = -60;

    [CurveDimensions(0, c_ReverbPowerAdjustMaxDistance, c_ReverbPowerAdjustMin, c_ReverbPowerAdjustMax, c_ReverbPowerAdjustLabel)]
    [Tooltip("Additively adjust the calculated reverb wetness in dB based on source-listener distance. Default=0dB, Range [-20dB,20dB]")]
    [ContextMenuItem("Reset", "ResetReverbCurve")]
    public AnimationCurve ReverbPowerCurve = AnimationCurve.Linear(0, 0, c_ReverbPowerAdjustMaxDistance, 0);
    private float last_reverbPower = 0;

    [Range(0, 2)]
    [Tooltip("Adjusts the reverb decay time multiplicatively. Default=1, Range[0,2]")]
    public float DecayTimeScale = 1;
    private float last_decayTimeScale = 1;

    [Tooltip("If enabled, this sound source should use the full acoustic simulation. Otherwise, regular spatialization is used. Default=true")]
    public bool EnableAcoustics = true;
    private bool last_enableAcoustics = true;

    [Range(-1, 1)]
    [Tooltip("Adjusts how \"outdoors\" a source sounds. -1 being completely indoors, 1 being completely outdoors. Default=0. Range[-1,1]")]
    public float Outdoorness = 0.0f;
    private float last_outdoorness = 0.0f;

    private void ResetReverbCurve()
    {
        ReverbPowerCurve = AnimationCurve.Linear(0, 0, c_ReverbPowerAdjustMaxDistance, 0);

#if UNITY_EDITOR
        // This forces the inspector preview of the animation curve to refresh
        AssetDatabase.Refresh();
#endif
    }

    void Update()
    {
        var source = GetComponent<AudioSource>();

        // Source-listener distance
        var distance = Vector3.Distance(transform.position, Camera.main.transform.position);

        // Get the reverb for this distance
        var reverbPower = ReverbPowerCurve.Evaluate(distance);

        if (last_reverbPower != reverbPower)
        {
            last_reverbPower = reverbPower;
            source.SetSpatializerFloat((int)SpatializerParams.ReverbPowerAdjust, last_reverbPower);
        }

        if (last_decayTimeScale != DecayTimeScale)
        {
            last_decayTimeScale = DecayTimeScale;
            source.SetSpatializerFloat((int)SpatializerParams.DecayTimeScale, last_decayTimeScale);
        }

        if (last_enableAcoustics != EnableAcoustics)
        {
            last_enableAcoustics = EnableAcoustics;
            source.SetSpatializerFloat((int)SpatializerParams.EnableTriton, last_enableAcoustics ? 1 : 0);
        }

        if (last_occlusion != Occlusion)
        {
            last_occlusion = Occlusion;
            source.SetSpatializerFloat((int)SpatializerParams.OcclusionFactor, last_occlusion);
        }

        if (last_transmissionDb != Transmission)
        {
            last_transmissionDb = Transmission;
            source.SetSpatializerFloat((int)SpatializerParams.TransmissionDb, last_transmissionDb);
        }
        
        if (last_outdoorness != Outdoorness)
        {
            last_outdoorness = Outdoorness;
            source.SetSpatializerFloat((int)SpatializerParams.OutdoornessAdjustment, last_outdoorness);
        }
    }
}

#if UNITY_EDITOR
/// <summary>
/// Custom drawer for the startup of AnimationCurves. Sets the grid range for the curve editor.
/// </summary>
[CustomPropertyDrawer(typeof(CurveDimensions))]
public class CurveDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        CurveDimensions curve = attribute as CurveDimensions;
        if (property.propertyType == SerializedPropertyType.AnimationCurve)
        {
            EditorGUI.CurveField(position, property, Color.green, new Rect(curve.XMin, curve.YMin, System.Math.Abs(curve.XMin-curve.XMax), System.Math.Abs(curve.YMin - curve.YMax)), new GUIContent(curve.Label));
        }
    }
}
#endif

/// <summary>
/// Property attribute for setting the CurveField grid range.
/// </summary>
public class CurveDimensions : PropertyAttribute
{
    public float XMin, XMax, YMin, YMax;
    public string Label;

    public CurveDimensions(float xMin, float xMax, float yMin, float yMax, string label)
    {
        this.XMin = xMin;
        this.XMax = xMax;
        this.YMin = yMin;
        this.YMax = yMax;
        this.Label = label;
    }
}
