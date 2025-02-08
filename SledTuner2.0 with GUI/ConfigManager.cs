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
        /// Resets the configuration by reverting the sled parameters to their original values.
        /// This does not delete the configuration file.
        /// </summary>
        public void ResetParameters()
        {
            _sledParameterManager.ResetParameters();
            MelonLogger.Msg("[ConfigManager] Sled parameters reset to original values.");
        }

        /// <summary>
        /// Toggles the ragdoll state by navigating the object hierarchy:
        /// Snowmobile(Clone) => Body => IK Player (Drivers)
        /// and then toggling the 'enabled' property of the RagDollManager and RagDollCollisionController components.
        /// </summary>
        public void ToggleRagdoll()
        {
            MelonLogger.Msg("[ConfigManager] ToggleRagdoll called.");

            // First, find the Snowmobile(Clone) object.
            GameObject snowmobile = GameObject.Find("Snowmobile(Clone)");
            if (snowmobile == null)
            {
                MelonLogger.Warning("[ConfigManager] 'Snowmobile(Clone)' not found.");
                return;
            }

            // Find the Body child.
            Transform body = snowmobile.transform.Find("Body");
            if (body == null)
            {
                MelonLogger.Warning("[ConfigManager] 'Body' not found under Snowmobile(Clone).");
                return;
            }

            // Find the IK Player (Drivers) child.
            Transform driverTransform = body.Find("IK Player (Drivers)");
            if (driverTransform == null)
            {
                MelonLogger.Warning("[ConfigManager] 'IK Player (Drivers)' not found under Body.");
                return;
            }

            GameObject driverGO = driverTransform.gameObject;

            // Toggle RagDollManager.
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

            // Toggle RagDollCollisionController.
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
        /// Toggles the tree renderer state by finding the "LevelEssentials" object and toggling
        /// the active state of its "TreeRenderer" child.
        /// </summary>
        public void ToggleTreeRenderer()
        {
            MelonLogger.Msg("[ConfigManager] ToggleTreeRenderer called.");
            GameObject levelEssentials = GameObject.Find("LevelEssentials");
            if (levelEssentials == null)
            {
                MelonLogger.Msg("[ConfigManager] LevelEssentials not found.");
                return;
            }
            Transform treeRendererTransform = levelEssentials.transform.Find("TreeRenderer");
            if (treeRendererTransform == null)
            {
                MelonLogger.Msg("[ConfigManager] TreeRenderer not found under LevelEssentials.");
                return;
            }
            GameObject treeRenderer = treeRendererTransform.gameObject;
            bool currentState = treeRenderer.activeSelf;
            treeRenderer.SetActive(!currentState);
            MelonLogger.Msg($"[ConfigManager] TreeRenderer toggled to {!currentState}.");
        }

        /// <summary>
        /// Teleports the sled to a predetermined location.
        /// (Currently a stub; implement your teleportation logic here.)
        /// </summary>
        public void TeleportSled()
        {
            MelonLogger.Msg("[ConfigManager] TeleportSled called.");
            // TODO: Implement teleportation logic here.
        }

        /// <summary>
        /// Helper method to toggle the "enabled" property of a Component.
        /// </summary>
        private void ToggleComponentEnabled(Component comp)
        {
            var type = comp.GetType();
            var prop = type.GetProperty("enabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop != null && prop.CanRead && prop.CanWrite)
            {
                bool current = (bool)prop.GetValue(comp, null);
                prop.SetValue(comp, !current, null);
            }
        }
    }
}
