// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// Class used to hide editor only implementation so we don't have to include it in the game runtime code.
/// </summary>
public abstract class AcousticsActualRenderer : ScriptableObject
{
    public abstract void Render();
}

/// <summary>
/// Component class used to render probe preview gizmos in the scene view. In its own class so it can be turned on/off separately.
/// </summary>
[DisallowMultipleComponent]
public class AcousticsProbes : MonoBehaviour
{
    public AcousticsActualRenderer ProbesRenderer { get; set; }

    private void OnDrawGizmos()
    {
        if (ProbesRenderer != null)
        {
            ProbesRenderer.Render();
        }
    }
}
#endif
