// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Microsoft.Cloud.Acoustics
{
    public class AcousticsProbesRenderer : AcousticsActualRenderer
    {
        private Triton.SimulationConfig m_previewData;

        // Class that draws the gizmos showing the acoustic probe locations.
        public override void Render()
        {
            if (m_previewData == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;

            for (int curProbe = 0; curProbe < m_previewData.NumProbes; curProbe++)
            {
                Triton.Vec3f curLocation = m_previewData.GetProbePoint(curProbe);

                Gizmos.DrawCube(AcousticsEditor.TritonToWorld(new Vector3(curLocation.x, curLocation.y, curLocation.z)), new Vector3(0.2f, 0.2f, 0.2f));
            }
        }

        public void SetPreviewData(Triton.SimulationConfig results)
        {
            m_previewData = results;
        }
    }
}
