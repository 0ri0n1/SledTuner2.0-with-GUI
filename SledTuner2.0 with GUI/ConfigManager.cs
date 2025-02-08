using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace SledTunerProject
{
    public class ConfigManager
    {
        private readonly SledParameterManager _sledParameterManager;
        private string _configFilePath;
        private bool _isInitialized = false;

        // Event fired after configuration is successfully loaded (or reset) so that GUI can update.
        public event Action ConfigurationLoaded;

        public ConfigManager(SledParameterManager sledParameterManager)
        {
            _sledParameterManager = sledParameterManager ?? throw new ArgumentNullException(nameof(sledParameterManager));
        }

        /// <summary>
        /// Updates the configuration file path based on the sled's name.
        /// </summary>
        private void UpdateConfigFilePath()
        {
            // Use the sled's name to create a safe filename.
            string sledName = _sledParameterManager.GetSledName() ?? "UnknownSled";
            string safeSledName = Utilities.MakeSafeFileName(sledName);
            _configFilePath = Path.Combine(_sledParameterManager.JsonFolderPath, $"{safeSledName}.json");
        }

        /// <summary>
        /// Waits for the snowmobile to appear in the scene and then initializes the configuration.
        /// </summary>
        public IEnumerator WaitForSnowmobileAndInitialize()
        {
            while (!_isInitialized)
            {
                GameObject snowmobile = GameObject.Find("Snowmobile(Clone)");
                if (snowmobile != null)
                {
                    MelonLogger.Msg("[ConfigManager] Snowmobile found. Initializing configuration.");
                    InitializeIfNeeded();
                    yield break;
                }
                MelonLogger.Msg("[ConfigManager] Snowmobile not found. Retrying in 1 second...");
                yield return new WaitForSeconds(1f);
            }
        }

        /// <summary>
        /// Initializes the configuration if it has not yet been done.
        /// </summary>
        public void InitializeIfNeeded()
        {
            if (_isInitialized)
            {
                MelonLogger.Msg("[ConfigManager] Already initialized.");
                return;
            }
            MelonLogger.Msg("[ConfigManager] Attempting initialization...");
            UpdateConfigFilePath();
            string sledName = _sledParameterManager.GetSledName();
            if (string.IsNullOrEmpty(sledName))
            {
                MelonLogger.Warning("[ConfigManager] Initialization failed: Sled name not found.");
                return;
            }
            MelonLogger.Msg($"[ConfigManager] Initialized for sled: {sledName}");
            _isInitialized = true;
        }

        /// <summary>
        /// Saves the current configuration asynchronously.
        /// </summary>
        public async void SaveConfiguration()
        {
            InitializeIfNeeded();
            if (string.IsNullOrEmpty(_configFilePath))
            {
                MelonLogger.Warning("[ConfigManager] Config file path is not set. Aborting save.");
                return;
            }

            var data = _sledParameterManager.GetCurrentParameters();
            try
            {
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                await Task.Run(() => File.WriteAllText(_configFilePath, json));
                MelonLogger.Msg($"[ConfigManager] Configuration saved to: {_configFilePath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Error saving configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the configuration asynchronously.
        /// </summary>
        public async void LoadConfiguration()
        {
            InitializeIfNeeded();
            if (string.IsNullOrEmpty(_configFilePath) || !File.Exists(_configFilePath))
            {
                MelonLogger.Warning($"[ConfigManager] Config file not found: {_configFilePath}");
                return;
            }

            try
            {
                string json = await Task.Run(() => File.ReadAllText(_configFilePath));
                var data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(json);
                if (data != null)
                {
                    _sledParameterManager.SetParameters(data);
                    MelonLogger.Msg($"[ConfigManager] Loaded configuration from: {_configFilePath}");
                    // Notify subscribers (e.g., GUIManager) to update their fields.
                    ConfigurationLoaded?.Invoke();
                }
                else
                {
                    MelonLogger.Warning("[ConfigManager] Loaded configuration is null.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Error loading configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the configuration by reverting parameters to their original values.
        /// This does not delete the configuration file.
        /// </summary>
        public void ResetParameters()
        {
            _sledParameterManager.ResetParameters();
            MelonLogger.Msg("[ConfigManager] Sled parameters reset to original values.");
            // Notify subscribers so that GUI updates.
            ConfigurationLoaded?.Invoke();
        }

        /// <summary>
        /// Toggles the ragdoll state by enabling/disabling related components.
        /// </summary>
        public void ToggleRagdoll()
        {
            MelonLogger.Msg("[ConfigManager] ToggleRagdoll called.");
            // Look in the correct hierarchy: Snowmobile(Clone) => Body => IK Player (Drivers)
            GameObject driverGO = GameObject.Find("Snowmobile(Clone)/Body/IK Player (Drivers)");
            if (driverGO == null)
            {
                MelonLogger.Warning("[ConfigManager] 'IK Player (Drivers)' not found under Snowmobile(Clone)/Body.");
                return;
            }

            // Toggle RagDollManager
            Component ragdollManager = driverGO.GetComponent("RagDollManager");
            if (ragdollManager != null)
            {
                ToggleComponentEnabled(ragdollManager);
                MelonLogger.Msg("[ConfigManager] RagDollManager toggled.");
            }
            else
            {
                MelonLogger.Msg("[ConfigManager] RagDollManager component not found.");
            }

            // Toggle RagDollCollisionController
            Component ragdollCollision = driverGO.GetComponent("RagDollCollisionController");
            if (ragdollCollision != null)
            {
                ToggleComponentEnabled(ragdollCollision);
                MelonLogger.Msg("[ConfigManager] RagDollCollisionController toggled.");
            }
            else
            {
                MelonLogger.Msg("[ConfigManager] RagDollCollisionController component not found.");
            }
        }

        /// <summary>
        /// Toggles the tree renderer state.
        /// </summary>
        public void ToggleTreeRenderer()
        {
            MelonLogger.Msg("[ConfigManager] ToggleTreeRenderer called.");
            GameObject levelEssentials = GameObject.Find("LevelEssentials");
            if (levelEssentials == null)
            {
                MelonLogger.Warning("[ConfigManager] 'LevelEssentials' not found.");
                return;
            }
            Transform treeRendererTransform = levelEssentials.transform.Find("TreeRenderer");
            if (treeRendererTransform == null)
            {
                MelonLogger.Warning("[ConfigManager] 'TreeRenderer' not found under LevelEssentials.");
                return;
            }
            GameObject treeRenderer = treeRendererTransform.gameObject;
            bool newState = !treeRenderer.activeSelf;
            treeRenderer.SetActive(newState);
            MelonLogger.Msg($"[ConfigManager] TreeRenderer toggled to {(newState ? "ON" : "OFF")}.");
        }

        /// <summary>
        /// Teleports the sled. (Teleport logic remains for future integration.)
        /// </summary>
        public void TeleportSled()
        {
            MelonLogger.Msg("[ConfigManager] TeleportSled called.");
            // TODO: Implement teleportation logic here.
        }

        /// <summary>
        /// Helper method to toggle a component’s enabled state via its 'enabled' property.
        /// </summary>
        private void ToggleComponentEnabled(Component comp)
        {
            var prop = comp.GetType().GetProperty("enabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop != null && prop.CanRead && prop.CanWrite)
            {
                bool currentState = (bool)prop.GetValue(comp, null);
                prop.SetValue(comp, !currentState, null);
            }
        }
    }
}
