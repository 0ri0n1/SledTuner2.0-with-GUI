using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

[assembly: MelonInfo(typeof(SledTunerProject.Main), "SledTunerGUI", "2.0.0", "SledTuner Team")]
[assembly: MelonGame("Hanki Games", "Sledders")]

namespace SledTunerProject
{
    public class Main : MelonMod
    {
        private SledParameterManager _sledParameterManager;
        private ConfigManager _configManager;
        private GUIManager _guiManager;
        private TeleportManager _teleportManager;
        
        // Maximum number of automatic retry attempts
        private const int MAX_AUTO_RETRIES = 3;
        private int _currentRetryAttempt = 0;
        private float _retryDelay = 2.0f;
        private bool _retryScheduled = false;

        // Scenes considered "valid" for auto-initializing the sled.
        private readonly HashSet<string> _validScenes = new HashSet<string>
        {
            "Woodland",
            "Side_Cliffs_03_27",
            "Idaho",
            "Rocky Mountains",
            "Valley"
        };

        // Scenes considered "invalid" for our tuner.
        private readonly HashSet<string> _invalidScenes = new HashSet<string>
        {
            "TitleScreen",
            "Garage"
        };

        private bool _initialized = false;
        private bool _diagnosticsMode = false;
        private bool _bonerUIBlocked = false;

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("[SledTuner] OnInitializeMelon => Start setting up the mod...");

            // Try to find and block any "Boner" UI elements from other mods
            if (!_bonerUIBlocked)
            {
                // Find and unload any problematic mods
                var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        if (assembly.FullName.ToLower().Contains("boner"))
                        {
                            MelonLogger.Msg($"[SledTuner] Found potentially conflicting mod: {assembly.FullName}");
                            _bonerUIBlocked = true;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"[SledTuner] Error inspecting assembly: {ex.Message}");
                    }
                }
            }

            MelonLogger.Msg($"[SledTuner] Valid scenes: {string.Join(", ", _validScenes)}");
            MelonLogger.Msg($"[SledTuner] Invalid scenes: {string.Join(", ", _invalidScenes)}");

            // Create core managers - SledParameterManager and ConfigManager
            if (_sledParameterManager == null)
                _sledParameterManager = new SledParameterManager();
                
            if (_configManager == null)
                _configManager = new ConfigManager(_sledParameterManager);
            
            // Create teleport manager
            _teleportManager = new TeleportManager();
            
            try
            {
                // Create GUI manager
                _guiManager = new GUIManager(_sledParameterManager, _configManager, _teleportManager);
                MelonLogger.Msg("GUI Manager created successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error creating GUI Manager: {ex.Message}");
            }

            MelonLogger.Msg("[SledTuner] Mod setup complete. Will initialize sled after loading a valid scene or on F2/F3 press.");

            // Schedule the creation of the racing preset
            MelonCoroutines.Start(CreateRacingPresetWhenReady());

            // Subscribe to scene loading events
            SceneManager.sceneLoaded -= OnSceneWasLoadedInternal;
            SceneManager.sceneLoaded += OnSceneWasLoadedInternal;
        }

        private void OnSceneWasLoadedInternal(Scene scene, LoadSceneMode mode)
        {
            // Call our OnSceneWasLoaded method with the scene name and build index
            OnSceneWasLoaded(scene.buildIndex, scene.name);
        }

        /// <summary>
        /// Checks if the scene is a valid game scene where the mod should be active
        /// </summary>
        private bool IsValidGameScene(string sceneName)
        {
            return _validScenes.Contains(sceneName);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"Scene loaded: {sceneName} (buildIndex: {buildIndex})");

            // Reset managers when scene changes
            if (_sledParameterManager != null)
            {
                _sledParameterManager.Reset();
            }
            
            if (_teleportManager != null)
            {
                _teleportManager.Reset();
            }
            
            _currentRetryAttempt = 0;
            _retryScheduled = false;
            
            // Initialize managers in valid scenes
            if (IsValidGameScene(sceneName))
            {
                MelonLogger.Msg($"[SledTuner] Scene '{sceneName}' is valid. Auto-initializing sled...");
                // Start initialization with retry capability
                if (!_initialized)
                {
                    MelonCoroutines.Start(DelayedInitializeSled(1.0f));
                }
                
                if (_teleportManager != null)
                {
                    MelonCoroutines.Start(DelayedInitializeTeleporter());
                }
            }
            else if (_invalidScenes.Contains(sceneName))
            {
                MelonLogger.Msg($"[SledTuner] Scene '{sceneName}' is invalid. Marking as uninitialized.");
                _initialized = false;
            }
            else
            {
                MelonLogger.Msg($"[SledTuner] Scene '{sceneName}' not explicitly marked; marking uninitialized by default.");
                _initialized = false;
            }
        }

        /// <summary>
        /// Coroutine to attempt initialization after a delay
        /// </summary>
        private IEnumerator DelayedInitializeSled(float delay)
        {
            _retryScheduled = true;
            yield return new WaitForSeconds(delay);
            _retryScheduled = false;
            TryInitializeSled();
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
                _currentRetryAttempt = 0; // Reset retry counter on success
            }
            else
            {
                MelonLogger.Warning("[SledTuner] SledParameterManager.IsInitialized is FALSE, something failed to initialize.");
                
                // Log specific errors from the last attempt
                if (_sledParameterManager.LastInitializationErrors.Count > 0)
                {
                    MelonLogger.Warning("[SledTuner] Initialization errors:");
                    foreach (var error in _sledParameterManager.LastInitializationErrors)
                    {
                        MelonLogger.Warning($"[SledTuner]   - {error.Key}: {error.Value}");
                    }
                }
                
                // Auto-retry if we haven't exceeded the limit
                if (_currentRetryAttempt < MAX_AUTO_RETRIES && !_retryScheduled)
                {
                    _currentRetryAttempt++;
                    float backoffDelay = _retryDelay * _currentRetryAttempt; // Exponential backoff
                    MelonLogger.Msg($"[SledTuner] Scheduling retry #{_currentRetryAttempt} in {backoffDelay} seconds...");
                    MelonCoroutines.Start(DelayedInitializeSled(backoffDelay));
                }
                else if (_currentRetryAttempt >= MAX_AUTO_RETRIES)
                {
                    MelonLogger.Error($"[SledTuner] Failed to initialize after {MAX_AUTO_RETRIES} attempts. Press F3 to try again manually.");
                }
            }
        }

        private IEnumerator DelayedInitializeTeleporter()
        {
            MelonLogger.Msg("Starting delayed teleporter initialization...");
            
            // Wait for 3 seconds to allow the scene to fully load
            yield return new WaitForSeconds(3f);
            
            // Try to initialize teleport manager
            int attempts = 0;
            bool success = false;
            
            while (!success && attempts < 3)
            {
                attempts++;
                MelonLogger.Msg($"Initializing teleporter (attempt {attempts})...");
                
                bool shouldWait = false;
                
                try
                {
                    success = _teleportManager.Initialize();
                    
                    if (success)
                    {
                        MelonLogger.Msg("Teleporter initialized successfully");
                    }
                    else
                    {
                        MelonLogger.Warning($"Teleporter initialization failed on attempt {attempts}");
                        shouldWait = (attempts < 3);
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error in teleporter initialization: {ex.Message}");
                    shouldWait = (attempts < 3);
                }
                
                // Wait outside the try/catch block
                if (shouldWait)
                {
                    yield return new WaitForSeconds(2f);
                }
            }
        }

        public override void OnUpdate()
        {
            // F2 to toggle menu
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

            // F3 to force re-initialization
            if (Input.GetKeyDown(KeyCode.F3))
            {
                MelonLogger.Msg("[SledTuner] F3 pressed => Forcing re-initialization.");
                _initialized = false;
                _currentRetryAttempt = 0; // Reset retry counter
                TryInitializeSled();
            }
            
            // F4 to toggle diagnostics mode
            if (Input.GetKeyDown(KeyCode.F4))
            {
                _diagnosticsMode = !_diagnosticsMode;
                MelonLogger.Msg($"[SledTuner] F4 pressed => Diagnostics mode {(_diagnosticsMode ? "enabled" : "disabled")}");
                _guiManager.SetDiagnosticsMode(_diagnosticsMode);
            }
            
            // F5 to load default preset
            if (Input.GetKeyDown(KeyCode.F5))
            {
                MelonLogger.Msg("[SledTuner] F5 pressed => Loading default configuration.");
                _configManager.LoadConfiguration();
            }
            
            // F6 to save default preset
            if (Input.GetKeyDown(KeyCode.F6))
            {
                MelonLogger.Msg("[SledTuner] F6 pressed => Saving current parameters as default.");
                _configManager.SaveConfiguration();
            }
            
            // Ctrl+F12 to reset parameters
            if (Input.GetKeyDown(KeyCode.F12) && Input.GetKey(KeyCode.LeftControl))
            {
                MelonLogger.Msg("[SledTuner] Ctrl+F12 pressed => Resetting to original parameters.");
                _configManager.ResetParameters();
            }

            // Handle teleport functionality through the TeleportManager
            if (_teleportManager != null && _teleportManager.IsInitialized)
            {
                _teleportManager.ProcessInput();
            }
        }

        public override void OnGUI()
        {
            // Block other GUI windows by setting GUI.skin to null and back
            // This is a trick to prevent other mods' or game's GUI windows from rendering
            GUISkin originalSkin = GUI.skin;
            
            // Always use the main instance for drawing our menu
            GUIManager.Instance?.DrawMenu();
            
            // Show diagnostics overlay when in diagnostics mode
            if (_diagnosticsMode)
            {
                DrawDiagnosticsOverlay();
            }
            
            // Restore original skin
            GUI.skin = originalSkin;
        }
        
        /// <summary>
        /// Draw diagnostics information as an overlay when in diagnostics mode
        /// </summary>
        private void DrawDiagnosticsOverlay()
        {
            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.normal.textColor = Color.white;
            style.fontSize = 14;
            style.alignment = TextAnchor.UpperLeft;
            
            int width = 400;
            int height = 300;
            int padding = 10;
            
            GUI.Box(new Rect(Screen.width - width - padding, padding, width, height), "", style);
            
            GUILayout.BeginArea(new Rect(Screen.width - width - padding + 5, padding + 5, width - 10, height - 10));
            
            GUILayout.Label("<b>SledTuner Diagnostics</b>", style);
            GUILayout.Space(5);
            
            GUILayout.Label($"Initialized: {_initialized}", style);
            GUILayout.Label($"Current Scene: {SceneManager.GetActiveScene().name}", style);
            GUILayout.Label($"Initialization Attempts: {_sledParameterManager.InitializationAttempts}", style);
            GUILayout.Label($"Auto-retry Attempts: {_currentRetryAttempt}/{MAX_AUTO_RETRIES}", style);
            
            if (_sledParameterManager.LastInitializationErrors.Count > 0)
            {
                GUILayout.Label("<b>Last Initialization Errors:</b>", style);
                foreach (var error in _sledParameterManager.LastInitializationErrors)
                {
                    GUILayout.Label($"- {error.Key}: {error.Value}", style);
                }
            }
            
            GUILayout.EndArea();
        }

        // Method to create a racing preset from the provided JSON values
        private void CreateRacingPreset()
        {
            try
            {
                if (_configManager == null || _sledParameterManager == null || !_sledParameterManager.IsInitialized)
                {
                    MelonLogger.Error("[SledTuner] Cannot create racing preset: Managers not initialized");
                    return;
                }

                // Racing preset parameters from Yuhana_Sender
                var racingParams = new Dictionary<string, Dictionary<string, object>>
                {
                    ["SnowmobileController"] = new Dictionary<string, object>
                    {
                        ["leanSteerFactorSoft"] = 1.8f,
                        ["leanSteerFactorTrail"] = 2.3f,
                        ["throttleExponent"] = 2.8f,
                        ["drowningDepth"] = 2.5f,
                        ["drowningTime"] = 3.0f,
                        ["isEngineOn"] = true,
                        ["isStuck"] = false,
                        ["canRespawn"] = true,
                        ["hasDrowned"] = false,
                        ["rpmSensitivity"] = 0.35f,
                        ["rpmSensitivityDown"] = 0.3f,
                        ["minThrottleOnClutchEngagement"] = 0.2f,
                        ["clutchRpmMin"] = 3000.0f,
                        ["clutchRpmMax"] = 10500.0f,
                        ["isHeadlightOn"] = true,
                        ["wheelieThreshold"] = -40.0f
                    },
                    ["SnowmobileControllerBase"] = new Dictionary<string, object>
                    {
                        ["skisMaxAngle"] = 55.0f,
                        ["driverZCenter"] = -0.15f,
                        ["enableVerticalWeightTransfer"] = true,
                        ["trailLeanDistance"] = 0.25f,
                        ["switchbackTransitionTime"] = 0.1f
                    },
                    ["MeshInterpretter"] = new Dictionary<string, object>
                    {
                        ["power"] = 750000.0f,
                        ["powerEfficiency"] = 0.97f,
                        ["breakForce"] = 14000.0f,
                        ["frictionForce"] = 1000.0f,
                        ["trackMass"] = 22.0f,
                        ["coefficientOfFriction"] = 1.4f,
                        ["snowPushForceFactor"] = 85.0f,
                        ["snowPushForceNormalizedFactor"] = 450.0f,
                        ["snowSupportForceFactor"] = 750.0f,
                        ["maxSupportPressure"] = 1.4f,
                        ["lugHeight"] = 0.25f,
                        ["snowOutTrackWidth"] = 0.45f,
                        ["pitchFactor"] = 10.0f
                    },
                    ["SnowParameters"] = new Dictionary<string, object>
                    {
                        ["snowNormalConstantFactor"] = 1.2f,
                        ["snowNormalDepthFactor"] = 3.0f,
                        ["snowFrictionFactor"] = 0.1f
                    },
                    ["SuspensionController"] = new Dictionary<string, object>
                    {
                        ["suspensionSubSteps"] = 320,
                        ["antiRollBarFactor"] = 15000.0f,
                        ["skiAutoTurn"] = true,
                        ["trackRigidityFront"] = 32.0f,
                        ["trackRigidityRear"] = 34.0f
                    },
                    ["Stabilizer"] = new Dictionary<string, object>
                    {
                        ["trackSpeedGyroMultiplier"] = 40.0f,
                        ["idleGyro"] = 300.0f
                    },
                    ["Rigidbody"] = new Dictionary<string, object>
                    {
                        ["mass"] = 180.0f,
                        ["drag"] = 7.45227051f,
                        ["angularDrag"] = 7.45569372f,
                        ["useGravity"] = true,
                        ["maxAngularVelocity"] = 22.0f
                    }
                };

                // Store the current parameters
                var currentParams = _sledParameterManager.GetCurrentParameters();

                // Apply racing parameters temporarily
                _sledParameterManager.SetParameters(racingParams);

                // Save as "Racing" preset
                _configManager.SavePreset("Racing", "Optimal racing configuration based on Yuhana_Sender settings");

                // Restore original parameters
                _sledParameterManager.SetParameters(currentParams);

                MelonLogger.Msg("[SledTuner] Racing preset created successfully!");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[SledTuner] Error creating racing preset: {ex.Message}");
            }
        }

        private IEnumerator CreateRacingPresetWhenReady()
        {
            // Wait until the ConfigManager is initialized and we're in a valid scene
            for (int i = 0; i < 30; i++) // Try for about 30 seconds
            {
                if (_configManager != null && _sledParameterManager != null && _sledParameterManager.IsInitialized)
                {
                    MelonLogger.Msg("[SledTuner] Creating racing preset...");
                    CreateRacingPreset();
                    yield break;
                }
                yield return new WaitForSeconds(1.0f);
            }
            MelonLogger.Warning("[SledTuner] Timed out waiting to create racing preset. Will try again when sled is initialized.");
            
            // If we couldn't create it during startup, set up an event to try again after manual initialization
            _sledParameterManager.InitializationComplete += (sender, args) => {
                MelonLogger.Msg("[SledTuner] SledParameterManager initialized, creating racing preset...");
                CreateRacingPreset();
            };
        }
    }
}
