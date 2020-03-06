// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using UnityEngine;

public class AcousticsInterop : IDisposable
{
    private bool disposed = false;
    private System.IntPtr tritonHandle = IntPtr.Zero;

    public AcousticsInterop(bool debug)
    {
        AcousticsPAL.Triton_CreateInstance(debug, out tritonHandle);
        // Pass the triton instance to the Spatializer
        if (!AcousticsPAL.Spatializer_SetTritonHandle(tritonHandle))
        {
            throw new Exception ("Failed to set Triton handle. Check your plugin configuration");
        }
    }

    public AcousticsInterop(bool debug, string filename) : this(debug)
    {
        if (!AcousticsPAL.Triton_LoadAceFile(tritonHandle, filename))
        {
            throw new Exception ("Invalid ACE file: " + filename);
        }

        AcousticsPAL.Spatializer_SetAceFileLoaded(true);
    }

    ~AcousticsInterop()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed && tritonHandle != IntPtr.Zero)
        {
            // Tell Spatializer that Triton is going away.
            AcousticsPAL.Spatializer_SetAceFileLoaded(false);
            AcousticsPAL.Spatializer_SetTritonHandle(IntPtr.Zero);
            AcousticsPAL.Triton_UnloadAll(tritonHandle, true);
            AcousticsPAL.Triton_DestroyInstance(tritonHandle);
            disposed = true;
        }
    }

    public int GetProbeCount()
    {
        int count = 0;
        if (!AcousticsPAL.Triton_GetProbeCount(tritonHandle, out count))
        {
            throw new InvalidOperationException();
        }

        return count;
    }

    public void GetProbeMetadata(int probeIndex, ref Vector3 location, out Color color)
    {
        var probeData = new AcousticsPAL.ProbeMetadata();
        if (!AcousticsPAL.Triton_GetProbeMetadata(tritonHandle, probeIndex, out probeData))
        {
            throw new InvalidOperationException();
        }

        location.x = probeData.Location.x;
        location.y = probeData.Location.y;
        location.z = probeData.Location.z;

        switch (probeData.State)
        {
            case AcousticsPAL.ProbeLoadState.DoesNotExist:
            case AcousticsPAL.ProbeLoadState.Invalid:
            {
                color = Color.red;
                break;
            }
            case AcousticsPAL.ProbeLoadState.Loaded:
            {
                color = Color.cyan;
                break;
            }
            case AcousticsPAL.ProbeLoadState.LoadFailed:
            {
                color = Color.magenta;
                break;
            }
            case AcousticsPAL.ProbeLoadState.LoadInProgress:
            {
                color = Color.yellow;
                break;
            }
            case AcousticsPAL.ProbeLoadState.NotLoaded:
            default:
            {
                color = Color.gray;
                break;
            }
        }
    }

    public void LoadProbes(Vector3 position, Vector3 probeLoadRegion)
    {
        bool unloadOutside = true;
        bool shouldBlock = false;
        int probeCount = 0;
        AcousticsPAL.Triton_LoadRegion(tritonHandle, new AcousticsPAL.TritonVec3f(position), new AcousticsPAL.TritonVec3f(probeLoadRegion), unloadOutside, shouldBlock, out probeCount);
    }

    public VoxelMapSection GetVoxelMapSection(Vector3 minCorner, Vector3 maxCorner)
    {
        return new VoxelMapSection(tritonHandle, minCorner, maxCorner);
    }

    public void SetTransforms(Matrix4x4 worldToLocal, Matrix4x4 localToWorld)
    {
        AcousticsPAL.Spatializer_SetTransforms(new AcousticsPAL.ATKMatrix4x4(worldToLocal), new AcousticsPAL.ATKMatrix4x4(localToWorld));
    }
}
