// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using UnityEngine;
using System.Runtime.InteropServices;

internal class AcousticsPAL
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TritonVec3f
    {
        public float x;
        public float y;
        public float z;

        public TritonVec3f(float a, float b, float c) { x = a; y = b; z = c; }
        public TritonVec3f(Vector3 vec) { x = vec.x; y = vec.y; z = vec.z; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TritonVec3i
    {
        public int x;
        public int y;
        public int z;

        public TritonVec3i(int a, int b, int c) { x = a; y = b; z = c; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ATKMatrix4x4
    {
        public float m11;
        public float m12;
        public float m13;
        public float m14;
        public float m21;
        public float m22;
        public float m23;
        public float m24;
        public float m31;
        public float m32;
        public float m33;
        public float m34;
        public float m41;
        public float m42;
        public float m43;
        public float m44;

        public ATKMatrix4x4(Matrix4x4 a)
        {
            m11 = a.m00;
            m12 = a.m01;
            m13 = a.m02;
            m14 = a.m03;
            m21 = a.m10;
            m22 = a.m11;
            m23 = a.m12;
            m24 = a.m13;
            m31 = a.m20;
            m32 = a.m21;
            m33 = a.m22;
            m34 = a.m23;
            m41 = a.m30;
            m42 = a.m31;
            m43 = a.m32;
            m44 = a.m33;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TritonAcousticParameters
    {
        public float DirectDelay;
        public float DirectLoudnessDB;
        public float DirectAzimuth;
        public float DirectElevation;

        public float ReflectionsDelay;
        public float ReflectionsLoudnessDB;

        public float ReflLoudnessDB_Channel_0;
        public float ReflLoudnessDB_Channel_1;
        public float ReflLoudnessDB_Channel_2;
        public float ReflLoudnessDB_Channel_3;
        public float ReflLoudnessDB_Channel_4;
        public float ReflLoudnessDB_Channel_5;

        public float EarlyDecayTime;
        public float ReverbTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TritonAcousticParametersDebug
    {
        public int SourceId;
        public TritonVec3f SourcePosition;
        public TritonVec3f ListenerPosition;
        public float Outdoorness;
        public TritonAcousticParameters AcousticParameters;
    }

    public enum ProbeLoadState
    {
        Loaded,
        NotLoaded,
        LoadFailed,
        LoadInProgress,
        DoesNotExist,
        Invalid
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeMetadata
    {
        // Current loading state of this probe
        public ProbeLoadState State;

        // World location of this probe
        public TritonVec3f Location;

        // Corners of the cubical region around this probe
        // for which it has data
        public TritonVec3f DataMinCorner;
        public TritonVec3f DataMaxCorner;
    }

    // Import the functions from the DLL
    // Define the dll name based on the target platform
#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_ANDROID || UNITY_XBOXONE
    const string TritonDll = "Triton";
    const string SpatializerDll = "AudioPluginMicrosoftAcoustics";
#elif UNITY_STANDALONE_OSX
    // Triton dylib is included inside AudioPluginMicrosoftAcoustics bundle file
    // (virtual directory) on MacOS, specify bundle name in order to bind to 
    // libTriton.dylib exports
    const string TritonDll = "AudioPluginMicrosoftAcoustics";
    const string SpatializerDll = "AudioPluginMicrosoftAcoustics";
#else
    // No other platforms are currently supported
    const string TritonDll = " ";
    const string SpatializerDll = " ";
#endif

    // Only import functions for platforms we have support for, otherwise DllImport will throw an error
    // Add to the following #if as other platforms come online
    //#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_ANDROID || UNITY_XBOXONE
    // Spatializer Exports
    [DllImport(SpatializerDll)]
    public static extern bool Spatializer_SetTritonHandle(IntPtr triton);

    [DllImport(SpatializerDll)]
    public static extern void Spatializer_SetAceFileLoaded(bool loaded);

    [DllImport(SpatializerDll)]
    public static extern void Spatializer_SetTransforms(ATKMatrix4x4 worldToLocal, ATKMatrix4x4 localToWorld);

    [DllImport(SpatializerDll)]
    public static extern bool Spatializer_GetDebugInfo(out IntPtr debugInfo, out int count);

    [DllImport(SpatializerDll)]
    public static extern void Spatializer_FreeDebugInfo(IntPtr debugInfo);

    // Triton API Exports
    [DllImport(TritonDll)]
    public static extern bool Triton_CreateInstance(bool debug, out IntPtr triton);

    [DllImport(TritonDll)]
    public static extern bool Triton_LoadAceFile(IntPtr triton, [MarshalAs(UnmanagedType.LPStr)] string filename);

    [DllImport(TritonDll)]
    public static extern bool Triton_LoadAll(IntPtr triton, bool block);

    [DllImport(TritonDll)]
    public static extern bool Triton_UnloadAll(IntPtr triton, bool block);

    [DllImport(TritonDll)]
    public static extern bool Triton_LoadRegion(IntPtr triton, TritonVec3f center, TritonVec3f length, bool unloadOutside, bool block, out int probesLoaded);

    [DllImport(TritonDll)]
    public static extern bool Triton_UnloadRegion(IntPtr triton, TritonVec3f center, TritonVec3f length, bool block);

    [DllImport(TritonDll)]
    public static extern bool Triton_DestroyInstance(IntPtr triton);

    [DllImport(TritonDll)]
    public static extern bool Triton_Clear(IntPtr triton);

    [DllImport(TritonDll)]
    public static extern bool Triton_GetProbeCount(IntPtr triton, out int count);

    [DllImport(TritonDll)]
    public static extern bool Triton_GetProbeMetadata(IntPtr triton, int index, out ProbeMetadata metadata);

    [DllImport(TritonDll)]
    public static extern bool Triton_GetVoxelMapSection(
        IntPtr triton,
        TritonVec3f minCorner,
        TritonVec3f maxCorner,
        out IntPtr section);

    [DllImport(TritonDll)]
    public static extern bool VoxelMapSection_Destroy(IntPtr section);

    [DllImport(TritonDll)]
    public static extern bool VoxelMapSection_GetCellCount(IntPtr section, out TritonVec3i count);

    [DllImport(TritonDll)]
    public static extern bool VoxelMapSection_IsVoxelWall(IntPtr section, TritonVec3i cell);

    [DllImport(TritonDll)]
    public static extern bool VoxelMapSection_GetMinCorner(IntPtr section, out TritonVec3f value);

    [DllImport(TritonDll)]
    public static extern bool VoxelMapSection_GetCellIncrementVector(IntPtr section, out TritonVec3f vector);
    //#endif

}