// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AcousticsAdjust))]
[CanEditMultipleObjects]
public class AcousticsAdjustEditor : Editor
{
    private SerializedProperty Occlusion;
    private SerializedProperty ReverbPowerCurve;
    private SerializedProperty DecayTimeScale;
    private SerializedProperty EnableAcoustics;
    private SerializedProperty Transmission;
    private SerializedProperty Outdoorness;

    public void OnEnable()
    {
        Occlusion = serializedObject.FindProperty("Occlusion");
        ReverbPowerCurve = serializedObject.FindProperty("ReverbPowerCurve");
        DecayTimeScale = serializedObject.FindProperty("DecayTimeScale");
        EnableAcoustics = serializedObject.FindProperty("EnableAcoustics");
        Transmission = serializedObject.FindProperty("Transmission");
        Outdoorness = serializedObject.FindProperty("Outdoorness");
    }
    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();

        EditorGUILayout.PropertyField(EnableAcoustics);
        EditorGUILayout.PropertyField(Occlusion);
        EditorGUILayout.PropertyField(Transmission, new GUIContent("Transmission (dB)"));
        EditorGUILayout.PropertyField(ReverbPowerCurve);
        EditorGUILayout.PropertyField(DecayTimeScale);
        EditorGUILayout.PropertyField(Outdoorness);
        serializedObject.ApplyModifiedProperties();
    }
}
