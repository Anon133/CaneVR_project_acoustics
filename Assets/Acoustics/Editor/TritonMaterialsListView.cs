// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Assertions;

namespace Microsoft.Cloud.Acoustics
{
    internal class TritonMaterialsListView : TreeView
    {
        const float RowHeights = 20;

        // All columns
        enum ListViewColumnNames
        {
            Material,
            AcousticMatch,
            Absorptivity,
        }

        private struct MaterialInfoWithCode
        {
            public string Name;
            public long MaterialCode;
            public float Absorptivity;

            public MaterialInfoWithCode(string name, long code, float absorptivity)
            {
                Name = name;
                MaterialCode = code;
                Absorptivity = absorptivity;
            }
        };

        List<MaterialInfoWithCode> m_knownMaterials;
        Triton.MaterialLibrary m_library;

        // Set to the current item being edited via the popup menu. Null otherwise.
        bool m_dirty = false;
        bool m_filterUnmarked = true;
        bool m_sortKnownMaterialsByAbsorption = false;

        List<TritonMaterialsListElement> m_cachedData;

        /// <summary>
        /// Called when the user changes any of the fields in the list.
        /// </summary>
        public event Action OnDataChanged;

        public TritonMaterialsListView(TreeViewState state, MultiColumnHeader multicolumnHeader, Triton.MaterialLibrary materialLibrary, List<TritonMaterialsListElement> seedData)
            : base(state, multicolumnHeader)
        {
            // Custom setup
            rowHeight = RowHeights;
            showAlternatingRowBackgrounds = true;
            showBorder = true;

            m_library = materialLibrary;
            m_dirty = false;

            UpdateKnownMaterials();

            m_cachedData = seedData;

            Reload();
        }

        public Triton.MaterialLibrary MaterialLibrary
        {
            get { return m_library; }
        }

        public void SetData(List<TritonMaterialsListElement> data)
        {
            m_cachedData = data;
            m_dirty = false;
        }

        public List<TritonMaterialsListElement> GetData()
        {
            return m_cachedData;
        }

        public void SetDirty()
        {
            OnDataChanged?.Invoke();

            m_dirty = true;
        }

        public bool FilterUnmarked
        {
            get
            {
                return m_filterUnmarked;
            }

            set
            {
                if (value != m_filterUnmarked)
                {
                    m_filterUnmarked = value;
                    Reload();
                }
            }
        }

        public bool SortKnownMaterialsByAbsorption
        {
            get
            {
                return m_sortKnownMaterialsByAbsorption;
            }

            set
            {
                if (value != m_sortKnownMaterialsByAbsorption)
                {
                    m_sortKnownMaterialsByAbsorption = value;
                    UpdateKnownMaterials();
                    Reload();
                }
            }
        }

        private void UpdateKnownMaterials()
        {
            long[] materialCodes;
            Triton.AcousticMaterial[] knownMaterialsInfos;
            var count = m_library.GetKnownMaterials(out knownMaterialsInfos, out materialCodes);
            m_knownMaterials = new List<MaterialInfoWithCode>(count);
            Triton.AcousticMaterial defaultMaterialInfo = m_library.GetMaterialInfo(Triton.MaterialLibrary.DefaultWallCode);

            for (int i = 0; i < count; i++)
            {
                m_knownMaterials.Add(new MaterialInfoWithCode(knownMaterialsInfos[i].Name, materialCodes[i], knownMaterialsInfos[i].Absorptivity));
            }

            if (m_sortKnownMaterialsByAbsorption)
            {
                // Sort by absorption value then name
                m_knownMaterials.Sort((left, right) => 
                {
                    int aComp = left.Absorptivity.CompareTo(right.Absorptivity);
                    if (aComp == 0)
                    {
                        return left.Name.CompareTo(right.Name);
                    }

                    return aComp;
                });
            }
            else
            {
                // Sort by name then absorption value
                m_knownMaterials.Sort((left, right) => 
                {
                    int nComp = left.Name.CompareTo(right.Name);
                    if (nComp == 0)
                    {
                        return left.Absorptivity.CompareTo(right.Absorptivity);
                    }

                    return nComp;
                });
            }

            // Now that we're sorted, put Default and Custom at the beginning.
            m_knownMaterials.Insert(0, new MaterialInfoWithCode("Default", Triton.MaterialLibrary.DefaultWallCode, defaultMaterialInfo.Absorptivity));
            m_knownMaterials.Insert(1, new MaterialInfoWithCode("Custom", Triton.MaterialLibrary.Reserved0, defaultMaterialInfo.Absorptivity));
        }

        protected override TreeViewItem BuildRoot()
        {
            List<TritonMaterialsListElement> newCache = new List<TritonMaterialsListElement>();

            TritonMaterialsListElement root = new TritonMaterialsListElement("root")
            {
                depth = -1,
                id = 0
            };

            int itemID = 1;

            List<string> addedNames = new List<string>();

            Renderer[] renderers = GameObject.FindObjectsOfType<Renderer>(); // TODO - Do we also need PhysicMaterial? Maybe others?

            // If we are rebuilding the list (during a refresh or deserialization) we don't want to throw away what we knew before.
            // Keep the list that we had previously so we can match up those entries to the current list of materials.
            List<TritonMaterialsListElement> oldCache;

            if (m_cachedData != null)
            {
                oldCache = m_cachedData;
                m_cachedData = null;
            }
            else
            {
                oldCache = new List<TritonMaterialsListElement>();
            }

            foreach (Renderer r in renderers)
            {
                // Why is HideFlags not marked as a bitmask-able enum?
                HideFlags hiddenFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                if ((r.hideFlags & hiddenFlags) != 0)
                {
                    continue;
                }

                if (m_filterUnmarked && r.GetComponent<AcousticsGeometry>() == null)
                {
                    continue;
                }

                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || addedNames.Contains(m.name))
                    {
                        // We've already added this material, or an empty material.
                        continue;
                    }

                    addedNames.Add(m.name);

                    // If the same material name existed in our previous list, then use that same entry.
                    TritonMaterialsListElement item = oldCache.Find(i => String.Equals(i.Name, m.name, StringComparison.InvariantCultureIgnoreCase));

                    if (item != null)
                    {
                        // Make sure the material code is valid
                        try
                        {
                            // Throws an exception if not found. Ignore result.
                            m_library.GetMaterialInfo(item.MaterialCode);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            // Just pretend we never knew we had a match.
                            item = null;
                        }
                    }

                    if (item == null)
                    {
                        long materialCode = m_library.GuessMaterialCodeFromGeneralName(m.name);

                        item = new TritonMaterialsListElement(m.name)
                        {
                            parent = root,
                            depth = 1,
                            MaterialCode = materialCode
                        };
                    }
 
                    // ID needs to be unique.
                    item.id = item.Name.GetHashCode() + itemID++;

                    newCache.Add(item);
                }
            }

            // Sort entries by name
            newCache.Sort((left, right) => left.Name.CompareTo(right.Name));

            root.children = newCache.ConvertAll<TreeViewItem>(i => i as TreeViewItem);
            m_cachedData = newCache;

            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            TritonMaterialsListElement item = (TritonMaterialsListElement)args.item;

            for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
            {
                CellGUI(args.GetCellRect(i), item, (ListViewColumnNames)args.GetColumn(i), ref args);
            }
        }

        /// <summary>
        /// Updates our cache whenever changes are made in the UI.
        /// </summary>
        protected override void AfterRowsGUI()
        {
            if (m_dirty)
            {
                IList<TreeViewItem> rows = GetRows();

                if (m_cachedData == null)
                {
                    m_cachedData = new List<TritonMaterialsListElement>();
                }
                else
                {
                    m_cachedData.Clear();
                }

                if (rows != null)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        m_cachedData.Add(rows[i] as TritonMaterialsListElement);
                    }
                }

                m_dirty = false;
            }

            base.AfterRowsGUI();
        }

        void CellGUI(Rect cellRect, TritonMaterialsListElement item, ListViewColumnNames column, ref RowGUIArgs args)
        {
            // Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
            CenterRectUsingSingleLineHeight(ref cellRect);

            switch (column)
            {
                case ListViewColumnNames.Material:
                {
                    DefaultGUI.Label(cellRect, item.Name, args.selected, args.focused);
                    args.rowRect = cellRect;
                    base.RowGUI(args);
                }
                break;

                case ListViewColumnNames.AcousticMatch:
                {
                    long materialCode = item.MaterialCode;
                    int popupIndex = m_knownMaterials.FindIndex(m => m.MaterialCode == materialCode); // Default and Custom entries do not require special handling.

                    if (popupIndex == -1)
                    {
                        // The only time we might hit this is if the list of materials changes.
                        Debug.Log("ERROR: Item material code not found in the list of known materials!");
                        popupIndex = 0;
                    }

                    if (EditorGUI.DropdownButton(cellRect, new GUIContent(m_knownMaterials[popupIndex].Name), FocusType.Keyboard))
                    {
                        GenericMenu popupMenu = new GenericMenu();

                        for (int i = 0; i < m_knownMaterials.Count; i++)
                        {
                            MaterialInfoWithCode m = m_knownMaterials[i];

                            popupMenu.AddItem(new GUIContent($"{m.Name}\t{m.Absorptivity:F2}"), (m.MaterialCode == materialCode), (index) => {
                                item.MaterialCode = m_knownMaterials[(int)index].MaterialCode;
                                SetDirty();
                            }, i);

                            if (i == 1)
                            {
                                // Put a separator after the Default and Custom entries.
                                popupMenu.AddSeparator("");
                            }
                        }

                        popupMenu.DropDown(GUILayoutUtility.GetLastRect());
                    }
                }
                break;

                case ListViewColumnNames.Absorptivity:
                {
                    bool itemIsCustomMaterial = (item.MaterialCode == Triton.MaterialLibrary.Reserved0);

                    EditorGUI.BeginDisabledGroup(!itemIsCustomMaterial);
                    if (itemIsCustomMaterial)
                    {
                        float oldValue = item.Absorptivity;
                        item.Absorptivity = EditorGUI.Slider(cellRect, item.Absorptivity, 0, 1);

                        if (item.Absorptivity != oldValue)
                        {
                            SetDirty();
                        }
                    }
                    else
                    {
                        MaterialInfoWithCode materialInfo = m_knownMaterials.Find(m => m.MaterialCode == item.MaterialCode);

                        EditorGUI.Slider(cellRect, materialInfo.Absorptivity, 0, 1);
                    }
                    EditorGUI.EndDisabledGroup();
                }
                break;
            }
        }

        // Misc
        //--------
        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count > 0)
            {
                List<TritonMaterialsListElement> items = m_cachedData.FindAll(i => selectedIds.Contains(i.id));
                List<UnityEngine.Object> objectsToSelect = new List<UnityEngine.Object>();

                foreach (TritonMaterialsListElement item in items)
                {
                    // This is a brute-force technique. If not performant enough on large maps a
                    // cache of item IDs to game objects could be maintained by BuildRoot() that we
                    // use here instead of searching for the materials each time.
                    Renderer[] renderers = GameObject.FindObjectsOfType<Renderer>(); // TODO - Do we also need PhysicMaterial? Maybe others?

                    foreach (Renderer r in renderers)
                    {
                        HideFlags hiddenFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                        if ((r.hideFlags & hiddenFlags) != 0)
                        {
                            continue;
                        }

                        foreach (Material m in r.sharedMaterials)
                        {
                            if (m != null && m.name == item.Name)
                            {
                                objectsToSelect.Add(r.gameObject);
                                break;
                            }
                        }
                    }
                }

                if (objectsToSelect.Count > 0)
                {
                    Selection.objects = objectsToSelect.ToArray();
                }
            }

            base.SelectionChanged(selectedIds);
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState(float treeViewWidth)
        {
            MultiColumnHeaderState.Column[] columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Scene Material", "Material used in the current scene"),
                    contextMenuText = "Asset",
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 150,
                    minWidth = 80,
                    maxWidth = 300,
                    autoResize = true,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Acoustics", "Materials with known acoustic absorption data. Change sort order of dropdown menu using radio buttons above."),
                    contextMenuText = "Type",
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 80,
                    minWidth = 80,
                    maxWidth = 300,
                    autoResize = true,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Absorption Coeff.", "Absorption coefficient (range 0 to 1: 0 = reflective, 1 = absorbent)"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = false,
                    sortingArrowAlignment = TextAlignment.Center,
                    width = 125,
                    minWidth = 125,
                    autoResize = false,
                    allowToggleVisibility = false
                }
            };

            Assert.AreEqual(columns.Length, Enum.GetValues(typeof(ListViewColumnNames)).Length, "Number of columns should match number of enum values: You probably forgot to update one of them.");

            MultiColumnHeaderState state = new MultiColumnHeaderState(columns);
            return state;
        }
    }
}
