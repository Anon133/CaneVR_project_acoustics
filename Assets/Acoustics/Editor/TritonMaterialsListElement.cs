// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Microsoft.Cloud.Acoustics
{
    [Serializable]
    public class TritonMaterialsListElement : TreeViewItem
    {
        [SerializeField] string m_name;
        [SerializeField] float m_absorptivity;
        [SerializeField] long m_materialCode;

        /// <summary>
        /// Material name. This is the name of the material in the Unity scene.
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Absorptivity value. Used only for custom materials.
        /// </summary>
        public float Absorptivity
        {
            get { return m_absorptivity; }
            set { m_absorptivity = value; }
        }

        /// <summary>
        /// Material code of the mapped material. This is obtained from the Triton.MaterialLibrary class.
        /// </summary>
        public long MaterialCode
        {
            get { return m_materialCode; }
            set { m_materialCode = value; }
        }

        public TritonMaterialsListElement()
        {
        }

        public TritonMaterialsListElement(string name)
        {
            m_name = name;
            m_absorptivity = Triton.MaterialLibrary.DefaultWallAbsorption;
            m_materialCode = Triton.MaterialLibrary.DefaultWallCode;
        }
    }
}



