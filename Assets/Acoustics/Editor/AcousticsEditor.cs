// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Threading;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Microsoft.Cloud.Acoustics
{
    public class AcousticsEditor : EditorWindow, IHasCustomMenu
    {
        private enum SelectedTab
        {
            ObjectTab,
            MaterialsTab,
            ProbesTab,
            BakeTab
        };

        private enum SelectedSceneFilter
        {
            All = 0,
            MeshRenderers = 1,
            Terrains = 2,
            Geometry = 3,
            Navigation = 4
        };

        private enum AcousticMaterialSortOrder
        {
            ByName = 0,
            ByAbsorptivity = 1
        };

        private struct CalcProbeParams
        {
            public Triton.AcousticMesh mesh;
            public Triton.SimulationParameters simParams;
            public Triton.JobOperationalParams opParams;
            public Triton.MaterialLibrary matlib;
        };

        const string AzurePortalUrl = "https://portal.azure.com";
        const string AzureAccountInfoKeyName = "AcousticsAzureAccounts";

        const string UntitledString = "Untitled";
        const string DefaultAcousticParameterBasePath = "Assets/Editor";
        const string AcousticParametersSuffix = "_AcousticParameters";

        [SerializeField]
        static AcousticsParameters s_AcousticsParameters;

        SelectedTab m_currentTab = SelectedTab.ObjectTab;

        static bool s_staticInitialized = false;
        bool m_initialized = false;

        bool m_bakeCredsFoldoutOpen = true;

        SelectedSceneFilter m_currentSceneFilter = SelectedSceneFilter.All;
        AcousticMaterialSortOrder m_currentMaterialSortOrder = AcousticMaterialSortOrder.ByName;

        GUIStyle m_leftStyle;
        GUIStyle m_rightStyle;
        GUIStyle m_midStyle;
        GUIStyle m_leftSelected;
        GUIStyle m_rightSelected;
        GUIStyle m_midSelected;

        TritonMaterialsListView m_listView;

        System.Threading.Thread m_workerThread;

        // Used to let our worker thread run an action on the main thread.
        List<Action> m_queuedActions = new List<Action>();

        // Values related to doing probe location preview
        Triton.SimulationConfig m_previewResults;
        GameObject m_previewRootObject;
        TimeSpan m_estimatedTotalComputeTime = TimeSpan.Zero;
        string m_computeTimeCostSheet = null;
        bool m_previewCancelRequested = false;
        string m_progressMessage;
        int m_progressValue = 0;

        // Mesh conversion constants.
        static Matrix4x4 s_tritonToWorld;
        static Matrix4x4 s_worldToTriton;

        // Fields related to cloud azure service
        const double AzureBakeCheckInterval = 30; // Interval in seconds to check status on cloud bake
        string m_cloudJobStatus;
        DateTime m_cloudLastUpdateTime;
        double m_timerStartValue;
        bool m_cloudJobDeleting = false;

        // State related fields.
        [SerializeField]
        TreeViewState m_listViewState;
        [SerializeField]
        MultiColumnHeaderState m_multiColumnHeaderState;

        /// <summary>
        /// Top level menu item to bring up our UI
        /// </summary>
        [MenuItem("Window/Acoustics")]
        public static void ShowWindow()
        {
            EditorWindow win = EditorWindow.GetWindow(typeof(AcousticsEditor));
            win.titleContent = new GUIContent("Acoustics", "Configuration of Triton Environmental Acoustics");
            win.Show();
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("About Project Acoustics..."), false, () => {
                AcousticsAbout aboutWindow = ScriptableObject.CreateInstance<AcousticsAbout>();
                aboutWindow.position = new Rect(Screen.width / 2, Screen.height / 2, 1024, 768);
                aboutWindow.titleContent = new GUIContent("About Project Acoustics");
                aboutWindow.ShowUtility();
            });
        }

        /// <summary>
        /// Repaint our window if the hierarchy changes and we're showing the object window, which displays a count of marked objects.
        /// This is primarily to catch the case of someone adding the AcousticsGeometry or AcousticsNavigation components to an object, but would also apply to
        /// adding a prefab that has AcousticsGeometry objects, and so on.
        /// </summary>
        void OnHierarchyChange()
        {
            bool doRepaint = false;

            if (m_currentTab == SelectedTab.ObjectTab)
            {
                doRepaint = true;
            }

            if (m_listView != null && m_currentTab == SelectedTab.MaterialsTab)
            {
                m_listView.Reload();
                doRepaint = true;
            }

            if (doRepaint)
            {
                Repaint();
            }
        }

        /// <summary>
        /// Called whenever the selection changes in the scene.
        /// </summary>
        private void OnSelectionChange()
        {
            if (m_currentTab != SelectedTab.BakeTab)
            {
                Repaint();
            }
        }

        /// <summary>
        /// Called automatically several times a second by Unity
        /// </summary>
        private void Update()
        {
            List<Action> actionsToPerform = new List<Action>();

            lock (m_queuedActions)
            {
                actionsToPerform.AddRange(m_queuedActions);
                m_queuedActions.Clear();
            }

            foreach (Action actionToPerform in actionsToPerform)
            {
                actionToPerform?.Invoke();
            }

            if (m_timerStartValue != 0 && EditorApplication.timeSinceStartup > m_timerStartValue + AzureBakeCheckInterval)
            {
                m_timerStartValue = EditorApplication.timeSinceStartup; // Must be done before call below
                StartAzureStatusCheck();
            }
        }

        /// <summary>
        /// Callback that tells us when the active scene has changed. 
        /// We want to reload the acoustic parameters that belong to the active scene.
        /// </summary>
        /// <param name="state"></param>
        private void SceneChanged(Scene current, Scene next)
        {
            // Save current acoustic parameters before we change
            AssetDatabase.SaveAssets();

            // Remove all preview elements when switching scenes
            CleanupPreviewData(false);

            LoadAcousticParameters();
            AssetDatabase.Refresh();

            // Force a refresh of our data
            m_initialized = false;
            Repaint();
        }

        /// <summary>
        /// Callback that tells us when a scene has been saved (any scene, not necessarily the active one)
        /// We want to reload the acoustic parameters if an untitled scene was renamed
        /// </summary>
        /// <param name="scene"></param>
        private void SceneSaved(Scene scene)
        {
            // We don't care about scenes that aren't the active one
            if (scene != SceneManager.GetActiveScene())
            {
                return;
            }

            // Save current acoustic parameters before we change anything
            AssetDatabase.SaveAssets();

            // We are trying to catch the case of an untitled scene being renamed
            // This also has the side effect of creating new acoustic parameters for any scene that was renamed
            string newSceneAssetName = CreateAcousticParameterAssetString(scene.name);
            if (newSceneAssetName != s_AcousticsParameters.name)
            {
                LoadAcousticParameters();
                AssetDatabase.Refresh();

                // Force a refresh of our data
                m_initialized = false;
                Repaint();
            }
        }

        /// <summary>
        /// Called when the Window is opened / loaded, and when entering/exiting play mode.
        /// </summary>
        private void OnEnable()
        {
            m_cloudLastUpdateTime = DateTime.Now;

            InitializeStatic(); // Normally we would do this in Awake, but there are a few scenarios where we don't get an Awake call.
                                // Can't call Initialize() here because the window is not yet visible, and properties such as its size are not yet available.

            EditorApplication.playModeStateChanged += OnPlayStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode += SceneChanged;
            EditorSceneManager.sceneSaved += SceneSaved;
        }

        /// <summary>
        /// Called when the window is closed / unloaded, and when entering/exiting play mode.
        /// </summary>
        private void OnDisable()
        {
            AssetDatabase.SaveAssets();

            // The probe point preview is not preserved when we're disabled, so we need to force a reload when we are reactivated.
            CleanupPreviewData(false);
            m_initialized = false;

            EditorApplication.playModeStateChanged -= OnPlayStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= SceneChanged;
            EditorSceneManager.sceneSaved -= SceneSaved;
        }

        /// <summary>
        /// Callback that tells us when we enter and exit Play or Edit mode. We use this to ensure we are properly initialized
        /// when re-entering Edit mode after Play mode.
        /// </summary>
        /// <param name="state"></param>
        void OnPlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                // We don't get a call to update anything in this case. Force a refresh of our data.
                m_initialized = false;
                Repaint();
            }
            else if (state == PlayModeStateChange.ExitingEditMode)
            {
                CleanupPreviewData(false);
            }
        }

        /// <summary>
        /// Called to render our UI
        /// </summary>
        void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // We don't do anything in play mode
                GUILayout.Space(30);
                EditorGUILayout.LabelField("Acoustics editor disabled in Play Mode.", EditorStyles.boldLabel);
                return;
            }

            EditorGUIUtility.wideMode = true;

            Initialize();

            RenderTabButtons();

            switch (m_currentTab)
            {
                case SelectedTab.ObjectTab:
                    RenderObjectsTab();
                    break;

                case SelectedTab.MaterialsTab:
                    RenderMaterialsTab();
                    break;

                case SelectedTab.ProbesTab:
                    RenderProbesTab();
                    break;

                case SelectedTab.BakeTab:
                    RenderBakeTab();
                    break;
            }
        }

        static void InitializeStatic()
        {
            if (!s_staticInitialized)
            {
                // Save off the transforms once. This is converting from unity's default space to Maya Z+
                Matrix4x4 unityToMayaZ = new Matrix4x4();
                unityToMayaZ.SetRow(0, new Vector4(1, 0, 0, 0));
                unityToMayaZ.SetRow(1, new Vector4(0, 0, 1, 0));
                unityToMayaZ.SetRow(2, new Vector4(0, 1, 0, 0));
                unityToMayaZ.SetRow(3, new Vector4(0, 0, 0, 1));

                s_worldToTriton = unityToMayaZ;
                s_tritonToWorld = Matrix4x4.Inverse(s_worldToTriton);

                if (s_AcousticsParameters == null)
                {
                    LoadAcousticParameters();
                }

                s_staticInitialized = true;
            }
        }

        void Initialize()
        {
            if (!m_initialized)
            {
                if (String.IsNullOrWhiteSpace(s_AcousticsParameters.DataFileBaseName))
                {
                    s_AcousticsParameters.DataFileBaseName = "Acoustics_" + SceneManager.GetActiveScene().name;
                }

                if (String.IsNullOrWhiteSpace(s_AcousticsParameters.AcousticsDataFolder))
                {
                    s_AcousticsParameters.AcousticsDataFolder = Path.Combine("Assets", AcousticsParameters.DefaultDataFolder);
                }

                if (!Directory.Exists(s_AcousticsParameters.AcousticsDataFolder))
                {
                    Directory.CreateDirectory(s_AcousticsParameters.AcousticsDataFolder);
                }

                // We also put data in an Editor subfolder under the AcousticsDataFolder so unnecessary data doesn't get packaged with the game
                if (!Directory.Exists(s_AcousticsParameters.AcousticsDataFolderEditorOnly))
                {
                    Directory.CreateDirectory(s_AcousticsParameters.AcousticsDataFolderEditorOnly);
                }

                // Pre-compute the various styles we will be using.
                m_leftStyle = new GUIStyle(EditorStyles.miniButtonLeft)
                {
                    fontSize = 11
                };
                m_rightStyle = new GUIStyle(EditorStyles.miniButtonRight)
                {
                    fontSize = 11
                };
                m_midStyle = new GUIStyle(EditorStyles.miniButtonMid)
                {
                    fontSize = 11
                };
                // For some reason the different miniButton styles don't have the same values for top margin.
                m_leftStyle.margin.top = 0;
                m_midStyle.margin.top = 0;
                m_rightStyle.margin.top = 0;

                m_leftSelected = new GUIStyle(m_leftStyle)
                {
                    normal = m_leftStyle.active,
                    onNormal = m_leftStyle.active
                };
                m_midSelected = new GUIStyle(m_midStyle)
                {
                    normal = m_midStyle.active,
                    onNormal = m_midStyle.active
                };
                m_rightSelected = new GUIStyle(m_rightStyle)
                {
                    normal = m_rightStyle.active,
                    onNormal = m_rightStyle.active
                };
                m_leftSelected.normal.textColor = Color.white;
                m_midSelected.normal.textColor = Color.white;
                m_rightSelected.normal.textColor = Color.white;

                if (m_listView == null)
                {
                    MonoScript thisScript = MonoScript.FromScriptableObject(this);
                    string pathToThisScript = Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript));
                    string unityRootPath = Path.GetDirectoryName(Application.dataPath);

                    string materialsPropertiesFile = Path.Combine(unityRootPath, pathToThisScript, @"DefaultMaterialProperties.json");
                    materialsPropertiesFile = Path.GetFullPath(materialsPropertiesFile); // Normalize the path

                    Triton.MaterialLibrary materialLibrary = Triton.MaterialLibrary.Create(materialsPropertiesFile);

                    if (materialLibrary == null)
                    {
                        Debug.Log(String.Format("Failed to load default material properties from {0}!", materialsPropertiesFile));
                        return;
                    }
                    else
                    {
                        Debug.Log(String.Format("Default materials loaded. Number of entries in material library: {0}", materialLibrary.NumMaterials));
                    }

                    if (m_listViewState == null)
                    {
                        m_listViewState = new TreeViewState();
                    }

                    MultiColumnHeaderState columnHeaderState = TritonMaterialsListView.CreateDefaultMultiColumnHeaderState(EditorGUIUtility.currentViewWidth);

                    bool firstInit = m_multiColumnHeaderState == null;

                    if (MultiColumnHeaderState.CanOverwriteSerializedFields(m_multiColumnHeaderState, columnHeaderState))
                    {
                        MultiColumnHeaderState.OverwriteSerializedFields(m_multiColumnHeaderState, columnHeaderState);
                    }

                    m_multiColumnHeaderState = columnHeaderState;

                    MultiColumnHeader multiColumnHeader = new MultiColumnHeader(columnHeaderState);
                    if (firstInit)
                    {
                        multiColumnHeader.ResizeToFit();
                        multiColumnHeader.canSort = false;
                        multiColumnHeader.height = MultiColumnHeader.DefaultGUI.minimumHeight;
                    }

                    m_listView = new TritonMaterialsListView(m_listViewState, multiColumnHeader, materialLibrary, s_AcousticsParameters.MaterialsListElements);

                    m_listView.OnDataChanged += MarkParametersDirty;
                    s_AcousticsParameters.ListView = m_listView;
                }

                // Attempt to load Azure account info from the registry
                string azureCreds = EditorPrefs.GetString(AzureAccountInfoKeyName);

                if (!String.IsNullOrEmpty(azureCreds))
                {
                    // decrypt creds
                    byte[] azureCredsBytes = Convert.FromBase64String(azureCreds);
                    byte[] azureCredsBytesUnprotected = System.Security.Cryptography.ProtectedData.Unprotect(azureCredsBytes, null, DataProtectionScope.CurrentUser);

                    string azureCredsUnprotected = System.Text.Encoding.Unicode.GetString(azureCredsBytesUnprotected);
                    EditorJsonUtility.FromJsonOverwrite(azureCredsUnprotected, s_AcousticsParameters.AzureAccounts);

                    // We may have stored empty creds. Only fold if something is there.
                    if (!String.IsNullOrEmpty(s_AcousticsParameters.AzureAccounts.BatchAccountName))
                    {
                        m_bakeCredsFoldoutOpen = false;
                    }
                }

                // If a preview exists, load it.
                if (File.Exists(Path.Combine(s_AcousticsParameters.AcousticsDataFolderEditorOnly, s_AcousticsParameters.DataFileBaseName + "_config.xml")))
                {
                    try
                    {
                        m_previewResults = Triton.SimulationConfig.Deserialize(s_AcousticsParameters.AcousticsDataFolderEditorOnly, s_AcousticsParameters.DataFileBaseName + @"_config.xml");
                        DisplayPreviewResults();
                    }
                    catch (Exception ex)
                    {
                        // Ignore.
                        Debug.Log($"Attempt to load preview data failed, so ignoring. {ex.Message}");
                    }
                }
                else
                {
                    // Otherwise, remove it
                    m_previewResults = null;
                }

                if (!String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
                {
                    m_cloudJobStatus = "Checking status...";
                    StartAzureCheckTimer();
                    StartAzureStatusCheck();
                }
                else
                {
                    m_cloudJobStatus = "";
                }

                m_initialized = true;
            }
        }

        /// <summary>
        /// Queue a single action for execution on the UI thread
        /// </summary>
        /// <param name="a">Action that should be queued</param>
        void QueueUIThreadAction(Action a)
        {
            lock (m_queuedActions)
            {
                m_queuedActions.Add(a);
            }
        }

        /// <summary>
        /// Queue two actions for execution on the UI thread. Use this vs. calling QueueAction(a) twice.
        /// </summary>
        /// <param name="a1">First action to queue</param>
        /// <param name="a2">Second action to queue</param>
        void QueueUIThreadAction (Action a1, Action a2)
        {
            lock (m_queuedActions)
            {
                m_queuedActions.Add(a1);
                m_queuedActions.Add(a2);
            }
        }

        static void LoadAcousticParameters()
        {
            string acousticParametersAssetpath = GetAcousticsParametersAssetPath();

            // See if there already are saved acoustic parameters for the active scene
            s_AcousticsParameters = AssetDatabase.LoadAssetAtPath(acousticParametersAssetpath, typeof(AcousticsParameters)) as AcousticsParameters;

            // Create new one if it doesn't already exist
            if (s_AcousticsParameters == null)
            {
                s_AcousticsParameters = CreateInstance<AcousticsParameters>();

                AssetDatabase.CreateAsset(s_AcousticsParameters, acousticParametersAssetpath);
                AssetDatabase.SaveAssets();
            }
        }

        private static string CreateAcousticParameterAssetString(string sceneName)
        {
            return sceneName + AcousticParametersSuffix;
        }

        /// <summary>
        /// Returns the path for the saved AcousticParameters asset file for the active scene
        /// </summary>
        /// <returns></returns>
        static string GetAcousticsParametersAssetPath()
        {
            string sceneBaseName = UntitledString;
            string basePath = DefaultAcousticParameterBasePath;

            string scenePath = SceneManager.GetActiveScene().path;
            if (!String.IsNullOrEmpty(scenePath))
            {
                sceneBaseName = Path.GetFileNameWithoutExtension(scenePath);
                basePath = Path.Combine(Path.GetDirectoryName(scenePath), "Editor");
            }

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            return Path.Combine(basePath, CreateAcousticParameterAssetString(sceneBaseName) + ".asset");
        }

        /// <summary>
        /// Renders the tab selection buttons across the top of our UI.
        /// </summary>
        void RenderTabButtons()
        {
            const float buttonWidth = 65;
            const float buttonHeight = 20;
            const float topBottomMargin = 10;
            const float buttonCount = 4;

            float width = EditorGUIUtility.currentViewWidth;
            float offset = buttonWidth * (buttonCount / 2);
            float center = width / 2;

            // BeginArea doesn't actually affect the layout, so allocate the space we want in the layout
            GUILayout.Space(buttonHeight + (topBottomMargin * 3));

            GUILayout.BeginArea(new Rect(center - offset, topBottomMargin, buttonWidth * buttonCount, buttonHeight + topBottomMargin));

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("Objects", "Step One: Mark Objects"), (m_currentTab == SelectedTab.ObjectTab) ? m_leftSelected : m_leftStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.ObjectTab;
            }

            if (GUILayout.Button(new GUIContent("Materials", "Step Two: Assign Materials"), (m_currentTab == SelectedTab.MaterialsTab) ? m_midSelected : m_midStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.MaterialsTab;
                if (m_listView != null)
                {
                    m_listView.Reload();
                }
            }

            if (GUILayout.Button(new GUIContent("Probes", "Step Three: Calculate Probes"), (m_currentTab == SelectedTab.ProbesTab) ? m_midSelected : m_midStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.ProbesTab;
            }

            if (GUILayout.Button(new GUIContent("Bake", "Step Four: Bake in the cloud"), (m_currentTab == SelectedTab.BakeTab) ? m_rightSelected : m_rightStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.BakeTab;
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        void RenderObjectsTab()
        {
            if (m_previewRootObject != null)
            {
                GUILayout.Label("Clear the preview on the Probes tab to make changes.", EditorStyles.boldLabel);
                GUILayout.Space(20);
            }

            using (new EditorGUI.DisabledScope(m_previewRootObject != null))
            {
                GUILayout.Label("Step One", EditorStyles.boldLabel);
                GUILayout.Label("Add the AcousticsGeometry or AcousticsNavigation components to objects using the options below.", EditorStyles.wordWrappedLabel);

                GUILayout.Space(10);
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                GUILayout.Space(10);

                GUILayout.Label("Scene Filter:");
                string[] optionList = new string[] { "All Objects  ", "Mesh Renderers  ", "Terrains  ", "Acoustics Geometry  ", "Acoustics Navigation  " }; // Extra spaces are for better UI layout

                SelectedSceneFilter oldFilter = m_currentSceneFilter;
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                m_currentSceneFilter = (SelectedSceneFilter)GUILayout.SelectionGrid((int)m_currentSceneFilter, optionList, 2, EditorStyles.radioButton);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                string[] filterTextOptions = { "All {0}", "{0} MeshRenderer", "{0} Terrain", "{0} Acoustics Geometry", "{0} Acoustics Navigation" };
                string filterText = filterTextOptions[(int)m_currentSceneFilter];

                if (m_currentSceneFilter != oldFilter)
                {
                    switch (m_currentSceneFilter)
                    {
                        case SelectedSceneFilter.All:
                            SetSearchFilter("", HierarchyFilterMode.All);
                            break;

                        case SelectedSceneFilter.MeshRenderers:
                            SetSearchFilter("MeshRenderer", HierarchyFilterMode.Type);
                            break;

                        case SelectedSceneFilter.Terrains:
                            SetSearchFilter("Terrain", HierarchyFilterMode.Type);
                            break;

                        case SelectedSceneFilter.Geometry:
                            SetSearchFilter("AcousticsGeometry", HierarchyFilterMode.Type);
                            break;

                        case SelectedSceneFilter.Navigation:
                            SetSearchFilter("AcousticsNavigation", HierarchyFilterMode.Type);
                            break;
                    }
                }

                GUILayout.Space(10);
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                GUILayout.Space(10);

                // Get only the MeshRenderer and Terrain components from the selection
                Transform[] currentSelection;
                bool selectionFilter = false;

                if (Selection.objects.Length > 0)
                {
                    currentSelection = Selection.GetTransforms(SelectionMode.Deep | SelectionMode.Editable);
                    selectionFilter = true;
                }
                else
                {
                    currentSelection = GameObject.FindObjectsOfType<Transform>();
                    selectionFilter = false;
                }

                int countWithIgnored = currentSelection.Length;
                currentSelection = currentSelection.Where(t => t.gameObject.activeInHierarchy == true && (t.GetComponent<MeshRenderer>() != null || t.GetComponent<Terrain>() != null)).ToArray();
                int countAllItems = currentSelection.Length;

                // Count up all the different things we're interested in about the current selection
                int countMeshes = 0;
                int countTerrains = 0;
                int countGeometry = 0;
                int countNav = 0;
                int countGeometryUnmarked = 0;
                int countNavigationUnmarked = 0;

                foreach (Transform t in currentSelection) // All items here are either a mesh or terrain
                {
                    bool isMesh = (t.GetComponent<MeshRenderer>() != null);
                    bool isTerrain = (t.GetComponent<Terrain>() != null);
                    bool isGeometry = (t.GetComponent<AcousticsGeometry>() != null);
                    bool isNav = (t.GetComponent<AcousticsNavigation>() != null);

                    if (isMesh)
                    {
                        countMeshes++;
                    }

                    if (isTerrain)
                    {
                        countTerrains++;
                    }

                    if (isGeometry)
                    {
                        countGeometry++;
                    }
                    else
                    {
                        countGeometryUnmarked++;
                    }

                    if (isNav)
                    {
                        countNav++;
                    }
                    else
                    {
                        countNavigationUnmarked++;
                    }
                }

                string ignoredMessage = "";
                if (countWithIgnored > countAllItems)
                {
                    ignoredMessage = $" ({countWithIgnored - countAllItems} ignored)";
                }

                if (selectionFilter)
                {
                    string objectText = String.Format(filterText, "Selected");
                    EditorGUILayout.LabelField($"{objectText} Objects{ignoredMessage}:", EditorStyles.boldLabel);
                }
                else
                {
                    string objectText = String.Format(filterText, "Scene");
                    EditorGUILayout.LabelField($"{objectText} Objects{ignoredMessage}:", EditorStyles.boldLabel);
                }
                EditorGUI.indentLevel += 1;
                EditorGUILayout.LabelField($"Total: {countWithIgnored}, Mesh: {countMeshes}, Terrain: {countTerrains}, Geometry: {countGeometry}, Navigation: {countNav}");
                EditorGUI.indentLevel -= 1;

                GUILayout.Space(10);
                EditorGUILayout.LabelField($"{countMeshes} MeshRenderers and {countTerrains} Terrains", EditorStyles.boldLabel);

                EditorGUI.indentLevel += 1;

                if (countAllItems > 0)
                {
                    // Geometry select/unselect
                    bool mixedSelection = (countGeometry != 0 && countGeometryUnmarked != 0);

                    if (mixedSelection)
                    {
                        EditorGUI.showMixedValue = true;
                    }

                    bool currentValue = (countGeometry > 0);
                    bool newValue = EditorGUILayout.Toggle(new GUIContent("Acoustics Geometry", "Mark the selected objects as geometry for the acoustics simulation."), currentValue);

                    // If mixed mode is on the toggle always returns false until the checkbox is clicked.
                    if ((!mixedSelection && (newValue != currentValue)) || (mixedSelection && newValue))
                    {
                        ChangeObjectMarkStatus(currentSelection, newValue, false);
                    }

                    if (mixedSelection)
                    {
                        EditorGUI.showMixedValue = false;
                    }

                    // Navigation select/unselect
                    mixedSelection = (countNav != 0 && countNavigationUnmarked != 0);

                    if (mixedSelection)
                    {
                        EditorGUI.showMixedValue = true;
                    }

                    currentValue = (countNav > 0);
                    newValue = EditorGUILayout.Toggle(new GUIContent("Acoustics Navigation", "Mark the selected objects as navigable to be used during probe point layout."), currentValue);

                    // If mixed mode is on the toggle always returns false until the checkbox is clicked.
                    if ((!mixedSelection && (newValue != currentValue)) || (mixedSelection && newValue))
                    {
                        ChangeObjectMarkStatus(currentSelection, newValue, true);
                    }

                    if (mixedSelection)
                    {
                        EditorGUI.showMixedValue = false;
                    }
                    EditorGUI.indentLevel -= 1;
                }

                GUILayout.Space(20);

                if (!selectionFilter || countAllItems == 0)
                {
                    if (selectionFilter && countWithIgnored > 0)
                    {
                        EditorGUILayout.HelpBox("Only inactive or non-mesh/terrain objects are selected. Please select one or more active mesh or terrain objects using the hierarchy window.", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Please select one or more active mesh or terrain objects using the hierarchy window. Use the scene filters above to filter for relevant objects.", MessageType.None);
                    }
                    return;
                }
            }
        }

        void RenderMaterialsTab()
        {
            if (m_listView == null)
            {
                // Can't do anything if the listview failed to load.
                EditorGUILayout.LabelField("ERROR: Failed to load materials data.");
                return;
            }

            if (m_previewRootObject != null)
            {
                GUILayout.Label("Clear the preview on the Probes tab to make changes.", EditorStyles.boldLabel);
                GUILayout.Space(20);
            }

            using (new EditorGUI.DisabledScope(m_previewRootObject != null))
            {
                GUILayout.Label("Step Two", EditorStyles.boldLabel);
                GUILayout.Label("Assign acoustic properties to each scene material using the dropdown. " +
                    "Different materials can have a dramatic effect on the results of the bake. " +
                    "Choose \"Custom\" to set the absorption coefficient directly.", EditorStyles.wordWrappedLabel);

                GUILayout.Space(10);

                GUILayout.Label("Click the scene material name to select the objects which use that material.", EditorStyles.wordWrappedLabel);

                GUILayout.Space(20);

                m_listView.FilterUnmarked = EditorGUILayout.Toggle(new GUIContent("Show Marked Only", "When checked, only materials on objects marked as Acoustics Geometry will be listed"), m_listView.FilterUnmarked);

                string[] optionList = new string[] { "Name  ", "Absorptivity" };
                GUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Sort Acoustics By:", "Use this to choose the sort order for the list of known acoustic materials"));
                m_currentMaterialSortOrder = (AcousticMaterialSortOrder)GUILayout.SelectionGrid((int)m_currentMaterialSortOrder, optionList, 2, EditorStyles.radioButton);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(10);

                m_listView.SortKnownMaterialsByAbsorption = (m_currentMaterialSortOrder == AcousticMaterialSortOrder.ByAbsorptivity);

                Rect rect = GUILayoutUtility.GetRect(200f, 200f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

                m_listView.OnGUI(rect);
            }
        }

        void RenderProbesTab()
        {
            float buttonWidth = 115;
            float viewWidth = EditorGUIUtility.currentViewWidth;

            bool previewShowing = (m_previewRootObject != null);

            if (previewShowing)
            {
                GUILayout.Label("Clear the preview to make changes and recompute.", EditorStyles.boldLabel);
                GUILayout.Label("Use the 'Clear' button below to clear it.", EditorStyles.boldLabel);
                GUILayout.Space(10);
                GUILayout.Label("Use Gizmos menu to show/hide probe points and voxels.", EditorStyles.boldLabel);
            }

            using (new EditorGUI.DisabledScope(previewShowing))
            {
                GUILayout.Label("Step Three", EditorStyles.boldLabel);
                GUILayout.Label("Previewing the probe points helps you ensure that your probe locations map to the areas " +
                    "in the scene where the user will travel, as well as evaulating the number of probe points, which affects " +
                    "bake time and cost.\n\nIn addition, you can preview the voxels to see how portals (doors, windows, etc.) " +
                    "might be affected by the simulation resolution.\n\nThe probe points calculated here will be used when " +
                    "you submit your bake.", EditorStyles.wordWrappedLabel);

                GUILayout.Space(20);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Simulation Resolution");

                int selectedOld = (s_AcousticsParameters.SimulationMaxFrequency == AcousticsParameters.CoarseSimulationFrequency) ? 0 : 1;
                string[] optionList = new string[] { AcousticsParameters.CoarseSimulationName + "  ", AcousticsParameters.FineSimulationName + "  " }; // Extra spaces are for better UI layout.

                int selectedNew = GUILayout.SelectionGrid(selectedOld, optionList, 2, EditorStyles.radioButton);
                GUILayout.FlexibleSpace();
                s_AcousticsParameters.SimulationMaxFrequency = (selectedNew == 0) ? AcousticsParameters.CoarseSimulationFrequency : AcousticsParameters.FineSimulationFrequency;
                EditorGUILayout.EndHorizontal();

                if (selectedNew != selectedOld)
                {
                    // Need a new cost sheet
                    m_computeTimeCostSheet = null;
                    UpdateComputeTimeEstimate();
                }

                GUILayout.Space(20);

                string oldDataFolder = s_AcousticsParameters.AcousticsDataFolder;

                EditorGUILayout.BeginHorizontal();
                s_AcousticsParameters.AcousticsDataFolder = EditorGUILayout.TextField(new GUIContent("Acoustics Data Folder", "Enter the path where you want acoustics data stored."), s_AcousticsParameters.AcousticsDataFolder);
                if (GUILayout.Button(new GUIContent("...", "Use file chooser dialog to specify the folder for acoustics files."), GUILayout.Width(25)))
                {
                    string result = EditorUtility.OpenFolderPanel("Acoustics Data Folder", "", "");
                    if (!String.IsNullOrEmpty(result))
                    {
                        s_AcousticsParameters.AcousticsDataFolder = result;
                    }
                    GUI.FocusControl("...");    // Manually move the focus so that the text field will update
                }
                EditorGUILayout.EndHorizontal();

                if (!oldDataFolder.Equals(s_AcousticsParameters.AcousticsDataFolder) && !Directory.Exists(s_AcousticsParameters.AcousticsDataFolderEditorOnly))
                {
                    Directory.CreateDirectory(s_AcousticsParameters.AcousticsDataFolderEditorOnly);
                }

                s_AcousticsParameters.DataFileBaseName = EditorGUILayout.TextField(new GUIContent("Acoustics Files Prefix", "Enter the base filename for acoustics files."), s_AcousticsParameters.DataFileBaseName);

                // Update the serialized copy of this data
                MarkParametersDirty();
            }

            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!previewShowing))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(buttonWidth)))
                {
                    CleanupPreviewData(true);
                }
            }

            using (new EditorGUI.DisabledScope(previewShowing))
            {
                if (GUILayout.Button("Calculate...", GUILayout.Width(buttonWidth)))
                {
                    CalculateProbePoints();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        void RenderBakeTab()
        {
            float buttonWidth = 115;
            float viewWidth = EditorGUIUtility.currentViewWidth;
            Rect tmpRect;
            bool allCredsFilledIn = false;
            bool anyCredsFilledIn = false;

            GUILayout.Label("Step Four", EditorStyles.boldLabel);
            GUILayout.Label("Once you have completed the previous steps then submit the job for baking in the cloud here.\n\n" +
                "Make sure you have created your Azure account according to the instructions.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(20);

            // The returned rect is a dummy rect if calculatingLayout is true, but in this case we don't care - the Foldout updates properly during repaint.
            tmpRect = GUILayoutUtility.GetRect(new GUIContent("Advanced"), GUI.skin.label);

            m_bakeCredsFoldoutOpen = EditorGUI.Foldout(tmpRect, m_bakeCredsFoldoutOpen, "Azure Configuration", true);
            if (m_bakeCredsFoldoutOpen)
            {
                string oldCreds = EditorJsonUtility.ToJson(s_AcousticsParameters.AzureAccounts);

                EditorGUI.indentLevel += 1;

                s_AcousticsParameters.AzureAccounts.BatchAccountName = EditorGUILayout.TextField(new GUIContent("Batch Account Name", "Enter the name of your Azure Batch account here."), s_AcousticsParameters.AzureAccounts.BatchAccountName);
                s_AcousticsParameters.AzureAccounts.BatchAccountUrl = EditorGUILayout.TextField(new GUIContent("Batch Account URL", "Enter the url of your Azure Batch account here."), s_AcousticsParameters.AzureAccounts.BatchAccountUrl);
                s_AcousticsParameters.AzureAccounts.BatchAccountKey = EditorGUILayout.PasswordField(new GUIContent("Batch Account Key", "Enter the key of your Azure Batch account here."), s_AcousticsParameters.AzureAccounts.BatchAccountKey);

                GUILayout.Space(10);

                s_AcousticsParameters.AzureAccounts.StorageAccountName = EditorGUILayout.TextField(new GUIContent("Storage Account Name", "Enter the name of the associated Azure Storage account here."), s_AcousticsParameters.AzureAccounts.StorageAccountName);
                s_AcousticsParameters.AzureAccounts.StorageAccountKey = EditorGUILayout.PasswordField(new GUIContent("Storage Account Key", "Enter the key of the associated Azure Storage account here."), s_AcousticsParameters.AzureAccounts.StorageAccountKey);

                GUILayout.Space(10);

                if (s_AcousticsParameters.TritonImage == "")
                {
                    s_AcousticsParameters.TritonImage = AcousticsParameters.DefaultTritonImage;
                }
                s_AcousticsParameters.TritonImage = EditorGUILayout.TextField(new GUIContent("Toolset Version", "Enter a docker image tag for the bake tools to use. To reset, clear this field"), s_AcousticsParameters.TritonImage);

                GUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 20);
                GUIContent buttonContent = new GUIContent("Launch Azure Portal", AzurePortalUrl);
                GUIStyle style = GUI.skin.button;
                Vector2 contentSize = style.CalcSize(buttonContent);
                if (GUILayout.Button(buttonContent, GUILayout.Width(contentSize.x)))
                {
                    Application.OpenURL(AzurePortalUrl);
                }
                GUILayout.EndHorizontal();

                EditorGUI.indentLevel -= 1;

                allCredsFilledIn = AreAllAzureCredentialsFilledIn();

                anyCredsFilledIn = (!String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountName) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountKey) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountUrl) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountName) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountKey));

                // Update stored info if necessary.
                string newCreds = EditorJsonUtility.ToJson(s_AcousticsParameters.AzureAccounts);

                if (!newCreds.Equals(oldCreds))
                {
                    if (anyCredsFilledIn)
                    {
                        // encrypt creds before saving
                        byte[] newCredsBytes = System.Text.Encoding.Unicode.GetBytes(newCreds);
                        byte[] newCredsBytesProtected = System.Security.Cryptography.ProtectedData.Protect(newCredsBytes, null, DataProtectionScope.CurrentUser);

                        string newCredsProtected = Convert.ToBase64String(newCredsBytesProtected);

                        EditorPrefs.SetString(AzureAccountInfoKeyName, newCredsProtected);
                    }
                    else if (!allCredsFilledIn && EditorPrefs.HasKey(AzureAccountInfoKeyName))
                    {
                        // User emptied all the fields. Remove the key from the registry.
                        EditorPrefs.DeleteKey(AzureAccountInfoKeyName);
                    }
                }
            }
            else
            {
                allCredsFilledIn = AreAllAzureCredentialsFilledIn();
            }

            GUILayout.Space(10);

            int oldVM = s_AcousticsParameters.SelectedVMType;
            int oldNodeCount = s_AcousticsParameters.NodeCount;

            s_AcousticsParameters.SelectedVMType = EditorGUILayout.Popup("VM Node Type", s_AcousticsParameters.SelectedVMType, s_AcousticsParameters.SupportedAzureVMTypes);
            int newNodeCount = EditorGUILayout.IntField("Node Count", s_AcousticsParameters.NodeCount);
            int previewNodes = m_previewResults != null ? m_previewResults.NumProbes : int.MaxValue;

            // Node count needs be between 1 and total number of probes
            s_AcousticsParameters.NodeCount = Math.Min(Math.Max(newNodeCount, 1), previewNodes); 
            s_AcousticsParameters.UseLowPriorityNodes = EditorGUILayout.Toggle("Use Low Priority", s_AcousticsParameters.UseLowPriorityNodes);

            if ((s_AcousticsParameters.SelectedVMType != oldVM) || (s_AcousticsParameters.NodeCount != oldNodeCount))
            {
                 UpdateComputeTimeEstimate();
            }

            // Update the serialized copy of this data
            MarkParametersDirty();

            if (m_previewResults != null)
            {
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                GUILayout.Space(10);
                EditorGUILayout.LabelField("Probe Count", m_previewResults.NumProbes.ToString());
                EditorGUILayout.LabelField("Estimated Bake Time", GetFormattedTimeEstimate(m_estimatedTotalComputeTime), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Estimated Compute Cost", GetFormattedTimeEstimate(TimeSpan.FromTicks(m_estimatedTotalComputeTime.Ticks * s_AcousticsParameters.NodeCount)), EditorStyles.wordWrappedLabel);

                EditorGUI.indentLevel += 1;
                EditorGUILayout.LabelField("NOTE:", "Estimated cost and time are rough estimates only. They do not include pool or node startup and shutdown time. Accuracy is not guaranteed.", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel -= 1;
            }

            GUILayout.Space(20);
            GUILayout.Label($"The result file will be saved as {s_AcousticsParameters.DataFileBaseName}.ace.bytes in the acoustics data folder. Use the Probes tab to change location or name.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            // Disable "Clear State" button while job deletion is pending OR if there's no active job.
            using (new EditorGUI.DisabledScope(m_cloudJobDeleting || (m_timerStartValue == 0 && String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))))
            {
                if (GUILayout.Button(new GUIContent("Clear State", "Forget about the submitted job and stop checking status."), GUILayout.Width(buttonWidth)))
                {
                    m_cloudJobStatus = "Reset";
                    s_AcousticsParameters.ActiveJobID = null;
                    m_timerStartValue = 0;
                }
            }

            using (new EditorGUI.DisabledScope(m_cloudJobDeleting || (m_workerThread != null && m_workerThread.IsAlive))) // Disable the Bake/Cancel button if there is the job is deleting, or some work is being done (such as submitting or canceling a bake!)
            {
                if (String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
                {
                    using (new EditorGUI.DisabledScope(m_previewRootObject == null)) // Disable Bake if there is no preview
                    {
                        if (GUILayout.Button(new GUIContent("Bake", "Start the bake in the cloud"), GUILayout.Width(buttonWidth)))
                        {
                            if (!allCredsFilledIn)
                            {
                                EditorUtility.DisplayDialog("Acoustics Bake", "One or more Azure credentials fields are not filled in.\nPlease fill in all fields.", "OK");
                            }
                            else if (String.IsNullOrWhiteSpace(s_AcousticsParameters.AcousticsDataFolder) || String.IsNullOrWhiteSpace(s_AcousticsParameters.DataFileBaseName))
                            {
                                EditorUtility.DisplayDialog("Acoustics Bake", "The data folder and/or base filename fields on the Probes tab are not correct.\nPlease fill in all fields.", "OK");
                            }
                            else
                            {
                                StartBake();
                            }
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button(new GUIContent("Cancel Job", "Cancel the currently running bake job"), GUILayout.Width(buttonWidth)))
                    {
                        if (EditorUtility.DisplayDialog("Cancel Azure Job?", "Are you sure you wish to cancel the submitted Azure job? Any calculations done so far will be lost, and you will still be charged for the time used. This cannot be undone.", "Yes - Do It!", "No - Leave It Alone"))
                        {
                            CancelBake();
                        }
                    }
                }
            }

            // Support for local bakes
            using (new EditorGUI.DisabledScope(m_previewRootObject == null)) // Disable local bake button if there is no preview
            {
                GUIContent localBakeContent = new GUIContent("Prepare Local Bake", "Generate package for local bake");
                GUIStyle localBakeButtonStyle = GUI.skin.button;
                if (GUILayout.Button(localBakeContent, GUILayout.Width(localBakeButtonStyle.CalcSize(localBakeContent).x)))
                {
                    string localPath = EditorUtility.OpenFolderPanel("Select a folder for local bake package", "", "ProjectAcoustics");
                    if (!String.IsNullOrEmpty(localPath))
                    {
                        // Copy simulation input files
                        File.Copy(s_AcousticsParameters.VoxFilepath, Path.Combine(localPath, s_AcousticsParameters.VoxFilename), true);
                        File.Copy(s_AcousticsParameters.ConfigFilepath, Path.Combine(localPath, s_AcousticsParameters.ConfigFilename), true);

                        // Validate Triton Image first
                        if (s_AcousticsParameters.TritonImage == "")
                        {
                            s_AcousticsParameters.TritonImage = AcousticsParameters.DefaultTritonImage;
                        }

                        // Generate Windows batch file for local processing
                        using (StreamWriter writer = new StreamWriter(Path.Combine(localPath, "runlocalbake.bat")))
                        {
                            string command = String.Format(
                                "docker run --rm -w /acoustics/ -v \"%CD%\":/acoustics/working/ {0} ./tools/Triton.LocalProcessor -vox {1} -config {2} -workingdir working",
                                s_AcousticsParameters.TritonImage,
                                s_AcousticsParameters.VoxFilename,
                                s_AcousticsParameters.ConfigFilename);
                            writer.WriteLine(command);
                            writer.WriteLine("del *.dat");
                            writer.WriteLine("del *.enc");
                        }

                        // Generate MacOS bash script for local processing
                        using (StreamWriter writer = new StreamWriter(Path.Combine(localPath, "runlocalbake.sh")))
                        {
                            string command = String.Format(
                                "docker run --rm -w /acoustics/ -v \"$PWD\":/acoustics/working/ {0} ./tools/Triton.LocalProcessor -vox {1} -config {2} -workingdir working",
                                s_AcousticsParameters.TritonImage,
                                s_AcousticsParameters.VoxFilename,
                                s_AcousticsParameters.ConfigFilename);
                            writer.WriteLine("#!/bin/bash");
                            writer.WriteLine(command);
                            writer.WriteLine("rm *.dat");
                            writer.WriteLine("rm *.enc");
                        }

                        this.ShowNotification(new GUIContent("Files for local bake saved under " + Path.GetFullPath(localPath)));
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(15);

            GUILayout.Label("Azure Bake Status:", EditorStyles.whiteLabel);
            GUILayout.Label((String.IsNullOrEmpty(m_cloudJobStatus) ? "Not submitted" : m_cloudJobStatus), EditorStyles.boldLabel);
            GUILayout.Label(String.Format("   Last Updated: {0}", m_cloudLastUpdateTime.ToString("g")), EditorStyles.miniLabel);

            if (!String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
            {
                GUILayout.Space(15);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Job ID:");
                EditorGUILayout.SelectableLabel(s_AcousticsParameters.ActiveJobID);
                EditorGUILayout.EndHorizontal();
            }
        }

        bool AreAllAzureCredentialsFilledIn()
        {
            return (!String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountName) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountKey) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountUrl) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountName) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountKey));
        }

        string GetFormattedTimeEstimate(TimeSpan duration)
        {
            string totalTimeString = "";

            if (duration.Days > 0)
            {
                totalTimeString = String.Format("{0} day{1} ", duration.Days, (duration.Days == 1) ? "" : "s");
            }

            if (duration.Hours > 0 || duration.Days > 0)
            {
                totalTimeString += String.Format("{0} hour{1} ", duration.Hours, (duration.Hours == 1) ? "" : "s");
            }

            totalTimeString += String.Format("{0} minute{1}", duration.Minutes, (duration.Minutes == 1) ? "" : "s");

            return totalTimeString;
        }

        public enum HierarchyFilterMode
        {
            All = 0,
            Name = 1,
            Type = 2,
        };

        /// <summary>
        /// Set the filter in the hierarchy window. Uses reflection to find a non-public method.
        /// </summary>
        /// <param name="filter">Text to use as filter</param>
        /// <param name="filterMode">What kind of filter should be applied</param>
        public static void SetSearchFilter(string filter, HierarchyFilterMode filterMode)
        {
            SearchableEditorWindow hierarchyWindow = null;
            SearchableEditorWindow[] windows = Resources.FindObjectsOfTypeAll<SearchableEditorWindow>();

            foreach (SearchableEditorWindow window in windows)
            {
                if (window.GetType().ToString() == "UnityEditor.SceneHierarchyWindow")
                {
                    hierarchyWindow = window;
                    break;
                }
            }

            if (hierarchyWindow != null)
            {
                MethodInfo setSearchType = typeof(SearchableEditorWindow).GetMethod("SetSearchFilter", BindingFlags.NonPublic | BindingFlags.Instance);
                ParameterInfo[] param_info = setSearchType.GetParameters();
                object[] parameters;
                if (param_info.Length == 3)
                {
                    // (string searchFilter, SearchMode mode, bool setAll)
                    parameters = new object[] { filter, (int)filterMode, false };
                }
                else if (param_info.Length == 4)
                {
                    // (string searchFilter, SearchMode mode, bool setAll, bool delayed)
                    parameters = new object[] { filter, (int)filterMode, false, false };
                }
                else
                {
                    Debug.LogError("Error calling SetSearchFilter");
                    return;
                }
                setSearchType?.Invoke(hierarchyWindow, parameters);
            }
        }

        /// <summary>
        /// Given a list of objects, will mark or unmark them for inclusion in acoustics or as an acoustics navmesh.
        /// </summary>
        /// <param name="Objects">List of objects to mark or unmark</param>
        /// <param name="SetMark">If true, then mark them, otherwise unmark them.</param>
        /// <param name="ForNavMesh">If true, mark or unmark the navmesh component. Otherwise, mark or unmark the acoustics component.</param>
        void ChangeObjectMarkStatus(Transform[] Objects, bool SetMark, bool ForNavMesh)
        {
            if (SetMark)
            {
                Array.ForEach<Transform>(Objects, obj => {
                    if ((obj.GetComponent<MeshRenderer>() != null || obj.GetComponent<Terrain>() != null))
                    {
                        if (!ForNavMesh && obj.GetComponent<AcousticsGeometry>() == null)
                        {
                            obj.gameObject.AddComponent<AcousticsGeometry>();
                        }
                        else if (ForNavMesh && obj.GetComponent<AcousticsNavigation>() == null)
                        {
                            obj.gameObject.AddComponent<AcousticsNavigation>();
                        }
                    }
                });
            }
            else if (!ForNavMesh)
            {
                Array.ForEach<Transform>(Objects, obj => {
                    AcousticsGeometry c = obj.GetComponent<AcousticsGeometry>();
                    if (c != null)
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                    }
                });
            }
            else
            {
                Array.ForEach<Transform>(Objects, obj => {
                    AcousticsNavigation c = obj.GetComponent<AcousticsNavigation>();
                    if (c != null)
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                    }
                });
            }
        }

        /// <summary>
        /// Called when the user clicks the Calculate button on the Probes tab. Passes the mesh data to Triton for
        /// voxelization and calculation of probe locations.
        /// </summary>
        void CalculateProbePoints()
        {
            // Init our cross-thread communication data
            m_previewResults = null;
            m_progressValue = 0;
            m_progressMessage = "Converting mesh object vertices...";
            m_previewCancelRequested = false;

            CleanupPreviewData(false);

            // Check if we have a nav mesh and warn if not.
            NavMeshTriangulation triangulatedNavMesh = NavMesh.CalculateTriangulation();
            AcousticsNavigation[] customNavMeshes = GameObject.FindObjectsOfType<AcousticsNavigation>();

            if (triangulatedNavMesh.vertices.Length < 1 && customNavMeshes.Length == 0)
            {
                EditorUtility.DisplayDialog("NavMesh Required", "You have not created or specified a navigation mesh! Navigation meshes determine how probe " +
                    "points are placed. \n\nYou can create a nav mesh by using Unity's navigation system or by marking objects as Acoustics Navigation in the " +
                    "Objects tab.", "OK");
                return;
            }

            // AcousticMesh is the object that contains all mesh data (including navigation mesh) for the calculation.
            Triton.AcousticMesh acousticMesh = Triton.AcousticMesh.Create();

            // Populate AcousticMesh with all tagged mesh objects in the scene.
            AcousticsGeometry[] agList = GameObject.FindObjectsOfType<AcousticsGeometry>();

            if (agList.Length < 1)
            {
                Debug.LogError("No game objects have been marked as Acoustics Geometry!");
                return;
            }

            // Put up the progress bar.
            UpdateProgressBarDuringPreview();

            // We have to create a new material library that will be used for the materials in this scene.
            // Store the mapping between the Unity material names and absorption coefficients that were selected by the user.
            Dictionary<string, float> materialMap = new Dictionary<string, float>();

            foreach (TreeViewItem item in m_listView.GetRows())
            {
                TritonMaterialsListElement element = (TritonMaterialsListElement)item;

                if (element.MaterialCode == Triton.MaterialLibrary.Reserved0)
                {
                    // User selected "Custom" for the material
                    materialMap.Add(element.Name, element.Absorptivity);
                }
                else
                {
                    // Get the absorptivity from the list of known materials dropdown's library.
                    Triton.AcousticMaterial mat = m_listView.MaterialLibrary.GetMaterialInfo(element.MaterialCode);
                    materialMap.Add(element.Name, mat.Absorptivity);
                }
            }

            // This is the material library that will be used both for probe layout and for the actual bake.
            Triton.MaterialLibrary materialLib = Triton.MaterialLibrary.Create(materialMap);

            // Convert all mesh objects that have been tagged.
            foreach (AcousticsGeometry ag in agList)
            {
                MeshFilter mf = ag.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    long materialCode = Triton.MaterialLibrary.DefaultWallCode;

                    Mesh m = mf.sharedMesh;
                    Renderer r = ag.GetComponent<Renderer>();

                    if (r != null && r.sharedMaterial != null && materialMap.ContainsKey(r.sharedMaterial.name))
                    {
                        materialCode = materialLib.GetMaterialCode(r.sharedMaterial.name);
                    }

                    if (m != null)
                    {
                        AddToTritonAcousticMesh(mf.transform, acousticMesh, m.vertices, m.triangles, materialCode, false, true);
                    }
                }
            }

            // Convert the nav mesh(es)
            if (triangulatedNavMesh.vertices.Length > 0)
            {
                AddToTritonAcousticMesh(null, acousticMesh, triangulatedNavMesh.vertices, triangulatedNavMesh.indices, Triton.MaterialLibrary.TritonNavigableArea, true, false);
            }

            foreach (AcousticsNavigation nav in customNavMeshes)
            {
                MeshFilter mf = nav.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    Mesh m = mf.sharedMesh;
                    if (m != null)
                    {
                        AddToTritonAcousticMesh(mf.transform, acousticMesh, m.vertices, m.triangles, Triton.MaterialLibrary.TritonNavigableArea, true, true);
                    }
                }
            }

            // Create the configuration/specification objects for Triton. For the most part we use defaults.
            Triton.ProbeSamplingSpec pss = new Triton.ProbeSamplingSpec();
            pss.InitToInvalid();
            pss.SetUnspecifiedToDefault();
            Triton.SimulationRegionSpec src = new Triton.SimulationRegionSpec();
            src.InitToInvalid();
            src.SetUnspecifiedToDefault();

            src.BboxLarge =
                new Triton.AABox<Triton.Vec3f, float>(
                    TritonVec3fFromVector3(s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower),
                    TritonVec3fFromVector3(s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper));

            src.BboxSmall =
                new Triton.AABox<Triton.Vec3f, float>(
                    TritonVec3fFromVector3(s_AcousticsParameters.PerProbeSimulationRegion_Small_Lower),
                    TritonVec3fFromVector3(s_AcousticsParameters.PerProbeSimulationRegion_Small_Upper));

            Triton.SamplingRegionSpec samprc = new Triton.SamplingRegionSpec();
            samprc.UseSceneBoundingBox = true;
            samprc.CheckConsistency();

            pss.HorizontalSpacingMax = s_AcousticsParameters.ProbeHorizontalSpacingMax;
            pss.HorizontalSpacingMin = s_AcousticsParameters.ProbeHorizontalSpacingMin;
            pss.ProbeMinHeightAboveGround = s_AcousticsParameters.ProbeMinHeightAboveGround;

            // Now, hand the task off to Triton for calculation.
            Triton.SimulationParameters simParams = new Triton.SimulationParameters()
            {
                MeshUnitAdjustment = s_AcousticsParameters.MeshUnitAdjustment,
                SceneScale = s_AcousticsParameters.SceneScale,
                SpeedOfSound = s_AcousticsParameters.SpeedOfSound,
                SimulationMaxFrequency = s_AcousticsParameters.SimulationMaxFrequency,
                ReceiverSampleSpacing = s_AcousticsParameters.ReceiverSampleSpacing,
                ProbeSpacing = pss,
                PerProbeSimulationRegion = src,
                SamplingRegion = samprc,
                VoxelmapResolution = -1
            };

            simParams.SetUnspecifiedOptionalsToDefaults();

            string fileOutPrefix = s_AcousticsParameters.DataFileBaseName;

            Triton.JobOperationalParams opParams = new Triton.JobOperationalParams()
            {
                OutputPrefix = fileOutPrefix,
                ShouldSkipVoxelization = false,
                ShouldVisualize = false,
                // Defaults are OK for everything else.
            };

            opParams.SetWorkingDir(s_AcousticsParameters.AcousticsDataFolderEditorOnly);

            CalcProbeParams cpp = new CalcProbeParams()
            {
                mesh = acousticMesh,
                simParams = simParams,
                opParams = opParams,
                matlib = materialLib
            };

            m_workerThread = new System.Threading.Thread(DoCalculateProbes);
            m_workerThread.Start(cpp);
        }

        /// <summary>
        /// Calculate the voxels and probe points in a separate thread to prevent blocking the UI.
        /// </summary>
        /// <param name="parms">Must contain a filled in CalcProbeParams object.</param>
        void DoCalculateProbes(object parms)
        {
            CalcProbeParams cpp = (CalcProbeParams)parms;
            Triton.SimulationConfig config = null;

            try
            {
                config = Triton.CreateJobConfig.CalculateProbePoints(cpp.simParams, cpp.opParams, cpp.mesh, cpp.matlib, false, StatusCallback);
            }
            catch (Exception ex)
            {
                // Ignore exceptions. DisplayPreviewResults will do the right thing.
                m_progressMessage = ex.ToString();
                QueueUIThreadAction(LogMessage);
            }

            // When the calculation is finished run DisplayPreviewResults to clean up and add the output to the scene.
            m_previewResults = config;
            QueueUIThreadAction(DisplayPreviewResults);
        }

        /// <summary>
        /// Used mostly for debugging our worker thread.
        /// </summary>
        void LogMessage()
        {
            Debug.Log(m_progressMessage);
        }

        /// <summary>
        /// Called by the Triton code to display progress messages.
        /// </summary>
        /// <param name="message">Progress message to display</param>
        /// <param name="percent">Percent complete (0 - 100)</param>
        /// <returns>True if the calculation should be canceled.</returns>
        bool StatusCallback(string message, int percent)
        {
            bool returnValue = false;

            m_progressMessage = message;
            m_progressValue = percent;

            returnValue = m_previewCancelRequested;

            // Have to do updates on the UI thread.
            QueueUIThreadAction(UpdateProgressBarDuringPreview);

            return returnValue;
        }

        /// <summary>
        /// Method that actually updates the progress bar. Must be called on the UI thread.
        /// </summary>
        void UpdateProgressBarDuringPreview()
        {
            if (m_progressValue < 100)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Calculating Probe Locations", m_progressMessage, (float)m_progressValue / 100.0f))
                {
                    m_previewCancelRequested = true;
                }
            }
            else
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Called once the calculations are complete or have been aborted.
        /// </summary>
        void DisplayPreviewResults()
        {
            m_workerThread = null;

            EditorUtility.ClearProgressBar();

            if (m_previewResults == null)
            {
                Debug.Log("No results are available.");
                CleanupPreviewData(false);
                return;
            }

            UpdateComputeTimeEstimate();

            Debug.Log(String.Format("Number of probe points: {0}", m_previewResults.NumProbes));

            if (m_previewRootObject == null)
            {
                m_previewRootObject = new GameObject();
                // The mere existence of this component will cause its OnDrawGizmos function to be called.
                m_previewRootObject.AddComponent<AcousticsProbes>().ProbesRenderer = ScriptableObject.CreateInstance<AcousticsProbesRenderer>();
                m_previewRootObject.AddComponent<AcousticsVoxels>().VoxelRenderer = ScriptableObject.CreateInstance<AcousticsVoxelsRenderer>();
                m_previewRootObject.name = "Acoustics Previewer";
                m_previewRootObject.hideFlags = HideFlags.HideInInspector | HideFlags.NotEditable | HideFlags.DontSaveInEditor;
                m_previewRootObject.tag = "EditorOnly";
            }
            // Root object already exists in the scene. No need to recreate it.

            ((AcousticsProbesRenderer)m_previewRootObject.GetComponent<AcousticsProbes>().ProbesRenderer)?.SetPreviewData(m_previewResults);
            ((AcousticsVoxelsRenderer)m_previewRootObject.GetComponent<AcousticsVoxels>().VoxelRenderer)?.SetPreviewData(m_previewResults);
        }

        /// <summary>
        /// Based on the probe calculation data and information about the nodes, get the estimated compute time for this scene.
        /// </summary>
        void UpdateComputeTimeEstimate()
        {
            if (m_previewResults == null)
            {
                Debug.Log("No results are available.");
                CleanupPreviewData(false);
                return;
            }

            if (String.IsNullOrEmpty(m_computeTimeCostSheet))
            {
                MonoScript thisScript = MonoScript.FromScriptableObject(this);
                string pathToThisScript = Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript));
                string unityRootPath = Path.GetDirectoryName(Application.dataPath);

                string simFreqName = (s_AcousticsParameters.SimulationMaxFrequency == AcousticsParameters.CoarseSimulationFrequency) ? AcousticsParameters.CoarseSimulationName : AcousticsParameters.FineSimulationName;
                string costSheetFileName = $"{simFreqName}BakeCostSheet.xml";

                string costSheetFile = Path.Combine(unityRootPath, pathToThisScript, costSheetFileName);
                m_computeTimeCostSheet = Path.GetFullPath(costSheetFile); // Normalize the path
            }

            float simulationVolume = (s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper.x - s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower.x) * // Length
                                     (s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper.y - s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower.y) * // Width
                                     (s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper.z - s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower.z);  // Height

            SimulationConfiguration simConfig = new SimulationConfiguration()
            {
                Frequency = (int)s_AcousticsParameters.SimulationMaxFrequency,
                ProbeCount = m_previewResults.NumProbes,
                ProbeSpacing = s_AcousticsParameters.ProbeHorizontalSpacingMax,
                ReceiverSpacing = s_AcousticsParameters.ReceiverSampleSpacing,
                SimulationVolume = simulationVolume,
            };

            m_estimatedTotalComputeTime = CloudProcessor.EstimateProcessingTime(m_computeTimeCostSheet, simConfig, s_AcousticsParameters.GetPoolConfiguration());
        }

        /// <summary>
        /// Convert from Unity coordinates (Left-Handed, Y+ Up) to Maya/Triton coordinates (Right-Handed, Z+ Up)
        /// </summary>
        /// <param name="position">3D coordinate to translate.</param>
        /// <returns>Point in Triton space.</returns>
        static Vector4 WorldToTriton(Vector4 position)
        {
            return (s_worldToTriton * position);
        }

        /// <summary>
        /// Convert from Maya/Triton coordinates to Unity coordinates.
        /// </summary>
        /// <param name="position">3D coordinate to translate.</param>
        /// <returns>Point in Unity space.</returns>
        public static Vector4 TritonToWorld(Vector4 position)
        {
            return (s_tritonToWorld * position);
        }

        public static Triton.Vec3f TritonVec3fFromVector3(Vector3 vector)
        {
            return new Triton.Vec3f(vector.x, vector.y, vector.z);
        }

        /// <summary>
        /// Remove the calculated preview data
        /// </summary>
        /// <param name="deleteFiles">If true, delete the data files containing the preview as well.</param>
        void CleanupPreviewData(bool deleteFiles)
        {
            if (m_previewRootObject != null)
            {
                AcousticsProbesRenderer probeRenderer = ((AcousticsProbesRenderer)m_previewRootObject.GetComponent<AcousticsProbes>()?.ProbesRenderer);
                AcousticsVoxelsRenderer voxelRenderer = ((AcousticsVoxelsRenderer)m_previewRootObject.GetComponent<AcousticsVoxels>()?.VoxelRenderer);

                if (voxelRenderer != null)
                {
                    voxelRenderer.SetPreviewData(null);
                    ScriptableObject.DestroyImmediate(voxelRenderer);
                }

                if (probeRenderer != null)
                {
                    probeRenderer?.SetPreviewData(null);
                    ScriptableObject.DestroyImmediate(probeRenderer);
                }

                DestroyImmediate(m_previewRootObject);

                m_previewRootObject = null;
            }

            if (deleteFiles)
            {
                // This returns both the vox file and config file. We may get other matches as well.
                string[] assetGuids = AssetDatabase.FindAssets(s_AcousticsParameters.DataFileBaseName);

                foreach (string guid in assetGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    // Make sure we're deleting only the files we want
                    if (Path.GetFileName(assetPath).Equals(s_AcousticsParameters.VoxFilename) ||
                        Path.GetFileName(assetPath).Equals(s_AcousticsParameters.ConfigFilename))
                    {
                        AssetDatabase.DeleteAsset(assetPath);
                    }
                }
            }
        }

        /// <summary>
        /// Given the vertices and triangles of a unity mesh, add it to the AcousticMesh object.
        /// </summary>
        /// <param name="transform">Transform object used to convert object-local coordinates to world coordinates.</param>
        /// <param name="acousticMesh">AcousticMesh object to add mesh to.</param>
        /// <param name="verticesIn">List of vertices for the mesh.</param>
        /// <param name="trianglesIn">List of triangle indices for the mesh.</param>
        /// <param name="materialCode">The material code (from Triton.MaterialLibrary) that should be assigned to the mesh.</param>
        /// <param name="isNavMesh">If True, the given mesh will be added as a navigation mesh.</param>
        /// <param name="needsTransform">If True, the given mesh needs a local coordinate transformation to Unity world coordinates.</param>
        void AddToTritonAcousticMesh(Transform transform, Triton.AcousticMesh acousticMesh, Vector3[] verticesIn, int[] trianglesIn, long materialCode, bool isNavMesh, bool needsTransform)
        {
            Triton.TriangleInd[] triangles = new Triton.TriangleInd[trianglesIn.Length / 3];
            Triton.Vec3f[] vertices;

            if (!needsTransform)
            {
                vertices = Array.ConvertAll<Vector3, Triton.Vec3f>(verticesIn, vIn => {
                    // NavMesh vertices are already in Unity World coordinates
                    Vector4 vInTransformed = WorldToTriton(vIn);

                    return new Triton.Vec3f(vInTransformed.x, vInTransformed.y, vInTransformed.z);
                });
            }
            else
            {
                vertices = Array.ConvertAll<Vector3, Triton.Vec3f>(verticesIn, vIn => {
                    // Other mesh vertices are in local (relative) coordinates. Need to convert to Unity world coordinates.
                    Vector4 vInTransformed = WorldToTriton(transform.TransformPoint(vIn));

                    return new Triton.Vec3f(vInTransformed.x, vInTransformed.y, vInTransformed.z);
                });
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                int startIndex = i * 3;

                Triton.TriangleInd t = new Triton.TriangleInd();

                t.vertices[0] = trianglesIn[startIndex];
                t.vertices[1] = trianglesIn[startIndex + 1];
                t.vertices[2] = trianglesIn[startIndex + 2];

                if (!isNavMesh)
                {
                    t.MaterialCode = materialCode;
                }

                triangles[i] = t;
            }

            if (isNavMesh)
            {
                acousticMesh.AddNavigableArea("NavigationMesh", vertices, triangles, 1.0f);
            }
            else
            {
                acousticMesh.AddObject(vertices, triangles, 1.0f);
            }
        }

        /// <summary>
        /// Launch the cloud status check thread.
        /// </summary>
        void StartAzureStatusCheck()
        {
            m_workerThread = new System.Threading.Thread(CheckAzureBakeStatus);

            m_workerThread.Start();
        }

        /// <summary>
        /// Called at intervals to check on the status of the cloud bake. If completed, the ACE file is downloaded.
        /// Runs in another thread since the download may occur during the call.
        /// </summary>
        void CheckAzureBakeStatus()
        {
            if (Monitor.TryEnter(this) == false)
            {
                return;
            }
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidator;

                if (String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
                {
                    // Nothing to do.
                    Debug.Log("Timer to check cloud bake expired but there is no job ID. Use 'Clear State' to disable further checks.");
                    return;
                }

                CloudProcessor.BatchAccount.Url = s_AcousticsParameters.AzureAccounts.BatchAccountUrl;
                CloudProcessor.BatchAccount.Name = s_AcousticsParameters.AzureAccounts.BatchAccountName;
                CloudProcessor.BatchAccount.Key = s_AcousticsParameters.AzureAccounts.BatchAccountKey;
                CloudProcessor.StorageAccount.Name = s_AcousticsParameters.AzureAccounts.StorageAccountName;
                CloudProcessor.StorageAccount.Key = s_AcousticsParameters.AzureAccounts.StorageAccountKey;
                JobInformation jobInfo = null;

                m_cloudJobStatus = "";

                try
                {
                    Task<JobInformation> jobInfoTask = CloudProcessor.GetJobInformationAsync(s_AcousticsParameters.ActiveJobID);

                    jobInfoTask.Wait();
                    jobInfo = jobInfoTask.Result;
                }
                catch (AggregateException ex)
                {
                    foreach (Exception e in ex.InnerExceptions)
                    {
                        if (e is ArgumentException)
                        {
                            m_cloudJobStatus = "Job deleted";
                            m_timerStartValue = 0;
                            s_AcousticsParameters.ActiveJobID = null;
                            m_cloudJobDeleting = false;
                            break;
                        }
                        else
                        {
                            m_cloudJobStatus = "Error checking status. See console.";
                            m_progressMessage = ex.ToString();
                            QueueUIThreadAction(LogMessage);

                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_cloudJobStatus = "Error checking status. See console.";
                    m_progressMessage = ex.ToString();
                    QueueUIThreadAction(LogMessage);

                    throw;
                }

                m_cloudLastUpdateTime = DateTime.Now;

                if (jobInfo == null)
                {
                    if (String.IsNullOrEmpty(m_cloudJobStatus))
                    {
                        m_cloudJobStatus = "Job Status Unavailable";
                    }

                    QueueUIThreadAction(Repaint);

                    return;
                }

                if (jobInfo.Status == JobStatus.InProgress)
                {
                    int totalCount = jobInfo.Tasks.Active + jobInfo.Tasks.Completed + jobInfo.Tasks.Running;
                    m_cloudJobStatus = $"Running - {jobInfo.Tasks.Completed}/{totalCount} tasks complete";

                    if (jobInfo.Tasks.Failed > 0)
                    {
                        m_cloudJobStatus += $" ({jobInfo.Tasks.Failed} failed)";
                    }
                }
                else if (jobInfo.Status == JobStatus.Pending)
                {
                    m_cloudJobStatus = "Waiting for nodes to initialize";
                }
                else if (jobInfo.Status == JobStatus.Deleting)
                {
                    // Needed to ensure the Cancel/Bake button stays disabled while a job is in the deleting state.
                    m_cloudJobDeleting = true;
                    m_cloudJobStatus = "Cleaning up job resources...";
                }
                else // JobStatus.Completed
                {
                    string failedTasks = "";

                    if (jobInfo.Tasks != null && jobInfo.Tasks.Failed > 0)
                    {
                        failedTasks = String.Format(" ({0} tasks failed)", jobInfo.Tasks.Failed);
                    }

                    m_cloudJobStatus = $"Completed{failedTasks}. Downloading...";
                    QueueUIThreadAction(Repaint);

                    string aceFilename = s_AcousticsParameters.DataFileBaseName + ".ace.bytes";
                    
                    // Temporary location for download
                    string tempAcePath = Path.Combine(Path.GetTempPath(), aceFilename);

                    try
                    {
                        // DownloadAceFileAsync will throw on failure.
                        CloudProcessor.DownloadAceFileAsync(s_AcousticsParameters.ActiveJobID, tempAcePath).Wait();

                        // Copy to the project location and delete the temp file
                        string aceAsset = Path.Combine(s_AcousticsParameters.AcousticsDataFolder, aceFilename);
                        File.Copy(tempAcePath, aceAsset, true);
                        File.Delete(tempAcePath);
                    }
                    catch (Exception ex)
                    {
                        m_cloudJobStatus = "Error downloading ACE file, retrying. See console.";
                        m_progressMessage = ex.ToString();
                        QueueUIThreadAction(LogMessage, Repaint);
                        throw;
                    }

                    try
                    {
                        // Clean up the job and storage.
                        // This can sometimes throw with "service unavailable" in which case BakeService API will use re-try policy.
                        CloudProcessor.DeleteJobAsync(s_AcousticsParameters.ActiveJobID).Wait();
                    }
                    catch (Exception ex)
                    {
                        m_cloudJobStatus = "Failed to delete completed job, please use Azure Portal (https://portal.azure.com) to delete it. More info in the Console.";
                        m_progressMessage = ex.ToString();
                        QueueUIThreadAction(LogMessage, Repaint);
                        // Don't throw, we're done with the active job
                    }

                    m_cloudJobStatus = $"Downloaded{failedTasks}";
                    m_timerStartValue = 0;
                    s_AcousticsParameters.ActiveJobID = null;

                    QueueUIThreadAction(AddACEAsset);
                }

                QueueUIThreadAction(Repaint, MarkParametersDirty);
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = null;
                Monitor.Exit(this);
            }
        }

        /// <summary>
        /// When the ACE file is downloaded from the cloud, it will take Unity a while to find it and add it to the asset database unless we import it.
        /// This function imports the file to the asset database. This causes it to show up in the UI's Project window.
        /// </summary>
        public void AddACEAsset()
        {
            string ACEPath = Path.Combine(s_AcousticsParameters.AcousticsDataFolder, s_AcousticsParameters.DataFileBaseName + ".ace.bytes");
            ACEPath = Path.GetFullPath(ACEPath); // Normalize to ensure it's using standard path delimiters

            // ImportAsset requires a path relative to the application root, under Assets.
            int subPathIndex = ACEPath.ToLower().IndexOf($"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}") + 1;

            if (subPathIndex >= 0)
            {
                // Get a project relative path.
                string relativePath = ACEPath.Substring(subPathIndex);

                Debug.Log($"Importing file {relativePath}");
                AssetDatabase.ImportAsset(relativePath.Replace(Path.DirectorySeparatorChar, '/'), ImportAssetOptions.ForceUpdate);
            }
            else
            {
                Debug.LogWarning("The acoustics data folder is not inside the Assets folder!");
            }
        }

        /// <summary>
        /// Unity has a separate certificate store than the user's machine, so we have to do our own certificate validation in order to connect to Azure.
        /// </summary>
        public bool CertificateValidator(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            using (X509Certificate2 cert2 = new X509Certificate2(certificate))
            {
                // If verification fails (or throws e.g. on MacOS) we attempt to explicitly verify below 
                try
                {
                    // Certificate is trusted, return true
                    if (cert2.Verify())
                    {
                        return true;
                    }
                }
                catch (CryptographicException) 
                {
                    // Eat the exception, we'll validate the cert below
                }

                // Earlier verification attempt fails, so check explcitly.
                // Only trust an Azure domain certificate issued by Microsoft.
                return (cert2.IssuerName.Name.Contains("O=Microsoft Corporation") &&
                    (cert2.SubjectName.Name.Contains(".batch.azure.com") ||
                    cert2.SubjectName.Name.Contains(".core.windows.net") ||
                    cert2.SubjectName.Name.Contains("settings-win.data.microsoft.com")));
            }
        }

        /// <summary>
        /// Starts the thread used to start a cloud bake.
        /// </summary>
        void StartBake()
        {
            if (m_previewResults == null)
            {
                EditorUtility.DisplayDialog("Probes calculation missing!", "You must have completed a calculation using the Probes tab before submitting the bake!", "OK");
                return;
            }

            if (s_AcousticsParameters.NodeCount > m_previewResults.NumProbes)
            {
                if (!EditorUtility.DisplayDialog("Node count too high!", "It is a waste of resources to request more nodes than probes.\n\nDo you wish to adjust the node count to match the number of nodes?\n\n" +
                    "If so, the value will be adjusted and the bake submitted.\n\nMake sure your Azure Batch account has enough allocation!", "Adjust", "Cancel"))
                {
                    return;
                }

                s_AcousticsParameters.NodeCount = m_previewResults.NumProbes;
                MarkParametersDirty();
                Repaint();
            }

            m_workerThread = new System.Threading.Thread(SubmitBakeToAzure);
            m_workerThread.Start();
        }

        /// <summary>
        /// Start the timer used to check on the cloud bake status.
        /// </summary>
        /// <remarks>We only want to do this once the job has successfully been submitted, and this must be done on the UI thread.</remarks>
        void StartAzureCheckTimer()
        {
            m_cloudLastUpdateTime = DateTime.Now;
            m_timerStartValue = EditorApplication.timeSinceStartup;

            // This function is called right when the job is submitted. Mark the parameters data dirty so the JobID is saved to disk.
            MarkParametersDirty();
            Repaint();
        }

        /// <summary>
        /// Let Unity know that the acoustic parameters data has been changed so it will save the changes to disk.
        /// </summary>
        void MarkParametersDirty()
        {
            EditorUtility.SetDirty(s_AcousticsParameters);
        }

        /// <summary>
        /// Submit the cloud bake.
        /// </summary>
        /// <param name="threadData">Unused</param>
        void SubmitBakeToAzure(object threadData)
        {
            ComputePoolConfiguration poolConfiguration = s_AcousticsParameters.GetPoolConfiguration();

            CloudProcessor.AcousticsDockerImageName = s_AcousticsParameters.TritonImage;

            CloudProcessor.BatchAccount.Url = s_AcousticsParameters.AzureAccounts.BatchAccountUrl;
            CloudProcessor.BatchAccount.Name = s_AcousticsParameters.AzureAccounts.BatchAccountName;
            CloudProcessor.BatchAccount.Key = s_AcousticsParameters.AzureAccounts.BatchAccountKey;
            CloudProcessor.StorageAccount.Name = s_AcousticsParameters.AzureAccounts.StorageAccountName;
            CloudProcessor.StorageAccount.Key = s_AcousticsParameters.AzureAccounts.StorageAccountKey;

            JobConfiguration jobConfig = new JobConfiguration
            {
                Prefix = "U3D" + DateTime.Now.ToString("yyMMdd-HHmmssfff"),
                VoxFilepath = s_AcousticsParameters.VoxFilepath,
                ConfigurationFilepath = s_AcousticsParameters.ConfigFilepath,
            };

            m_cloudJobStatus = "Submitting (please wait)...";
            m_cloudLastUpdateTime = DateTime.Now;

            try
            {
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidator;

                Task<string> submitTask = CloudProcessor.SubmitForAnalysisAsync(poolConfiguration, jobConfig);
                submitTask.Wait();

                s_AcousticsParameters.ActiveJobID = submitTask.Result;
            }
            catch (Exception ex)
            {
                m_timerStartValue = 0;
                Exception curException = ex;

                // Make sure we log the exception
                m_cloudJobStatus = "An error occurred. See Console output.";
                m_progressMessage = ex.ToString();
                QueueUIThreadAction(LogMessage);

                while (curException != null)
                {
                    if (curException is WebException)
                    {
                        WebException we = curException as WebException;

                        if (we.Status == WebExceptionStatus.TrustFailure)
                        {
                            // If you hit this failure, set a breakpoint in the Certificate Validator function above
                            // and see if the certificate information has changed. If the cert looks valid, update the code
                            // to reflect the correct organization name or subject name.
                            m_cloudJobStatus = "Azure trust failure. Check scripts.";
                            throw new WebException("Connections to Azure web services are not trusted. The certificate check in the scripts may no longer match Azure certificates.", we);
                        }
                    }
                    else if (curException is Microsoft.Azure.Batch.AddTaskCollectionTerminatedException)
                    {
                        m_cloudJobStatus = "Error: Too many probes!\nEnsure the scene has a navmesh and try reducing the number of Acoustic Geometry objects";
                    }

                    curException = curException.InnerException;
                }
                throw;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = null;
            }

            m_cloudJobStatus = "Submitted Successfully.";

            QueueUIThreadAction(StartAzureCheckTimer);
        }

        /// <summary>
        /// Starts the thread that attempts to cancel a bake.
        /// </summary>
        void CancelBake()
        {
            if (String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
            {
                // Nothing to do
                return;
            }

            m_cloudJobStatus = "Canceling job (please wait)...";

            m_workerThread = new System.Threading.Thread(CancelBakeWorker);
            m_workerThread.Start();
        }

        /// <summary>
        /// Attempts to cancel a bake job in progress.
        /// </summary>
        void CancelBakeWorker()
        {
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidator;

                CloudProcessor.DeleteJobAsync(s_AcousticsParameters.ActiveJobID).Wait();
            }
            catch (Exception ex)
            {
                m_cloudJobStatus = "Job cancel failed. Use Azure Portal.";

                // Make sure we log the exception
                m_progressMessage = ex.ToString();
                QueueUIThreadAction(LogMessage);

                throw;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = null;
            }

            m_cloudJobStatus = "Cancel Request Sent.";

            QueueUIThreadAction(StartAzureCheckTimer, StartAzureStatusCheck);
        }
    }
}
