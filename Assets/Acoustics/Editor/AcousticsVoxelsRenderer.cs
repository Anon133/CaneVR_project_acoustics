// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Microsoft.Cloud.Acoustics
{
    /// <summary>
    /// Class that draws the voxels from a Pre-Bake calculation in the scene view (based on data in the .vox file). There is a different renderer
    /// that draws voxels based on data in the ACE file at runtime.
    /// </summary>
    public class AcousticsVoxelsRenderer : AcousticsActualRenderer
    {
        // Note: Increasing either of these values will have a noticable impact on render performance!
        private const float MaxRenderDistance = 5.0f; // Distance in meters from the camera we should render voxels
        private const float RenderViewAngle = 40.0f;  // Field of view away from center within which we should render voxels, in degrees.

        private Triton.SimulationConfig m_previewData;

        bool m_staticValuesCached = false;

        float m_cellSize;
        float m_centerOffset;
        Vector3 m_boxMin;
        Bounds m_voxelBounds;
        BoundsInt m_clampValues;

        /// <summary>
        /// Specifies which axis direction we want to draw a voxel side on
        /// </summary>
        private enum FaceDirection
        {
            X,
            Y,
            Z
        };

        // Render the current set of pre-bake voxels
        public override void Render()
        {
            if (m_previewData == null)
            {
                return;
            }

            if (!m_staticValuesCached)
            {
                // Get necessary data from Triton for use later
                Triton.Voxels voxels = m_previewData.VoxelData;

                m_cellSize = voxels.CellSize;
                m_centerOffset = m_cellSize / 2;

                Triton.AABox<Triton.Vec3f, float> voxelBBox = voxels.VoxBbox;
                m_boxMin = AcousticsEditor.TritonToWorld(new Vector3(voxelBBox.MinCorner.x, voxelBBox.MinCorner.y, voxelBBox.MinCorner.z));
                Vector3 boxMax = AcousticsEditor.TritonToWorld(new Vector3(voxelBBox.MaxCorner.x, voxelBBox.MaxCorner.y, voxelBBox.MaxCorner.z));

                // Get a bounding rectangle for the entire voxel volume
                m_voxelBounds = new Bounds();
                m_voxelBounds.center = m_boxMin;
                m_voxelBounds.size = Vector3.zero;
                m_voxelBounds.Encapsulate(boxMax);

                Vector3Int voxelCount = new Vector3Int(voxels.nx, voxels.nz, voxels.ny); // Unity space has Y and Z swapped from Triton space
                m_clampValues = new BoundsInt(0, 0, 0, voxelCount.x - 1, voxelCount.y - 1, voxelCount.z - 1);

                m_staticValuesCached = true;
            }

            var view = UnityEditor.SceneView.currentDrawingSceneView;
            Camera currentCamera = view ? view.camera : null;
            if (currentCamera == null)
            {
                // Scene view does not have the focus or something else wrong.
                return;
            }

            // Get a bounding rectangle that encloses the viewable area of the camera, up to our maximum render distance.
            // We obtain all four corners of the viewport at two different distances from the camera.
            Bounds clippingBounds = new Bounds();
            clippingBounds.size = Vector3.zero;
            clippingBounds.center = currentCamera.ViewportToWorldPoint(new Vector3(0, 0, currentCamera.nearClipPlane));
            clippingBounds.Encapsulate(currentCamera.ViewportToWorldPoint(new Vector3(0, 1, currentCamera.nearClipPlane)));
            clippingBounds.Encapsulate(currentCamera.ViewportToWorldPoint(new Vector3(1, 0, currentCamera.nearClipPlane)));
            clippingBounds.Encapsulate(currentCamera.ViewportToWorldPoint(new Vector3(1, 1, currentCamera.nearClipPlane)));
            clippingBounds.Encapsulate(currentCamera.ViewportToWorldPoint(new Vector3(0, 0, MaxRenderDistance)));
            clippingBounds.Encapsulate(currentCamera.ViewportToWorldPoint(new Vector3(0, 1, MaxRenderDistance)));
            clippingBounds.Encapsulate(currentCamera.ViewportToWorldPoint(new Vector3(1, 0, MaxRenderDistance)));
            clippingBounds.Encapsulate(currentCamera.ViewportToWorldPoint(new Vector3(1, 1, MaxRenderDistance)));

            // Draw the voxel volume
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(m_voxelBounds.center, m_voxelBounds.size);

            if (!m_voxelBounds.Intersects(clippingBounds))
            {
                // Nothing to display
                return;
            }

            // Convert the visible bounding area to voxel index values
            int xMin = Mathf.FloorToInt((clippingBounds.min.x - m_boxMin.x - m_centerOffset) / m_cellSize);
            int xMax = Mathf.CeilToInt((clippingBounds.max.x - m_boxMin.x) / m_cellSize);
            int yMin = Mathf.FloorToInt((clippingBounds.min.y - m_boxMin.y - m_centerOffset) / m_cellSize);
            int yMax = Mathf.CeilToInt((clippingBounds.max.y - m_boxMin.y) / m_cellSize);
            int zMin = Mathf.FloorToInt((clippingBounds.min.z - m_boxMin.z - m_centerOffset) / m_cellSize);
            int zMax = Mathf.CeilToInt((clippingBounds.max.z - m_boxMin.z) / m_cellSize);

            SwapLimitsIfNeeded(ref xMin, ref xMax);
            SwapLimitsIfNeeded(ref yMin, ref yMax);
            SwapLimitsIfNeeded(ref zMin, ref zMax);

            BoundsInt candidateIndices = new BoundsInt(xMin, yMin, zMin, xMax - xMin, yMax - yMin, zMax - zMin);

            candidateIndices.ClampToBounds(m_clampValues);

            // Pre-calculate values used in the loops below to ensure they are as fast as possible
            Vector3 startVoxelCenter = new Vector3();
            startVoxelCenter.x = QuantizeValue(clippingBounds.min.x, m_boxMin.x, m_cellSize) - m_centerOffset;
            startVoxelCenter.y = QuantizeValue(clippingBounds.min.y, m_boxMin.y, m_cellSize) - m_centerOffset;
            startVoxelCenter.z = QuantizeValue(clippingBounds.min.z, m_boxMin.z, m_cellSize) - m_centerOffset;

            Vector3 curVoxelCenter = startVoxelCenter;
            Vector3 cameraPosition = currentCamera.transform.position;
            Vector3 cameraLookDirection = currentCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0)).direction;
            float viewAngleLimit = Mathf.Cos(RenderViewAngle * Mathf.Deg2Rad);
            float halfCellSize = m_cellSize / 2;

            // These loops have to be super fast because we do them on every paint.
            for (int x = candidateIndices.min.x; x <= candidateIndices.max.x; x++, curVoxelCenter.x += m_cellSize)
            {
                for (int y = candidateIndices.min.y; y <= candidateIndices.max.y; y++, curVoxelCenter.y += m_cellSize)
                {
                    for (int z = candidateIndices.min.z; z <= candidateIndices.max.z; z++, curVoxelCenter.z += m_cellSize)
                    {
                        Vector3 voxelToCamera = curVoxelCenter - cameraPosition;
                        float frustumDotProd = Vector3.Dot(voxelToCamera, cameraLookDirection);

                        // Only draw voxels inside the field of view and distance we care about
                        if (frustumDotProd / voxelToCamera.magnitude > viewAngleLimit && GetVoxelOccupancy(x, y, z)) 
                        {
                            // Only consider the 3 faces visible to camera
                            int dx = (voxelToCamera.x > 0) ? -1 : 1;
                            int dy = (voxelToCamera.y > 0) ? -1 : 1;
                            int dz = (voxelToCamera.z > 0) ? -1 : 1;

                            // For the three front faces, only render if the face is on the
                            // surface -- that is, the voxel next to it is air.
                            if (!GetVoxelOccupancy(x + dx, y, z))
                            {
                                Vector3 faceCenter = new Vector3(curVoxelCenter.x + (dx * halfCellSize), curVoxelCenter.y, curVoxelCenter.z);
                                DrawDebugRectangle(faceCenter, m_cellSize, FaceDirection.X);
                            }

                            if (!GetVoxelOccupancy(x, y + dy, z))
                            {
                                Vector3 faceCenter = new Vector3(curVoxelCenter.x, curVoxelCenter.y + (dy * halfCellSize), curVoxelCenter.z);
                                DrawDebugRectangle(faceCenter, m_cellSize, FaceDirection.Y);
                            }

                            if (!GetVoxelOccupancy(x, y, z + dz))
                            {
                                Vector3 faceCenter = new Vector3(curVoxelCenter.x, curVoxelCenter.y, curVoxelCenter.z + (dz * halfCellSize));
                                DrawDebugRectangle(faceCenter, m_cellSize, FaceDirection.Z);
                            }
                        }
                    }

                    curVoxelCenter.z = startVoxelCenter.z;
                }

                curVoxelCenter.y = startVoxelCenter.y;
            }
        }

        /// <summary>
        /// Given the center of a voxel face and the voxel size, draw the rectangle for that face
        /// </summary>
        /// <param name="faceCenter">Center of the voxel face</param>
        /// <param name="faceSize">Voxel size for the given face</param>
        /// <param name="dir">Which voxel face are we drawing?</param>
        private void DrawDebugRectangle(Vector3 faceCenter, float faceSize, FaceDirection dir)
        {
            Vector3 offset = new Vector3(faceSize * 0.5f, faceSize * 0.5f, faceSize * 0.5f);
            Vector3 minCorner, dv1, dv2;

            switch (dir)
            {
                case FaceDirection.X:
                    offset.x = 0;
                    minCorner = faceCenter - offset;
                    dv1 = new Vector3(0, faceSize, 0);
                    dv2 = new Vector3(0, 0, faceSize);
                    break;
                case FaceDirection.Y:
                    offset.y = 0;
                    minCorner = faceCenter - offset;
                    dv1 = new Vector3(faceSize, 0, 0);
                    dv2 = new Vector3(0, 0, faceSize);
                    break;
                case FaceDirection.Z:
                    offset.z = 0;
                    minCorner = faceCenter - offset;
                    dv1 = new Vector3(faceSize, 0, 0);
                    dv2 = new Vector3(0, faceSize, 0);
                    break;
                default:
                    return;
            }

            Vector3 corner1 = minCorner + dv1;
            Vector3 corner2 = minCorner + dv1 + dv2;
            Vector3 corner3 = minCorner + dv2;

            Gizmos.DrawLine(minCorner, corner1);
            Gizmos.DrawLine(corner1, corner2);
            Gizmos.DrawLine(corner2, corner3);
            Gizmos.DrawLine(corner3, minCorner);
        }

        /// <summary>
        /// Returns whether the specified voxel (by index) is occupied or just air.
        /// </summary>
        private bool GetVoxelOccupancy(int x, int y, int z)
        {
            // Unity has Y and Z swapped from Triton
            return m_previewData.GetVoxelOccupancy(x, z, y);
        }

        /// <summary>
        /// Ensures min is less than max, swapping them if necessary
        /// </summary>
        private void SwapLimitsIfNeeded(ref int min, ref int max)
        {
            if (min > max)
            {
                int tmp = min;
                min = max;
                max = tmp;
            }
        }

        /// <summary>
        /// Given a value and a reference, make sure the value is a quantized distance away from reference.
        /// </summary>
        /// <param name="value">Value that you want to align to quantization steps</param>
        /// <param name="reference">Starting point to measure distance from</param>
        /// <param name="quantizationSize">Quantization size</param>
        /// <returns>A new value that is <paramref name="value"/> modified to be an even number of <paramref name="quantizationSize"/> steps away from <paramref name="reference"/>. 
        /// The input value is moved toward the closest quantization step to calculate the result.</returns>
        private float QuantizeValue(float value, float reference, float quantizationSize)
        {
            return reference + (quantizationSize * Mathf.Round((value - reference) / quantizationSize));
        }

        /// <summary>
        /// Store the preview information for later rendering
        /// </summary>
        /// <param name="results"></param>
        public void SetPreviewData(Triton.SimulationConfig results)
        {
            m_previewData = results;
            m_staticValuesCached = false;
        }

    }
}
