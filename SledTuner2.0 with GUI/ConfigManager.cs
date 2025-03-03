using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace SledTunerProject
{
    /// <summary>
    /// Configuration preset class that stores parameter settings
    /// </summary>
    public class ParameterPreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string SledName { get; set; }
        public DateTime Created { get; set; }
        public Dictionary<string, Dictionary<string, object>> Parameters { get; set; }

        public ParameterPreset()
        {
            Created = DateTime.Now;
            Parameters = new Dictionary<string, Dictionary<string, object>>();
        }
    }

    public class ConfigManager
    {
        private readonly SledParameterManager _sledParameterManager;
        private string _configFolderPath;
        private string _defaultConfigPath;
        private bool _isInitialized = false;
        
        // Cache of available presets
        private List<ParameterPreset> _availablePresets = new List<ParameterPreset>();
        private ParameterPreset _currentPreset = null;

        // Event fired after configuration is successfully loaded (or reset) so that GUI can update.
        public event Action ConfigurationLoaded;
        
        // Event fired when available presets change
        public event Action<List<ParameterPreset>> PresetsChanged;

        public ConfigManager(SledParameterManager sledParameterManager)
        {
            _sledParameterManager = sledParameterManager ?? throw new ArgumentNullException(nameof(sledParameterManager));
            
            string basePath = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "SledTuner", "Presets");
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);
            _configFolderPath = basePath;
        }

        /// <summary>
        /// Updates the configuration file path based on the sled's name.
        /// </summary>
        private void UpdateConfigFilePath()
        {
            // Use the sled's name to create a safe filename.
            string sledName = _sledParameterManager.GetSledName() ?? "UnknownSled";
            string safeSledName = Utilities.MakeSafeFileName(sledName);
            _defaultConfigPath = Path.Combine(_configFolderPath, $"{safeSledName}_default.json");
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
            
            // Ensure the config folder exists
            try
            {
                if (!Directory.Exists(_configFolderPath))
                {
                    Directory.CreateDirectory(_configFolderPath);
                    MelonLogger.Msg($"[ConfigManager] Created config folder: {_configFolderPath}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Failed to create config folder: {ex.Message}");
            }
            
            UpdateConfigFilePath();
            string sledName = _sledParameterManager.GetSledName();
            if (string.IsNullOrEmpty(sledName))
            {
                MelonLogger.Warning("[ConfigManager] Initialization failed: Sled name not found.");
                return;
            }
            MelonLogger.Msg($"[ConfigManager] Initialized for sled: {sledName}");
            _isInitialized = true;
            
            // Load available presets
            RefreshAvailablePresets();
        }

        /// <summary>
        /// Refreshes the list of available presets from disk
        /// </summary>
        public void RefreshAvailablePresets()
        {
            try
            {
                _availablePresets.Clear();
                
                if (!Directory.Exists(_configFolderPath))
                {
                    Directory.CreateDirectory(_configFolderPath);
                    MelonLogger.Msg($"[ConfigManager] Created presets directory: {_configFolderPath}");
                    return;
                }
                
                string[] presetFiles = Directory.GetFiles(_configFolderPath, "*.json");
                foreach (string file in presetFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        ParameterPreset preset = JsonConvert.DeserializeObject<ParameterPreset>(json);
                        
                        if (preset != null)
                        {
                            _availablePresets.Add(preset);
                            MelonLogger.Msg($"[ConfigManager] Loaded preset: {preset.Name} for {preset.SledName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[ConfigManager] Error loading preset file {file}: {ex.Message}");
                    }
                }
                
                // Notify listeners that presets have been updated
                PresetsChanged?.Invoke(_availablePresets);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Error refreshing presets: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets a list of available presets
        /// </summary>
        public List<ParameterPreset> GetAvailablePresets()
        {
            return _availablePresets;
        }
        
        /// <summary>
        /// Gets a list of presets for the current sled
        /// </summary>
        public List<ParameterPreset> GetPresetsForCurrentSled()
        {
            string sledName = _sledParameterManager.GetSledName() ?? "UnknownSled";
            return _availablePresets.Where(p => string.Equals(p.SledName, sledName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Saves the current configuration with the specified name.
        /// </summary>
        public async void SavePreset(string presetName, string description = "")
        {
            InitializeIfNeeded();
            if (!_isInitialized)
            {
                MelonLogger.Warning("[ConfigManager] Cannot save preset: Not initialized");
                return;
            }

            try
            {
                string sledName = _sledParameterManager.GetSledName() ?? "UnknownSled";
                string safePresetName = Utilities.MakeSafeFileName(presetName);
                string filePath = Path.Combine(_configFolderPath, $"{sledName}_{safePresetName}.json");
                
                ParameterPreset preset = new ParameterPreset
                {
                    Name = presetName,
                    Description = description,
                    SledName = sledName,
                    Parameters = _sledParameterManager.GetCurrentParameters()
                };
                
                string json = JsonConvert.SerializeObject(preset, Formatting.Indented);
                await Task.Run(() => File.WriteAllText(filePath, json));
                
                MelonLogger.Msg($"[ConfigManager] Preset '{presetName}' saved to: {filePath}");
                
                // Refresh presets and notify listeners
                RefreshAvailablePresets();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Error saving preset: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the current configuration as the default for this sled.
        /// </summary>
        public async void SaveConfiguration()
        {
            InitializeIfNeeded();
            if (!_isInitialized)
            {
                MelonLogger.Warning("[ConfigManager] Cannot save configuration: Not initialized");
                return;
            }

            var data = _sledParameterManager.GetCurrentParameters();
            try
            {
                // Create a preset
                string sledName = _sledParameterManager.GetSledName() ?? "UnknownSled";
                ParameterPreset preset = new ParameterPreset
                {
                    Name = "Default",
                    Description = "Default configuration for " + sledName,
                    SledName = sledName,
                    Parameters = data
                };
                
                string json = JsonConvert.SerializeObject(preset, Formatting.Indented);
                await Task.Run(() => File.WriteAllText(_defaultConfigPath, json));
                MelonLogger.Msg($"[ConfigManager] Default configuration saved to: {_defaultConfigPath}");
                
                // Refresh presets
                RefreshAvailablePresets();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Error saving configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a preset by name
        /// </summary>
        public void LoadPreset(string presetName)
        {
            try
            {
                ParameterPreset preset = _availablePresets.FirstOrDefault(p => 
                    string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
                    
                if (preset == null)
                {
                    MelonLogger.Warning($"[ConfigManager] Preset '{presetName}' not found");
                    return;
                }
                
                _currentPreset = preset;
                _sledParameterManager.SetParameters(preset.Parameters);
                MelonLogger.Msg($"[ConfigManager] Loaded preset: {preset.Name}");
                
                // Notify subscribers (e.g., GUIManager) to update their fields.
                ConfigurationLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Error loading preset: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Deletes a preset by name
        /// </summary>
        public void DeletePreset(string presetName)
        {
            try
            {
                ParameterPreset preset = _availablePresets.FirstOrDefault(p => 
                    string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
                    
                if (preset == null)
                {
                    MelonLogger.Warning($"[ConfigManager] Preset '{presetName}' not found for deletion");
                    return;
                }
                
                string sledName = preset.SledName ?? "UnknownSled";
                string safePresetName = Utilities.MakeSafeFileName(presetName);
                string filePath = Path.Combine(_configFolderPath, $"{sledName}_{safePresetName}.json");
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    MelonLogger.Msg($"[ConfigManager] Deleted preset: {preset.Name}");
                    
                    // Refresh presets
                    RefreshAvailablePresets();
                }
                else
                {
                    MelonLogger.Warning($"[ConfigManager] Preset file not found for deletion: {filePath}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ConfigManager] Error deleting preset: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the default configuration.
        /// </summary>
        public async void LoadConfiguration()
        {
            InitializeIfNeeded();
            if (!_isInitialized)
            {
                MelonLogger.Warning("[ConfigManager] Not initialized, cannot load configuration.");
                return;
            }
            
            if (string.IsNullOrEmpty(_defaultConfigPath))
            {
                MelonLogger.Warning("[ConfigManager] Default config path is not set.");
                return;
            }
            
            if (!File.Exists(_defaultConfigPath))
            {
                MelonLogger.Warning($"[ConfigManager] Default config file not found: {_defaultConfigPath}");
                MelonLogger.Msg("[ConfigManager] Creating a new default configuration...");
                
                // Save current parameters as the default configuration
                SaveConfiguration();
                
                // If we just created the file, we don't need to load it again
                return;
            }

            try
            {
                string json = await Task.Run(() => File.ReadAllText(_defaultConfigPath));
                ParameterPreset preset = JsonConvert.DeserializeObject<ParameterPreset>(json);
                
                if (preset != null && preset.Parameters != null)
                {
                    _currentPreset = preset;
                    _sledParameterManager.SetParameters(preset.Parameters);
                    MelonLogger.Msg($"[ConfigManager] Loaded default configuration from: {_defaultConfigPath}");
                    // Notify subscribers (e.g., GUIManager) to update their fields.
                    ConfigurationLoaded?.Invoke();
                }
                else
                {
                    MelonLogger.Warning("[ConfigManager] Loaded configuration is invalid");
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
                // Try the alternate search methods
                driverGO = FindDriverWithFallback();
                if (driverGO == null)
                {
                    MelonLogger.Warning("[ConfigManager] Could not find driver object with any method.");
                    return;
                }
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
        /// Finds the driver object with multiple fallback methods
        /// </summary>
        private GameObject FindDriverWithFallback()
        {
            // Try finding with various search patterns
            string[] driverPaths = new string[]
            {
                "Snowmobile/Body/IK Player (Drivers)",
                "Snowmobile(Clone)/IK Player (Drivers)",
                "Snowmobile(Clone)/Body/Driver",
                "Snowmobile(Clone)/Driver",
                "Snowmobile/Driver"
            };
            
            foreach (string path in driverPaths)
            {
                GameObject found = GameObject.Find(path);
                if (found != null)
                {
                    MelonLogger.Msg($"[ConfigManager] Found driver at: {path}");
                    return found;
                }
            }
            
            // Try searching for any object with "driver" in the name
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.ToLower().Contains("driver") || 
                    obj.name.ToLower().Contains("ragdoll") || 
                    obj.name.ToLower().Contains("ik player"))
                {
                    MelonLogger.Msg($"[ConfigManager] Found driver by name search: {obj.name}");
                    return obj;
                }
            }
            
            return null;
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
                
                // Try alternate search method
                GameObject[] treesObjects = GameObject.FindObjectsOfType<GameObject>()
                    .Where(go => go.name.ToLower().Contains("tree") && go.GetComponent<Renderer>() != null)
                    .ToArray();
                    
                if (treesObjects.Length > 0)
                {
                    bool currentState = treesObjects[0].GetComponent<Renderer>().enabled;
                    foreach (GameObject tree in treesObjects)
                    {
                        if (tree.GetComponent<Renderer>() != null)
                            tree.GetComponent<Renderer>().enabled = !currentState;
                    }
                    MelonLogger.Msg($"[ConfigManager] Tree renderers toggled to {(!currentState ? "ON" : "OFF")}.");
                    return;
                }
                else
                {
                    MelonLogger.Warning("[ConfigManager] No trees found to toggle.");
                    return;
                }
            }
            
            Transform treeRendererTransform = levelEssentials.transform.Find("TreeRenderer");
            if (treeRendererTransform == null)
            {
                MelonLogger.Warning("[ConfigManager] 'TreeRenderer' not found under LevelEssentials.");
                
                // Try searching for any child with TreeRenderer in the name
                foreach (Transform child in levelEssentials.transform)
                {
                    if (child.name.ToLower().Contains("tree"))
                    {
                        treeRendererTransform = child;
                        MelonLogger.Msg($"[ConfigManager] Found tree renderer: {child.name}");
                        break;
                    }
                }
                
                if (treeRendererTransform == null)
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
        /// Helper method to toggle a component's enabled state via its 'enabled' property.
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
