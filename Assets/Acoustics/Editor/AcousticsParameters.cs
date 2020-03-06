// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Collections.Generic;
using UnityEngine;
using System.IO;

#if UNITY_EDITOR
namespace Microsoft.Cloud.Acoustics
{
    public class AcousticsParameters : ScriptableObject, ISerializationCallbackReceiver
    {
        public class AzureAccountFields
        {
            public string BatchAccountName;
            public string BatchAccountUrl;
            public string BatchAccountKey;
            public string StorageAccountName;
            public string StorageAccountKey;
        }

        // Image should take a specific version, will currently bind to the latest
        public const string DefaultTritonImage = "mcr.microsoft.com/acoustics/baketools";
        public const float CoarseSimulationFrequency = 250;
        public const string CoarseSimulationName = "Coarse";
        public const float FineSimulationFrequency = 500;
        public const string FineSimulationName = "Fine";
        public const string DefaultDataFolder = "AcousticsData";

        // Triton Preview/Bake parameters
        public string TritonImage = DefaultTritonImage;
        public float MeshUnitAdjustment = 1;
        public float SceneScale = 1;
        public float SpeedOfSound = -1;
        public float SimulationMaxFrequency = CoarseSimulationFrequency;
        public float ReceiverSampleSpacing = 1.5f;

        public float ProbeHorizontalSpacingMin = 0.5f;
        public float ProbeHorizontalSpacingMax = 3.5f;
        public float ProbeVerticalSpacing = 1.0f;
        public float ProbeMinHeightAboveGround = 0.75f;

        public Vector3 PerProbeSimulationRegion_Small_Lower = new Vector3(-45, -45, -5);
        public Vector3 PerProbeSimulationRegion_Small_Upper = new Vector3(45, 45, 10);

        public Vector3 PerProbeSimulationRegion_Large_Lower = new Vector3(-45, -45, -10);
        public Vector3 PerProbeSimulationRegion_Large_Upper = new Vector3(45, 45, 20);

        /// Azure cloud parameters
        public string[] SupportedAzureVMTypes = new string[] { "Standard_F8s_v2", "Standard_F16s_v2", "Standard_H8", "Standard_H16" };

        public AzureAccountFields AzureAccounts = new AzureAccountFields();

        public int SelectedVMType = 0;
        public int NodeCount = 5;
        public bool UseLowPriorityNodes = false;

        public string AcousticsDataFolder;
        public string DataFileBaseName;
        public string AcousticsDataFolderEditorOnly => System.IO.Path.Combine(AcousticsDataFolder, "Editor");

        public string VoxFilename
        {
            get { return DataFileBaseName + ".vox"; }
        }

        public string VoxFilepath
        {
            get { return Path.Combine(AcousticsDataFolderEditorOnly, VoxFilename); }
        }

        public string ConfigFilename
        {
            get { return DataFileBaseName + "_config.xml"; }
        }

        public string ConfigFilepath
        {
            get { return Path.Combine(AcousticsDataFolderEditorOnly, ConfigFilename); }
        }

        public string ActiveJobID;

        // Materials mapping parameters
      
        // This field is filled in by the below methods from the info in the materials TreeView.
        public List<TritonMaterialsListElement> MaterialsListElements;

        // Not serialized, but used to help serialize materials mapping information.
        internal TritonMaterialsListView ListView { get; set; }

        public void OnBeforeSerialize()
        {
            MaterialsListElements = ListView?.GetData();
        }

        public void OnAfterDeserialize()
        {
            // Set the data in MaterialsListElements back into the control.
            ListView?.SetData(MaterialsListElements);
        }

        public void OnEnable()
        {
            ListView?.Reload();
        }

        public ComputePoolConfiguration GetPoolConfiguration()
        {
            ComputePoolConfiguration poolConfig = new ComputePoolConfiguration
            {
                VirtualMachineSize = SupportedAzureVMTypes[SelectedVMType],
                DedicatedNodes = UseLowPriorityNodes == false ? NodeCount : 0,
                LowPriorityNodes = UseLowPriorityNodes ? NodeCount : 0
            };
            return poolConfig;
        }
    }
}
#endif
