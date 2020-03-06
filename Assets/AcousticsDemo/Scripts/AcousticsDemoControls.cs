// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.AcousticsDemo.Scripts
{
    [DisallowMultipleComponent]
    public class AcousticsDemoControls : MonoBehaviour
    {
        enum DemoAction
        {
            PlaceSource = 0,
            ShootSource,
            ChangeAudioContent,
            PlayPause,
            SelectNextSource,
            ToggleAcoustics,
            Teleport
        }

        enum TeleportLocation
        {
            Hole1,
            Hole2,
            Hole3,
            Hole4,
            Hole5,
            Hole6,
            Hole7,
            Hole8,
            Hole9
        }

        DemoAction m_activeAction = DemoAction.PlaceSource;
        public GameObject CameraHolder;
        public List<GameObject> AudioSources;
        private int m_activeAudioSource = 0;
        public TextMesh ActionLabel;
        public TextMesh AcousticsLabel;
        public TextMesh HelpTextLabel;
        private List<AcousticsDemoSource> m_demoSources = new List<AcousticsDemoSource>();
        private CameraController m_cameraController;
        private TeleportLocation m_nextTeleport = TeleportLocation.Hole1;
        private AcousticsManager m_acousticsManager;
        public List<GameObject> TeePylons;
        public List<TextAsset> AceFiles;
        private int m_activeAceFile = 0;
        public GameObject Hub;

        private static bool isGameFocused = true;
        public static bool IsGameFocused() { return isGameFocused; }
        

        private void Start()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;

            for (int i = 0; i < AudioSources.Count; i++)
            {
                m_demoSources.Add(AudioSources[i].GetComponent<AcousticsDemoSource>());
            }
            m_cameraController = CameraHolder.GetComponentInChildren<CameraController>();
            m_acousticsManager = GetComponent<AcousticsManager>();
            m_activeAceFile = 0;

            // When we start, the first source is the one that we are operating on
            m_demoSources[0].Select();

            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void Update()
        {
#if UNITY_EDITOR
            // Handle focus changes in the editor. This isn't a problem in standalone game
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                isGameFocused = false;
                return;
            }
            if (!isGameFocused)
            {
                // If we registered a click, the game is focused again
                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                {
                    isGameFocused = true;
                }
                // Always eat the first click, or early return (because we don't have focus)
                return;
            }
#endif

#if UNITY_ANDROID
            // On Oculus Go, this is the trigger
            if (Input.GetKeyDown(KeyCode.JoystickButton15))
#else
            // On desktop, this is the left mouse button or the trigger on WMR remote
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.JoystickButton9))
#endif
            {
                DoAction(m_activeAction);
            }

#if UNITY_ANDROID
            // On Oculus Go, this is the back button
            if (Input.GetKeyDown(KeyCode.Escape))
#else
            // On desktop, this is the right mouse button or the grip button on WMR remote
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.JoystickButton1))
#endif
            {
                if ((int)m_activeAction < Enum.GetValues(typeof(DemoAction)).Length - 1)
                {
                    m_activeAction += 1;
                }
                else
                {
                    m_activeAction = DemoAction.PlaceSource;
                }
                ActionLabel.text = "Action: " + m_activeAction.ToString();
            }

#if !UNITY_ANDROID
            CheckKeyboardShortcuts();
#endif
        }

        void DoAction(DemoAction action)
        {
            switch (action)
            {
                case DemoAction.PlaceSource:
                {
                    PlaceSourceHere();
                    break;
                }
                case DemoAction.ShootSource:
                {
                    ShootSource();
                    break;
                }
                case DemoAction.ToggleAcoustics:
                {
                    CycleAceFiles();
                    break;
                }
                case DemoAction.ChangeAudioContent:
                {
                        m_demoSources[m_activeAudioSource].NextClip();
                    break;
                }
                case DemoAction.Teleport:
                {
                    TeleportToHole((int)m_nextTeleport);
                    if ((int)m_nextTeleport < Enum.GetValues(typeof(TeleportLocation)).Length)
                    {
                        m_nextTeleport += 1;
                    }
                    else
                    {
                        m_nextTeleport = TeleportLocation.Hole1;
                    }
                    break;
                }
                case DemoAction.SelectNextSource:
                {
                    SelectNextSource();
                    break;
                }
                case DemoAction.PlayPause:
                {
                    m_demoSources[m_activeAudioSource].PlayPause();
                    break;
                }
                default:
                {
                    throw new InvalidOperationException("Unexpected DemoAction");
                }
            }
        }

        void PlaceSourceHere()
        {
            var rigidBody = AudioSources[m_activeAudioSource].GetComponent<Rigidbody>();
            rigidBody.velocity = new Vector3(0, 0, 0);
            AudioSources[m_activeAudioSource].transform.position = Camera.main.transform.position + (Camera.main.transform.forward * 3);
        }

        void ShootSource()
        {
            // First, put the source in front of the user, then accelerate it forward
            PlaceSourceHere();
            var rigidBody = AudioSources[m_activeAudioSource].GetComponent<Rigidbody>();
            rigidBody.velocity = Camera.main.transform.forward * 15;
        }

        void SelectNextSource()
        {
            m_demoSources[m_activeAudioSource].Deselect();
            m_activeAudioSource += 1;
            if (m_activeAudioSource >= AudioSources.Count)
            {
                m_activeAudioSource = 0;
            }
            m_demoSources[m_activeAudioSource].Select();
        }

        void CycleAceFiles()
        {
            if (m_activeAceFile == AceFiles.Count)
            {
                m_acousticsManager.AceFile = null;
                m_activeAceFile = 0;
                AcousticsLabel.text = "Acoustics: Disabled";
                AcousticsLabel.color = Color.red;
            }
            else
            {
                m_acousticsManager.AceFile = AceFiles[m_activeAceFile];
                AcousticsLabel.text = "Acoustics: " + ((m_activeAceFile == 0) ? "Coarse Bake" : "Fine Bake");
                AcousticsLabel.color = Color.green;
                m_activeAceFile += 1;
            }
        }

        void TeleportToHole(int index)
        {
            if (index == TeePylons.Count)
            {
                TeleportHome();
            }
            else
            {
                var direction = TeePylons[index].transform.position - Hub.transform.position;
                CameraHolder.transform.position = TeePylons[index].transform.position + new Vector3(0, 2, 0) - direction.normalized * 10;
                m_cameraController.SetRotation(TeePylons[index].transform.parent.transform.rotation.eulerAngles);
            }
        }

        void TeleportHome()
        {
            CameraHolder.transform.position = new Vector3(0, 2, -6);
            m_cameraController.SetRotation(new Vector3(0, 0, 0));
        }

        void CheckKeyboardShortcuts()
        {
            // Teleportation
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                TeleportHome();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                TeleportToHole(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                TeleportToHole(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                TeleportToHole(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                TeleportToHole(3);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                TeleportToHole(4);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                TeleportToHole(5);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                TeleportToHole(6);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha8))
            {
                TeleportToHole(7);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                TeleportToHole(8);
            }

            // Other controls
            if (Input.GetKeyDown(KeyCode.F))
            {
                PlaceSourceHere();
            }
            if (Input.GetKeyDown(KeyCode.G))
            {
                ShootSource();
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                CycleAceFiles();
            }
            if (Input.GetKeyDown(KeyCode.C))
            {
                m_demoSources[m_activeAudioSource].NextClip();
            }
            if (Input.GetKeyDown(KeyCode.X))
            {
                m_demoSources[m_activeAudioSource].PlayPause();
            }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                SelectNextSource();
            }

            // Help
            if (Input.GetKeyDown(KeyCode.F1))
            {
                ToggleHelp();
            }
        }

        void ToggleHelp()
        {
            HelpTextLabel.gameObject.SetActive(!HelpTextLabel.gameObject.activeInHierarchy);
        }

        /*
        static int[] validSpeakerModes =
        {
            (int)AudioSpeakerMode.Mono,
            (int)AudioSpeakerMode.Stereo,
            (int)AudioSpeakerMode.Quad,
            (int)AudioSpeakerMode.Surround,
            (int)AudioSpeakerMode.Mode5point1,
            (int)AudioSpeakerMode.Mode7point1
        };
        */

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            /*
            if (deviceWasChanged)
            {
                var config = AudioSettings.GetConfiguration();
                config.dspBufferSize = 64;
                AudioSettings.Reset(config);
            }
            */
            m_demoSources[m_activeAudioSource].PlayPause();
        }
    }
}
