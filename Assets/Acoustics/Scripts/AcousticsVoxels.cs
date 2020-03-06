// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// Component class used to render voxel preview gizmos in the scene view. In its own class so it can be turned on/off separately.
/// </summary>
[DisallowMultipleComponent]
public class AcousticsVoxels : MonoBehaviour
{
    public AcousticsActualRenderer VoxelRenderer { get; set; }

    private void OnDrawGizmos()
    {
        if (VoxelRenderer != null)
        {
            VoxelRenderer.Render();
        }
    }
}
#endif
