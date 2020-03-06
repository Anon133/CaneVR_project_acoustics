// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class AcousticsAdjustExperimental : MonoBehaviour
{
    private const float c_PerceptualDistanceWarpMin = 0.1f;
    private const float c_PerceptualDistanceWarpMax = 2;

    [Range(0.1f, 2)]
    [Tooltip("Exponential warping of perceived distance. Default=1, Range[0.1,2]")]
    public float PerceptualDistanceWarp = 1;
    private float last_DistanceWarp = 1;

    void Update()
    {
        var source = GetComponent<AudioSource>();

        // Set distance warping exponent
        if (last_DistanceWarp != PerceptualDistanceWarp)
        {
            last_DistanceWarp = PerceptualDistanceWarp;
            source.SetSpatializerFloat((int)AcousticsAdjust.SpatializerParams.PerceptualDistanceWarp, last_DistanceWarp);
        }
    }
}

