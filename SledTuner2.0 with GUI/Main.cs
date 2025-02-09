using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using HarmonyLib;

[assembly: MelonInfo(typeof(SledTunerProject.Main), "SledTunerGUI", "1.0.0", "YourName")]
[assembly: MelonGame("Hanki Games", "Sledders")]

namespace SledTunerProject
{
    public class Main : MelonMod
    {
        // Managers for handling parameter inspection, configuration, and GUI.
        private SledParameterManager _sledParameterManager;
        private ConfigManager _configManager;
        private GUIManager _guiManager;

        // Use the fully-qualified type to avoid conflicts with the Harmony namespace.
        private HarmonyLib.Harmony _harmony;

        // Scenes considered “valid” for auto-initializing the sled.
        private readonly HashSet<string> _validScenes = new HashSet<string>
        {
            "Woodland",
            "Side_Cliffs_03_27",
            "Idaho",
            "Rocky Mountains",
            "Valley"
        };

        // Scenes considered “invalid” for our tuner.
        private readonly HashSet<string> _invalidScenes = new HashSet<string>
        {
            "TitleScreen",
            "Garage"
        };

        private bool _initialized = false;

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("[SledTuner] OnInitializeMelon => Start setting up the mod...");

            // Initialize Harmony and apply all patches in the assembly.
            _harmony = new HarmonyLib.Harmony("your.unique.modid.sledtuner");
            _harmony.PatchAll();
            MelonLogger.Msg("[SledTuner] Harmony patches applied.");

            MelonLogger.Msg($"[SledTuner] Valid scenes: {string.Join(", ", _validScenes)}");
            MelonLogger.Msg($"[SledTuner] Invalid scenes: {string.Join(", ", _invalidScenes)}");

            // Initialize the manager classes.
            _sledParameterManager = new SledParameterManager();
            _configManager = new ConfigManager(_sledParameterManager);
            _guiManager = new GUIManager(_sledParameterManager, _configManager);

            MelonLogger.Msg("[SledTuner] Mod setup complete. Will initialize sled after loading a valid scene or on F2/F3 press.");

            // Subscribe to scene load events.
            SceneManager.sceneLoaded += OnSceneWasLoaded;
        }

        private void OnSceneWasLoaded(Scene scene, LoadSceneMode mode)
        {
            MelonLogger.Msg($"[SledTuner] OnSceneWasLoaded: {scene.name}");

            if (_validScenes.Contains(scene.name))
            {
                MelonLogger.Msg($"[SledTuner] Scene '{scene.name}' is valid. Auto-initializing sled...");
                TryInitializeSled();
            }
            else if (_invalidScenes.Contains(scene.name))
            {
                MelonLogger.Msg($"[SledTuner] Scene '{scene.name}' is invalid. Marking as uninitialized.");
                _initialized = false;
            }
            else
            {
                MelonLogger.Msg($"[SledTuner] Scene '{scene.name}' not explicitly marked; marking uninitialized by default.");
                _initialized = false;
            }
        }

        private void TryInitializeSled()
        {
            if (_initialized)
            {
                MelonLogger.Msg("[SledTuner] Sled is already initialized, skipping re-init.");
                return;
            }

            MelonLogger.Msg("[SledTuner] Calling _sledParameterManager.InitializeComponents() now...");
            _sledParameterManager.InitializeComponents();

            if (_sledParameterManager.IsInitialized)
            {
                MelonLogger.Msg("[SledTuner] SledParameterManager reports IsInitialized = TRUE. Populating GUI fields now.");
                _guiManager.RePopulateFields();
                _initialized = true;
            }
            else
            {
                MelonLogger.Warning("[SledTuner] SledParameterManager.IsInitialized is FALSE, something failed to load the sled or Spot Light.");
            }
        }

        public override void OnUpdate()
        {
            // F2 toggles the GUI and attempts to initialize the sled if in a valid scene.
            if (Input.GetKeyDown(KeyCode.F2))
            {
                MelonLogger.Msg("[SledTuner] F2 pressed => Toggling menu.");
                _guiManager.ToggleMenu();

                string activeScene = SceneManager.GetActiveScene().name;
                if (_validScenes.Contains(activeScene))
                {
                    MelonLogger.Msg($"[SledTuner] Current scene '{activeScene}' is valid. Attempting to initialize sled on F2 press.");
                    TryInitializeSled();
                }
                else
                {
                    MelonLogger.Msg($"[SledTuner] Current scene '{activeScene}' not valid for auto-init.");
                }
            }

            // F3 forces re-initialization.
            if (Input.GetKeyDown(KeyCode.F3))
            {
                MelonLogger.Msg("[SledTuner] F3 pressed => Forcing re-initialization.");
                _initialized = false;
                TryInitializeSled();
            }
        }

        public override void OnGUI()
        {
            _guiManager?.DrawMenu();
        }
    }
}
