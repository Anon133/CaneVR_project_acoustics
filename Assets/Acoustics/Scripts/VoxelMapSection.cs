// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using UnityEngine;
using System.Collections.Generic;

public class VoxelMapSection
{
    IntPtr sectionHandle = IntPtr.Zero;

    public readonly float[] Voxels;
    public readonly int VoxelCount;
    public readonly Vector3 MinCorner;
    public readonly Vector3 MaxCorner;
    public readonly float VoxelSize;

    public VoxelMapSection(IntPtr tritonHandle, Vector3 minCorner, Vector3 maxCorner)
    {
        try
        {
            if (!AcousticsPAL.Triton_GetVoxelMapSection(
                tritonHandle,
                new AcousticsPAL.TritonVec3f(minCorner),
                new AcousticsPAL.TritonVec3f(maxCorner),
                out sectionHandle))
            {
                throw new InvalidOperationException();
            }

            // Query and cache section properties
            AcousticsPAL.TritonVec3f voxelMinCorner;
            if (!AcousticsPAL.VoxelMapSection_GetMinCorner(sectionHandle, out voxelMinCorner))
            {
                throw new InvalidOperationException();
            }
            MinCorner = new Vector3(voxelMinCorner.x, voxelMinCorner.y, voxelMinCorner.z);

            AcousticsPAL.TritonVec3f mapIncrement;
            if (!AcousticsPAL.VoxelMapSection_GetCellIncrementVector(sectionHandle, out mapIncrement))
            {
                throw new InvalidOperationException();
            }
            Vector3 increment = new Vector3(mapIncrement.x, mapIncrement.y, mapIncrement.z);
            var halfIncrement = increment * 0.5f;

            // Remember, we start from x=y=z=1, not 0, so the extra CellIncrement
            var startVoxelCenter = MinCorner + increment + halfIncrement;
            var currentVoxelCenter = startVoxelCenter;
            AcousticsPAL.TritonVec3i numVoxels;
            if (!AcousticsPAL.VoxelMapSection_GetCellCount(sectionHandle, out numVoxels))
            {
                throw new InvalidOperationException();
            }

            List<float> voxels = new List<float>();
            for (int x = 1; x < numVoxels.x - 1; x++, currentVoxelCenter.x += increment.x)
            {
                for (int y = 1; y < numVoxels.y - 1; y++, currentVoxelCenter.y += increment.y)
                {
                    for (int z = 1; z < numVoxels.z - 1; z++, currentVoxelCenter.z += increment.z)
                    {
                        if (AcousticsPAL.VoxelMapSection_IsVoxelWall(sectionHandle, new AcousticsPAL.TritonVec3i(x, y, z)))
                        {
                            // Add this voxel to our voxel array
                            voxels.Add(currentVoxelCenter.x);
                            voxels.Add(currentVoxelCenter.y);
                            voxels.Add(currentVoxelCenter.z);
                        }
                    }
                    currentVoxelCenter.z = startVoxelCenter.z;
                }
                currentVoxelCenter.y = startVoxelCenter.y;
                currentVoxelCenter.z = startVoxelCenter.z;
            }

            // Assume voxels are cubes, length of side is magnitude in any one direction
            VoxelSize = increment.x;
            Voxels = voxels.ToArray();
            VoxelCount = (voxels.Count / 3);
        }
        finally
        {
            if (sectionHandle != IntPtr.Zero)
            {
                AcousticsPAL.VoxelMapSection_Destroy(sectionHandle);
            }
        }
    }
}