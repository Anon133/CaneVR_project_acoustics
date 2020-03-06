// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using UnityEngine;

#if UNITY_EDITOR
namespace Microsoft.Cloud.Acoustics
{
    [DisallowMultipleComponent]
    public class AcousticsNavigation : MonoBehaviour
    {
        // This component has no content. Its presence on a GameObject indicates
        // that the object should be used as a navigation mesh when determining probe point layout.
    }
}
#endif
