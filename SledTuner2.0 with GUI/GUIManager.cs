using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using System.Text.RegularExpressions;

namespace SledTunerProject
{
    public class GUIManager
    {
        // Singleton pattern
        private static GUIManager _instance;
        
        /// <summary>
        /// Gets the singleton instance of GUIManager
        /// </summary>
        public static GUIManager Instance
        {
            get { return _instance; }
        }
        
        // Unique window ID to prevent duplicates
        private const int WINDOW_ID = 27391; // Random number to avoid conflicts
        
        // === REFERENCES ===
        private SledParameterManager _sledParameterManager;
        private ConfigManager _configManager;
        private TeleportManager _teleportManager;
        private List<ParameterPreset> _availablePresets = new List<ParameterPreset>();
        private string _newPresetName = "My Preset";
        private string _newPresetDescription = "";

        // === WINDOW & FIELD STATE ===
        private bool _menuOpen = false;
        private Rect _windowRect;
        private Vector2 _scrollPos = Vector2.zero;
        private Vector2 _teleportScrollPos = Vector2.zero;
        private Vector2 _aboutScrollPosition = Vector2.zero;
        // Reflection-based parameters (string-based for display):
        //   Component -> (Field -> string)
        private Dictionary<string, Dictionary<string, string>> _fieldInputs
            = new Dictionary<string, Dictionary<string, string>>();
        // For Advanced Tree View foldouts
        private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        // === GUI STYLES (initialized in OnGUI if null) ===
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _searchBoxStyle;
        private GUIStyle _tooltipStyle;
        private GUIStyle _tabStyle;
        private GUIStyle _activeTabStyle;

        // === ADDITIONAL FEATURES STATE ===
        private bool _manualApply = true;  // If true, changes only commit after "Apply".
        private bool _showHelp = false;
        private bool _advancedView = true; // Switch between Simple/Advanced
        private bool _treeViewEnabled = true; // Collapsible advanced parameters
        private bool _diagnosticsMode = false; // Debug/diagnostics mode
        private string _searchQuery = ""; // For filtering parameters
        private bool _showTooltips = true; // Show parameter descriptions
        private bool _showSearchResults = false; // When search is active
        private List<SearchResult> _searchResults = new List<SearchResult>(); // Search results

        // === WINDOW CONTROLS & RESIZING ===
        private bool _isMinimized = false;
        private Rect _prevWindowRect;
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Rect _resizeStartWindowRect;
        private ResizeEdges _resizeEdges;
        private float _opacity = 1f; // 0=transparent, 1=opaque

        // === TABS ===
        private int _selectedTab = 0;
        private readonly string[] _tabs = { "Parameters", "Presets", "Settings", "Teleport", "About" };

        // Teleport tab fields
        private string _teleportX = "0";
        private string _teleportY = "0";
        private string _teleportZ = "0";
        private List<string> _teleportHistory = new List<string>();
        private const int MAX_TELEPORT_HISTORY = 10;

        private struct ResizeEdges
        {
            public bool left, right, top, bottom;
        }

        // === SEARCH RESULTS ===
        private struct SearchResult
        {
            public string ComponentName;
            public string FieldName;
            public string DisplayValue;
        }

        // === SIMPLE VIEW LOCAL FIELDS (NOT reflection) ===
        private float speed = 10f;
        private float gravity = -9.81f;
        private float power = 143000f;
        private float lugHeight = 0.18f;
        private float trackLength = 1f;
        private float pitchFactor = 7f;

        private bool notdriverInvincible = true;
        private bool test = false;
        private bool apply = false;

        // Original simple param defaults
        private float originalPower = 143000f;
        private float originalLugHeight = 0.18f;
        private float originalTrackLength = 1f;
        private float originalGravity = -9.81f;
        private float originalPitchFactor = 7f;

        // === COLOR PREVIEW TEXTURE ===
        private Texture2D _colorPreviewTexture;

        // === LOCAL PREVIEW FOR REFLECTION FIELDS (ADVANCED) ===
        // For numeric fields, we store the "live" double in _fieldPreview[compName][fieldName].
        // Only on button release or "Apply" do we commit to SledParameterManager.
        private Dictionary<string, Dictionary<string, double>> _fieldPreview
            = new Dictionary<string, Dictionary<string, double>>();

        // For minus/plus detection in reflection-based fields:
        private HashSet<string> _minusHeldNow = new HashSet<string>();
        private HashSet<string> _minusHeldPrev = new HashSet<string>();
        private HashSet<string> _plusHeldNow = new HashSet<string>();
        private HashSet<string> _plusHeldPrev = new HashSet<string>();
        // Flag to track if a button is being held
        private bool _isAnyButtonHeld = false;
        // Store the scroll position when a button is first pressed
        private Vector2 _buttonPressScrollPos = Vector2.zero;

        // === CONSTRUCTOR ===
        public GUIManager(SledParameterManager sledParameterManager, ConfigManager configManager, TeleportManager teleportManager)
        {
            // Check if this is the first instance
            if (_instance == null)
            {
                _instance = this;
                MelonLogger.Msg("[GUIManager] Created main GUI instance");
            }
            else
            {
                MelonLogger.Warning("[GUIManager] Warning: Creating multiple GUIManager instances. Only the first one will display UI.");
                // Just return here without initializing this instance further
                return;
            }
            
            _sledParameterManager = sledParameterManager;
            _configManager = configManager;
            _teleportManager = teleportManager;
            
            // Subscribe to the PresetsChanged event to update UI when presets change
            _configManager.PresetsChanged += OnPresetsChanged;
            
            // Initial load of available presets
            _availablePresets = _configManager.GetAvailablePresets();
            
            // Default window position (centered)
            float width = 900f;
            float height = 600f;
            _windowRect = new Rect(
                (Screen.width - width) / 2,
                (Screen.height - height) / 2,
                width, height
            );
            
            _resizeEdges = new ResizeEdges();
            _advancedView = true;
            _treeViewEnabled = true;
            
            // Note: InitializeStyles() will be called on first DrawMenu
        }

        /// <summary>
        /// Event handler for when presets are changed in the ConfigManager
        /// </summary>
        private void OnPresetsChanged(List<ParameterPreset> presets)
        {
            MelonLogger.Msg("[GUIManager] Preset list updated");
            _availablePresets = presets;
        }

        /// <summary>
        /// Updates the reference to the teleport manager
        /// </summary>
        public void SetTeleportManager(TeleportManager teleportManager)
        {
            _teleportManager = teleportManager;
        }

        // === PUBLIC METHODS ===

        /// <summary>
        /// Set whether diagnostics mode is enabled
        /// </summary>
        public void SetDiagnosticsMode(bool enabled)
        {
            _diagnosticsMode = enabled;
        }

        /// <summary>
        /// Toggles the menu. On open, we populate from SledParameterManager
        /// but do not re-run reflection each time a user presses +/-.
        /// </summary>
        public void ToggleMenu()
        {
            // If this isn't the main instance, redirect to the main instance
            if (_instance != null && _instance != this)
            {
                _instance.ToggleMenu();
                return;
            }
            
            _menuOpen = !_menuOpen;
            if (_menuOpen)
            {
                RePopulateFields();
                MelonLogger.Msg("[GUIManager] Menu opened.");
            }
            else
            {
                MelonLogger.Msg("[GUIManager] Menu closed.");
            }
        }

        /// <summary>
        /// Loads current param data from SledParameterManager into local dictionaries
        /// so we do not have to re-run reflection or cause flicker mid-press.
        /// </summary>
        public void RePopulateFields()
        {
            _fieldInputs.Clear();
            _fieldPreview.Clear();

            try 
            {
                var currentParams = _sledParameterManager.GetCurrentParameters();
                foreach (var compEntry in _sledParameterManager.ComponentsToInspect)
                {
                    string compName = compEntry.Key;
                    _fieldInputs[compName] = new Dictionary<string, string>();
                    _fieldPreview[compName] = new Dictionary<string, double>();

                    foreach (string field in compEntry.Value)
                    {
                        object val = _sledParameterManager.GetFieldValue(compName, field);
                        string valStr = (val != null) ? val.ToString() : "(No data)";
                        _fieldInputs[compName][field] = valStr;

                        double numericVal = 0.0;
                        if (val is double dd) numericVal = dd;
                        else if (val is float ff) numericVal = ff;
                        else if (val is int ii) numericVal = ii;
                        else
                        {
                            double tryD;
                            if (double.TryParse(valStr, out tryD))
                                numericVal = tryD;
                        }
                        _fieldPreview[compName][field] = numericVal;
                    }
                }

                foreach (var comp in _fieldInputs.Keys)
                {
                    if (!_foldoutStates.ContainsKey(comp))
                        _foldoutStates[comp] = true;
                }
                MelonLogger.Msg("[GUIManager] Fields repopulated.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GUIManager] Error repopulating fields: {ex.Message}");
            }
        }

        /// <summary>
        /// Main IMGUI draw call. 
        /// We do not re-run reflection each time; we only manipulate local data.
        /// </summary>
        public void DrawMenu()
        {
            // Skip if menu is closed or this is not the main instance
            if (!_menuOpen || _instance != this)
                return;

            // Cache the current event
            Event currentEvent = Event.current;

            // Track if any button is being held
            bool wasButtonHeld = _isAnyButtonHeld;
            // Count previous frame's held buttons instead of current ones
            _isAnyButtonHeld = _minusHeldPrev.Count > 0 || _plusHeldPrev.Count > 0;

            // Move 'now' sets to 'prev' sets, then clear 'now'
            _minusHeldPrev = new HashSet<string>(_minusHeldNow);
            _plusHeldPrev = new HashSet<string>(_plusHeldNow);
            _minusHeldNow.Clear();
            _plusHeldNow.Clear();

            if (_windowStyle == null)
            {
                InitializeStyles();
            }

            Color prevColor = GUI.color;
            GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, _opacity);

            _windowRect = GUILayout.Window(WINDOW_ID, _windowRect, WindowFunction, "SledTuner Menu", _windowStyle);

            GUI.color = prevColor;
            HandleResize();
            
            // Draw tooltips if enabled and a tooltip is set
            if (_showTooltips && !string.IsNullOrEmpty(GUI.tooltip))
            {
                DrawTooltip();
            }
        }
        
        /// <summary>
        /// Initialize all GUI styles with consistent colors and formatting
        /// </summary>
        private void InitializeStyles()
        {
            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(10, 10, 20, 10)
            };
            
            _labelStyle = new GUIStyle(GUI.skin.label) 
            { 
                richText = true, 
                fontSize = 12 
            };
            
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12
            };
            
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(2, 2, 2, 2)
            };
            
            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                padding = new RectOffset(20, 0, 0, 0)
            };
            
            _foldoutStyle = new GUIStyle(GUI.skin.toggle) 
            { 
                richText = true, 
                fontSize = 13 
            };
            
            _headerStyle = new GUIStyle(GUI.skin.label) 
            { 
                fontStyle = FontStyle.Bold, 
                fontSize = 14 
            };
            
            _searchBoxStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(2, 2, 2, 2)
            };
            
            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { textColor = Color.white },
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };
            
            _tabStyle = new GUIStyle(GUI.skin.button)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(10, 10, 5, 5),
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 30
            };
            
            _activeTabStyle = new GUIStyle(_tabStyle)
            {
                normal = { background = Texture2D.grayTexture, textColor = Color.white }
            };
        }

        private void WindowFunction(int windowID)
        {
            if (_isMinimized)
            {
                DrawTitleBar();
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
                return;
            }

            DrawTitleBar();
            GUILayout.Space(5);
            
            // Add tabs
            DrawTabs();
            
            // Add search box for advanced view
            if (_advancedView && _selectedTab == 0)
            {
                DrawSearchBox();
            }
            
            // Draw content based on selected tab
            switch (_selectedTab)
            {
                case 0: // Parameters
                    if (_advancedView)
                    {
                        DrawAdvancedTunerMenu();
                    }
                    else
                    {
                        DrawSimpleTunerMenu();
                    }
                    break;
                    
                case 1: // Presets
                    DrawPresetsTab();
                    break;
                    
                case 2: // Settings
                    DrawSettingsTab();
                    break;
                    
                case 3: // Teleport
                    DrawTeleportTab();
                    break;
                    
                case 4: // About
                    DrawAboutTab();
                    break;
            }

            // On Repaint, detect newly released minus/plus => commit reflection fields if needed
            if (Event.current.type == EventType.Repaint)
            {
                DetectButtonReleases();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
        
        /// <summary>
        /// Draw tabs at the top of the window
        /// </summary>
        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (GUILayout.Button(_tabs[i], i == _selectedTab ? _activeTabStyle : _tabStyle, GUILayout.ExpandWidth(true)))
                {
                    _selectedTab = i;
                }
            }
            
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
        }
        
        /// <summary>
        /// Draw the search box for filtering parameters
        /// </summary>
        private void DrawSearchBox()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", _labelStyle, GUILayout.Width(60));
            
            string newSearch = GUILayout.TextField(_searchQuery, _searchBoxStyle, GUILayout.ExpandWidth(true));
            if (newSearch != _searchQuery)
            {
                _searchQuery = newSearch;
                if (!string.IsNullOrWhiteSpace(_searchQuery) && _searchQuery.Length >= 2)
                {
                    PerformSearch();
                    _showSearchResults = true;
                }
                else
                {
                    _showSearchResults = false;
                }
            }
            
            if (GUILayout.Button("Clear", _buttonStyle, GUILayout.Width(60)))
            {
                _searchQuery = "";
                _showSearchResults = false;
            }
            
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
        }
        
        /// <summary>
        /// Perform search across all parameters
        /// </summary>
        private void PerformSearch()
        {
            _searchResults.Clear();
            string searchLower = _searchQuery.ToLower();
            
            foreach (var compEntry in _fieldInputs)
            {
                string compName = compEntry.Key;
                
                // Match component name
                bool compMatches = compName.ToLower().Contains(searchLower);
                
                foreach (var fieldEntry in compEntry.Value)
                {
                    string fieldName = fieldEntry.Key;
                    string value = fieldEntry.Value;
                    
                    // Match field name or value
                    if (compMatches || 
                        fieldName.ToLower().Contains(searchLower) || 
                        value.ToLower().Contains(searchLower))
                    {
                        _searchResults.Add(new SearchResult 
                        { 
                            ComponentName = compName,
                            FieldName = fieldName,
                            DisplayValue = value
                        });
                    }
                }
            }
        }

        /// <summary>
        /// If a minus/plus was held last frame but not this frame => user just released => commit reflection-based changes.
        /// </summary>
        private void DetectButtonReleases()
        {
            foreach (string key in _minusHeldPrev)
            {
                if (!_minusHeldNow.Contains(key))
                {
                    CommitReflectionFieldIfNeeded(key);
                }
            }
            foreach (string key in _plusHeldPrev)
            {
                if (!_plusHeldNow.Contains(key))
                {
                    CommitReflectionFieldIfNeeded(key);
                }
            }
            
            // Reset button held flag if no buttons are being held now
            if (_minusHeldNow.Count == 0 && _plusHeldNow.Count == 0)
            {
                _isAnyButtonHeld = false;
            }
        }

        private void CommitReflectionFieldIfNeeded(string fieldKey)
        {
            // e.g. "MeshInterpretter.power.minus" => parse out compName + "." + fieldName + ".minus"
            if (fieldKey.StartsWith("simple."))
            {
                // local simple fields => not reflection => no commit
                return;
            }

            // parse
            string[] tokens = fieldKey.Split('.');
            if (tokens.Length < 3) return;

            string compName = tokens[0];
            string fieldName = tokens[1];

            if (!_fieldPreview.ContainsKey(compName) ||
                !_fieldPreview[compName].ContainsKey(fieldName))
                return;

            double finalVal = _fieldPreview[compName][fieldName];

            _sledParameterManager.SetFieldValue(compName, fieldName, finalVal);
            if (!_manualApply)
            {
                _sledParameterManager.ApplyParameters();
            }
        }

        private void DrawTitleBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("SledTuner Menu", _headerStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("?", _buttonStyle, GUILayout.Width(30)))
                _showHelp = !_showHelp;
            if (GUILayout.Button("_", _buttonStyle, GUILayout.Width(30)))
                MinimizeWindow();
            if (GUILayout.Button("[ ]", _buttonStyle, GUILayout.Width(30)))
                MaximizeWindow();
            if (GUILayout.Button("X", _buttonStyle, GUILayout.Width(30)))
                CloseWindow();
            if (GUILayout.Button("Switch View", _buttonStyle, GUILayout.Width(100)))
            {
                _advancedView = !_advancedView;
                MelonLogger.Msg("[GUIManager] Switched to " + (_advancedView ? "Advanced View" : "Simple View") + ".");
            }
            GUILayout.EndHorizontal();

            if (_showHelp)
            {
                DrawHelpPanel();
                GUILayout.Space(5);
            }
        }

        private void DrawAdvancedTunerMenu()
        {
            DrawConfigButtons();
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            _manualApply = GUILayout.Toggle(_manualApply, "Manual Apply", _toggleStyle, GUILayout.Width(120));
            _treeViewEnabled = GUILayout.Toggle(_treeViewEnabled, "Tree View", _toggleStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
            
            // When a button is being held, we need to keep the scroll position stable
            // so parameters don't disappear during button press
            if (_isAnyButtonHeld)
            {
                // Use a fixed scroll position while buttons are held
                GUILayout.BeginScrollView(_buttonPressScrollPos, GUILayout.ExpandHeight(true));
            }
            else
            {
                // Normal scrolling behavior when no buttons are held
                _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
                
                // If this is the first frame a button is pressed, save the current scroll position
                if (_plusHeldNow.Count > 0 || _minusHeldNow.Count > 0)
                {
                    _buttonPressScrollPos = _scrollPos;
                }
            }
            
            // If we have search results, show them instead of the full parameter list
            if (_showSearchResults && _searchResults.Count > 0)
            {
                DrawSearchResults();
            }
            else if (_showSearchResults && _searchResults.Count == 0)
            {
                GUILayout.Label($"No results found for '{_searchQuery}'", _labelStyle);
            }
            else
            {
                if (_treeViewEnabled)
                    DrawTreeViewParameters();
                else
                    DrawAdvancedFlatParameters();
            }
            
            GUILayout.EndScrollView();

            GUILayout.Space(5);
            DrawFooter();
        }

        private void DrawSimpleTunerMenu()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Simple Tuner Menu", _headerStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            apply = GUILayout.Toggle(apply, "Apply", _toggleStyle, GUILayout.Width(80));
            if (GUILayout.Button("Reset", _buttonStyle, GUILayout.Width(80)))
                ResetValues();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // local fields only
            GUILayout.Label("FlySpeed");
            speed = DrawLocalFloatField("simple.speed", speed, 0f, 200f);

            GUILayout.Label("Gravity");
            gravity = DrawLocalFloatField("simple.gravity", gravity, -10f, 10f);

            GUILayout.Label("Power");
            power = DrawLocalFloatField("simple.power", power, 0f, 300000f);

            GUILayout.Label("Lug Height");
            lugHeight = DrawLocalFloatField("simple.lugHeight", lugHeight, 0f, 2f);

            GUILayout.Label("Track Length");
            trackLength = DrawLocalFloatField("simple.trackLength", trackLength, 0.5f, 2f);

            GUILayout.Label("Pitch Factor");
            pitchFactor = DrawLocalFloatField("simple.pitchFactor", pitchFactor, 2f, 30f);

            GUILayout.Space(10);

            // Now we unify color channels for "Light" (both simple & advanced can call the same code).
            GUILayout.Label("Headlight Color (RGBA)");
            if (_fieldInputs.ContainsKey("Light"))
            {
                DrawColorChannelsCommon("Light", isAdvanced: false);
            }
            else
            {
                GUILayout.Label("(No Light component found)", _labelStyle);
            }

            // color preview
            float rVal = 1f, gVal = 1f, bVal = 1f, aVal = 1f;
            if (_fieldInputs.ContainsKey("Light"))
            {
                var dict = _fieldInputs["Light"];
                if (dict.TryGetValue("r", out string tmp)) float.TryParse(tmp, out rVal);
                if (dict.TryGetValue("g", out tmp)) float.TryParse(tmp, out gVal);
                if (dict.TryGetValue("b", out tmp)) float.TryParse(tmp, out bVal);
                if (dict.TryGetValue("a", out tmp)) float.TryParse(tmp, out aVal);
            }
            Color currentColor = new Color(rVal, gVal, bVal, aVal);
            GUILayout.Label("Color Preview:");
            UpdateColorPreviewTexture(currentColor);
            GUILayout.Box(_colorPreviewTexture, GUILayout.Width(30), GUILayout.Height(30));

            GUILayout.Space(10);
            notdriverInvincible = GUILayout.Toggle(notdriverInvincible, "Driver Ragdoll", _toggleStyle, GUILayout.Width(150));
            test = GUILayout.Toggle(test, "Test", _toggleStyle, GUILayout.Width(150));

            GUILayout.Space(10);
            GUILayout.Label("Made by Samisalami", _labelStyle, GUILayout.Width(200));
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Single-step local approach for simple tuner float fields.
        /// We do NOT commit to SledParameterManager each frame,
        /// we only store changes in local variable for the user to see.
        /// </summary>
        private float DrawLocalFloatField(string uniqueKey, float currentVal, float min, float max)
        {
            float newVal = GUILayout.HorizontalSlider(currentVal, min, max, GUILayout.Width(150));
            string textVal = GUILayout.TextField(newVal.ToString("F2"), GUILayout.Width(50));
            if (float.TryParse(textVal, out float parsed))
                newVal = parsed;

            GUILayout.BeginHorizontal(GUILayout.Width(60));
            // minus
            string minusKey = uniqueKey + ".minus";
            bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
            if (minusHeld) 
            {
                _minusHeldNow.Add(minusKey);
            }
            
            // plus
            string plusKey = uniqueKey + ".plus";
            bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
            if (plusHeld) 
            {
                _plusHeldNow.Add(plusKey);
            }
            GUILayout.EndHorizontal();
            
            // On Repaint, do a single step if held
            if (Event.current.type == EventType.Repaint)
            {
                if (_minusHeldNow.Contains(minusKey))
                    newVal = Mathf.Max(min, newVal - 0.01f);
                if (_plusHeldNow.Contains(plusKey))
                    newVal = Mathf.Min(max, newVal + 0.01f);
            }
            return newVal;
        }

        /// <summary>
        /// Draw color channels for "Light" from the same code,
        /// used by both Simple (isAdvanced=false) and Advanced (isAdvanced=true).
        /// </summary>
        private void DrawColorChannelsCommon(string compName, bool isAdvanced)
        {
            // compName should be "Light"
            if (!_fieldInputs.ContainsKey(compName))
                return;

            string[] channels = { "r", "g", "b", "a" };
            foreach (string ch in channels)
            {
                if (!_fieldInputs[compName].ContainsKey(ch))
                    continue;

                if (!float.TryParse(_fieldInputs[compName][ch], out float curVal))
                    curVal = 0f;
                float newVal = curVal;

                GUILayout.BeginHorizontal();
                string label = (ch == "r") ? "Red" :
                               (ch == "g") ? "Green" :
                               (ch == "b") ? "Blue" :
                               (ch == "a") ? "Alpha" : ch;

                GUILayout.Label(label + ":", _labelStyle, GUILayout.Width(40));
                newVal = GUILayout.HorizontalSlider(newVal, 0f, 1f, GUILayout.Width(150));
                string textVal = GUILayout.TextField(newVal.ToString("F2"), _textFieldStyle, GUILayout.Width(40));
                if (float.TryParse(textVal, out float parsedVal))
                    newVal = parsedVal;

                // minus
                string minusKey = compName + "." + ch + ".minus";
                bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                if (minusHeld)
                    _minusHeldNow.Add(minusKey);

                // plus
                string plusKey = compName + "." + ch + ".plus";
                bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
                if (plusHeld)
                    _plusHeldNow.Add(plusKey);

                GUILayout.EndHorizontal();

                if (Event.current.type == EventType.Repaint)
                {
                    if (_minusHeldNow.Contains(minusKey))
                        newVal = Mathf.Max(0f, newVal - 0.01f);
                    if (_plusHeldNow.Contains(plusKey))
                        newVal = Mathf.Min(1f, newVal + 0.01f);
                }

                if (Mathf.Abs(newVal - curVal) > 0.0001f)
                {
                    // update the UI string
                    _fieldInputs[compName][ch] = newVal.ToString("F2");

                    if (isAdvanced)
                    {
                        // In advanced mode, we also store numeric in _fieldPreview
                        // so that final commit can happen on release or 'Apply'
                        if (_fieldPreview.ContainsKey(compName) &&
                            _fieldPreview[compName].ContainsKey(ch))
                        {
                            _fieldPreview[compName][ch] = newVal;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reflection-based advanced parameters in a flat list. 
        /// We'll skip color channels in the normal numeric loop below 
        /// and call DrawColorChannelsCommon for 'Light'.
        /// </summary>
        private void DrawAdvancedFlatParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                GUILayout.Label("<b>Component: " + comp.Key + "</b>", _labelStyle);

                if (comp.Key == "Light")
                {
                    // unify color channels
                    DrawColorChannelsCommon("Light", isAdvanced: true);

                    // color preview
                    GUILayout.Space(5);
                    GUILayout.Label("Color Preview:", _labelStyle);
                    float r = 1f, g = 1f, b = 1f, a = 1f;
                    if (comp.Value.ContainsKey("r")) float.TryParse(comp.Value["r"], out r);
                    if (comp.Value.ContainsKey("g")) float.TryParse(comp.Value["g"], out g);
                    if (comp.Value.ContainsKey("b")) float.TryParse(comp.Value["b"], out b);
                    if (comp.Value.ContainsKey("a")) float.TryParse(comp.Value["a"], out a);
                    Color previewColor = new Color(r, g, b, a);
                    UpdateColorPreviewTexture(previewColor);
                    GUILayout.Box(_colorPreviewTexture, GUILayout.Width(30), GUILayout.Height(30));
                }
                else
                {
                    DrawReflectionParameters(comp.Key, comp.Value);
                }

                GUILayout.Space(10);
            }
        }

        private void DrawTreeViewParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                if (!_foldoutStates.ContainsKey(comp.Key))
                    _foldoutStates[comp.Key] = true;

                _foldoutStates[comp.Key] = GUILayout.Toggle(
                    _foldoutStates[comp.Key],
                    "<b>" + comp.Key + "</b>",
                    _foldoutStyle
                );

                if (_foldoutStates[comp.Key])
                {
                    GUILayout.BeginVertical(GUI.skin.box);

                    if (comp.Key == "Light")
                    {
                        // unify color channels
                        DrawColorChannelsCommon("Light", isAdvanced: true);

                        // color preview
                        GUILayout.Space(5);
                        GUILayout.Label("Color Preview:", _labelStyle);
                        float r = 1f, g = 1f, b = 1f, a = 1f;
                        if (comp.Value.ContainsKey("r")) float.TryParse(comp.Value["r"], out r);
                        if (comp.Value.ContainsKey("g")) float.TryParse(comp.Value["g"], out g);
                        if (comp.Value.ContainsKey("b")) float.TryParse(comp.Value["b"], out b);
                        if (comp.Value.ContainsKey("a")) float.TryParse(comp.Value["a"], out a);
                        Color previewColor = new Color(r, g, b, a);
                        UpdateColorPreviewTexture(previewColor);
                        GUILayout.Box(_colorPreviewTexture, GUILayout.Width(30), GUILayout.Height(30));
                    }
                    else
                    {
                        DrawReflectionParameters(comp.Key, comp.Value);
                    }

                    GUILayout.EndVertical();
                }
                GUILayout.Space(10);
            }
        }

        /// <summary>
        /// Reflection-based numeric/bool fields. 
        /// For color channels, we skip them here and call DrawColorChannelsCommon. 
        /// For numeric fields, we store local changes in _fieldPreview. 
        /// Only on button release or 'Apply' do we commit to SledParameterManager.
        /// </summary>
        private void DrawReflectionParameters(string compName, Dictionary<string, string> fields)
        {
            foreach (var kvp in fields)
            {
                string fieldName = kvp.Key;
                // skip color channels for Light => handled in DrawColorChannelsCommon
                if (compName == "Light" &&
                    (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(fieldName + ":", _labelStyle, GUILayout.Width(150));

                Type fieldType = _sledParameterManager.GetFieldType(compName, fieldName);
                double currentVal = _fieldPreview[compName][fieldName]; // numeric preview

                if (fieldType == typeof(float) || fieldType == typeof(double) || fieldType == typeof(int))
                {
                    DrawNumericParameterControl(compName, fieldName);
                }
                else if (fieldType == typeof(bool))
                {
                    // parse as bool
                    bool curBool = false;
                    bool.TryParse(_fieldInputs[compName][fieldName], out curBool);
                    bool newBool = GUILayout.Toggle(curBool, curBool ? "On" : "Off", _toggleStyle, GUILayout.Width(80));
                    if (newBool != curBool)
                    {
                        _fieldInputs[compName][fieldName] = newBool.ToString();
                        // if manual is off, commit immediately
                        if (!_manualApply)
                        {
                            _sledParameterManager.SetFieldValue(compName, fieldName, newBool);
                            _sledParameterManager.ApplyParameters();
                        }
                    }
                }
                else
                {
                    // fallback: string or other type
                    string newVal = GUILayout.TextField(
                        _fieldInputs[compName][fieldName],
                        _textFieldStyle,
                        GUILayout.ExpandWidth(true)
                    );
                    if (newVal != _fieldInputs[compName][fieldName])
                    {
                        _fieldInputs[compName][fieldName] = newVal;
                    }
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawConfigButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.LoadConfiguration();
                RePopulateFields(); // after load, re-populate
                MelonLogger.Msg("[GUIManager] Load clicked.");
            }
            if (GUILayout.Button("Save", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.SaveConfiguration();
                MelonLogger.Msg("[GUIManager] Save clicked.");
            }
            if (GUILayout.Button("Reset", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.ResetParameters();
                _sledParameterManager.RevertParameters();
                RePopulateFields();
                MelonLogger.Msg("[GUIManager] Reset clicked.");
            }
            if (GUILayout.Button("Apply", _buttonStyle, GUILayout.Height(25)))
            {
                ApplyChanges();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle Ragdoll", _buttonStyle, GUILayout.Width(120)))
                _configManager.ToggleRagdoll();
            if (GUILayout.Button("Toggle Tree Renderer", _buttonStyle, GUILayout.Width(150)))
                _configManager.ToggleTreeRenderer();
            if (GUILayout.Button("Teleport", _buttonStyle, GUILayout.Width(100)))
                _configManager.TeleportSled();
            GUILayout.EndHorizontal();
        }

        private void DrawOpacitySlider()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Opacity:", _labelStyle, GUILayout.Width(70));
            float newOpacity = GUILayout.HorizontalSlider(_opacity, 0.05f, 1f, GUILayout.Width(150));
            if (Math.Abs(newOpacity - _opacity) > 0.001f)
            {
                _opacity = newOpacity;
                // Force immediate repaint to see changes
                MelonLoader.MelonCoroutines.Start(RepaintNextFrame());
            }
            GUILayout.Label((_opacity * 100f).ToString("F0") + "%", _labelStyle, GUILayout.Width(40));
            GUILayout.EndHorizontal();
        }
        
        private System.Collections.IEnumerator RepaintNextFrame()
        {
            yield return null; // Wait for next frame
            GUI.changed = true; // Force a repaint
        }

        /// <summary>
        /// Called by "Apply" button. Commits advanced reflection fields from _fieldPreview
        /// to the SledParameterManager. Simple fields remain local unless we want to sync them too.
        /// </summary>
        private void ApplyChanges()
        {
            // commit reflection-based fields
            foreach (var compKvp in _fieldPreview)
            {
                string compName = compKvp.Key;
                foreach (var fieldKvp in compKvp.Value)
                {
                    string fieldName = fieldKvp.Key;
                    double val = fieldKvp.Value;

                    Type ft = _sledParameterManager.GetFieldType(compName, fieldName);
                    if (ft == typeof(float) || ft == typeof(double) || ft == typeof(int))
                    {
                        _sledParameterManager.SetFieldValue(compName, fieldName, val);
                    }
                    else if (ft == typeof(bool))
                    {
                        bool b;
                        bool.TryParse(_fieldInputs[compName][fieldName], out b);
                        _sledParameterManager.SetFieldValue(compName, fieldName, b);
                    }
                    else if (compName == "Light" &&
                             (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
                    {
                        // If we want to commit color channels on Apply:
                        // parse from _fieldInputs and do SetFieldValue
                        if (_fieldInputs[compName].TryGetValue(fieldName, out string channelStr))
                        {
                            if (float.TryParse(channelStr, out float cVal))
                            {
                                _sledParameterManager.SetFieldValue(compName, fieldName, cVal);
                            }
                        }
                    }
                }
            }
            _sledParameterManager.ApplyParameters();
            MelonLogger.Msg("[GUIManager] Changes applied.");
        }

        private void ResetValues()
        {
            // local simple fields
            speed = 10f;
            gravity = originalGravity;
            power = originalPower;
            lugHeight = originalLugHeight;
            trackLength = originalTrackLength;
            pitchFactor = originalPitchFactor;

            notdriverInvincible = true;
            test = false;
        }

        private void HandleResize()
        {
            Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool nearLeft = mousePos.x >= _windowRect.x && mousePos.x <= _windowRect.x + 10f;
            bool nearRight = mousePos.x >= _windowRect.xMax - 10f && mousePos.x <= _windowRect.xMax;
            bool nearTop = mousePos.y >= _windowRect.y && mousePos.y <= _windowRect.y + 10f;
            bool nearBottom = mousePos.y >= _windowRect.yMax - 10f && mousePos.y <= _windowRect.yMax;

            if (Event.current.type == EventType.MouseDown && (nearLeft || nearRight || nearTop || nearBottom))
            {
                _isResizing = true;
                _resizeStartMousePos = mousePos;
                _resizeStartWindowRect = _windowRect;
                _resizeEdges.left = nearLeft;
                _resizeEdges.right = nearRight;
                _resizeEdges.top = nearTop;
                _resizeEdges.bottom = nearBottom;
                Event.current.Use();
            }

            if (_isResizing)
            {
                if (Event.current.rawType == EventType.MouseDrag || Event.current.rawType == EventType.MouseMove)
                {
                    Vector2 delta = mousePos - _resizeStartMousePos;
                    Rect newRect = _resizeStartWindowRect;
                    float minWidth = Screen.width * 0.3f;
                    float maxWidth = Screen.width * 0.8f;
                    float minHeight = 30f;
                    float maxHeight = Screen.height * 0.8f;

                    if (_resizeEdges.right)
                        newRect.width = Mathf.Clamp(_resizeStartWindowRect.width + delta.x, minWidth, maxWidth);
                    if (_resizeEdges.left)
                    {
                        newRect.x = _resizeStartWindowRect.x + delta.x;
                        newRect.width = Mathf.Clamp(_resizeStartWindowRect.width - delta.x, minWidth, maxWidth);
                    }
                    if (_resizeEdges.bottom)
                        newRect.height = Mathf.Clamp(_resizeStartWindowRect.height + delta.y, minHeight, maxHeight);
                    if (_resizeEdges.top)
                    {
                        newRect.y = _resizeStartWindowRect.y + delta.y;
                        newRect.height = Mathf.Clamp(_resizeStartWindowRect.height - delta.y, minHeight, maxHeight);
                    }
                    _windowRect = newRect;
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseUp)
                {
                    _isResizing = false;
                    _resizeEdges = new ResizeEdges();
                    Event.current.Use();
                }
            }
        }

        private void MinimizeWindow()
        {
            if (!_isMinimized)
            {
                _prevWindowRect = _windowRect;
                _windowRect = new Rect(Screen.width - 150f - 10f, 10f, 150f, 30f);
                _isMinimized = true;
                MelonLogger.Msg("[GUIManager] Window minimized.");
            }
        }

        private void MaximizeWindow()
        {
            if (_isMinimized)
            {
                _windowRect = _prevWindowRect;
                _isMinimized = false;
                MelonLogger.Msg("[GUIManager] Window restored from minimized state.");
            }
            else
            {
                _windowRect.width = Screen.width * 0.8f;
                _windowRect.height = Screen.height * 0.8f;
                MelonLogger.Msg("[GUIManager] Window maximized.");
            }
            _menuOpen = true;
        }

        private void CloseWindow()
        {
            _menuOpen = false;
            _isMinimized = false;
            MelonLogger.Msg("[GUIManager] Window closed.");
        }

        private void DrawHelpPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Help:</b>\n\n" +
                "Hotkeys:\n  F2 - Toggle Menu\n  F3 - Refresh Fields\n\n" +
                "Instructions:\n" +
                "  - Adjust parameters using sliders, text fields, or +/- (click-and-hold) buttons.\n" +
                "  - Color channels for 'Light' are drawn the same in Simple/Advanced for easier maintenance.\n" +
                "  - In Advanced mode, only on button release or 'Apply' do changes commit (if Manual Apply is off).\n" +
                "  - If 'Manual Apply' is on, changes only commit when you press 'Apply'.\n" +
                "  - Use window buttons to Minimize, Maximize, or Close.\n" +
                "  - Footer toggles: Ragdoll, Tree Renderer, Teleport.\n" +
                "  - 'Tree View' collapses advanced components. 'Switch View' toggles Simple/Advanced.\n",
                _labelStyle);
            GUILayout.EndVertical();
        }

        private void UpdateColorPreviewTexture(Color color)
        {
            if (_colorPreviewTexture == null)
            {
                _colorPreviewTexture = new Texture2D(30, 30, TextureFormat.RGBA32, false);
                _colorPreviewTexture.filterMode = FilterMode.Bilinear;
                _colorPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            }

            Color[] pixels = new Color[30 * 30];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            _colorPreviewTexture.SetPixels(pixels);
            _colorPreviewTexture.Apply();
        }

        /// <summary>
        /// Draw the presets tab for saving/loading parameter configurations
        /// </summary>
        private void DrawPresetsTab()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Parameter Presets</b>", _headerStyle);
            GUILayout.Space(10);
            
            // Current sled info
            string sledName = _sledParameterManager.GetSledName() ?? "Unknown Sled";
            GUILayout.Label($"Current Sled: <b>{sledName}</b>", _labelStyle);
            GUILayout.Space(10);
            
            // Save preset section
            GUILayout.Label("Save Current Settings", _headerStyle);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Preset Name:", _labelStyle, GUILayout.Width(100));
            _newPresetName = GUILayout.TextField(_newPresetName, _textFieldStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Description:", _labelStyle, GUILayout.Width(100));
            _newPresetDescription = GUILayout.TextField(_newPresetDescription, _textFieldStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Preset", _buttonStyle, GUILayout.Width(120)))
            {
                if (!string.IsNullOrWhiteSpace(_newPresetName))
                {
                    _configManager.SavePreset(_newPresetName, _newPresetDescription);
                    MelonLogger.Msg($"[GUIManager] Saving preset: {_newPresetName}");
                }
                else
                {
                    MelonLogger.Warning("[GUIManager] Cannot save preset with empty name");
                }
            }
            
            if (GUILayout.Button("Save as Default", _buttonStyle, GUILayout.Width(120)))
            {
                _configManager.SaveConfiguration();
                MelonLogger.Msg("[GUIManager] Saved as default configuration");
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(20);
            
            // Presets for current sled
            List<ParameterPreset> presetsForSled = _availablePresets
                .Where(p => string.Equals(p.SledName, sledName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            // All presets section
            GUILayout.Label($"Available Presets ({presetsForSled.Count})", _headerStyle);
            
            if (presetsForSled.Count > 0)
            {
                _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUI.skin.box, GUILayout.Height(200));
                
                foreach (var preset in presetsForSled)
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    
                    // Preset name and description
                    GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    GUILayout.Label("<b>" + preset.Name + "</b>", _labelStyle);
                    if (!string.IsNullOrEmpty(preset.Description))
                    {
                        GUILayout.Label(preset.Description, _labelStyle);
                    }
                    GUILayout.Label("Created: " + preset.Created.ToString("g"), _labelStyle);
                    GUILayout.EndVertical();
                    
                    // Buttons
                    GUILayout.BeginVertical(GUILayout.Width(80));
                    if (GUILayout.Button("Load", _buttonStyle))
                    {
                        MelonLogger.Msg($"[GUIManager] Loading preset: {preset.Name}");
                        _configManager.LoadPreset(preset.Name);
                        RePopulateFields();
                    }
                    
                    if (GUILayout.Button("Delete", _buttonStyle))
                    {
                        MelonLogger.Msg($"[GUIManager] Deleting preset: {preset.Name}");
                        _configManager.DeletePreset(preset.Name);
                        // Refresh happens via event
                    }
                    GUILayout.EndVertical();
                    
                    GUILayout.EndHorizontal();
                    GUILayout.Space(5);
                }
                
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Box("No presets available for this sled", GUI.skin.box, GUILayout.ExpandWidth(true));
            }
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draw the settings tab for mod configuration
        /// </summary>
        private void DrawSettingsTab()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Mod Settings</b>", _headerStyle);
            GUILayout.Space(10);
            
            // Interface settings
            GUILayout.Label("Interface Settings", _headerStyle);
            
            _showTooltips = GUILayout.Toggle(_showTooltips, "Show Parameter Tooltips", _toggleStyle);
            _manualApply = GUILayout.Toggle(_manualApply, "Manual Apply Mode", _toggleStyle);
            _advancedView = GUILayout.Toggle(_advancedView, "Advanced Parameter Mode", _toggleStyle);
            
            if (_advancedView)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                _treeViewEnabled = GUILayout.Toggle(_treeViewEnabled, "Use Tree View", _toggleStyle);
                GUILayout.EndHorizontal();
            }
            
            GUILayout.Space(5);
            
            // Window settings
            GUILayout.Label("Window Settings", _headerStyle);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Opacity:", _labelStyle, GUILayout.Width(70));
            float newOpacity = GUILayout.HorizontalSlider(_opacity, 0.05f, 1f, GUILayout.Width(150));
            if (Math.Abs(newOpacity - _opacity) > 0.001f)
            {
                _opacity = newOpacity;
                // Force immediate repaint to see changes
                MelonLoader.MelonCoroutines.Start(RepaintNextFrame());
            }
            GUILayout.Label((_opacity * 100f).ToString("F0") + "%", _labelStyle, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            
            if (GUILayout.Button("Reset Window Position", _buttonStyle, GUILayout.Width(200)))
            {
                // Reset window to default position and size
                _windowRect = new Rect(
                    Screen.width * 0.2f,
                    Screen.height * 0.2f,
                    Screen.width * 0.6f,
                    Screen.height * 0.6f
                );
                MelonLogger.Msg("[GUIManager] Window position reset");
            }
            
            GUILayout.Space(10);
            
            // Advanced options
            GUILayout.Label("Advanced Options", _headerStyle);
            
            if (GUILayout.Button("Toggle Diagnostics Mode (F4)", _buttonStyle, GUILayout.Width(200)))
            {
                _diagnosticsMode = !_diagnosticsMode;
                MelonLogger.Msg($"[GUIManager] Diagnostics mode: {(_diagnosticsMode ? "enabled" : "disabled")}");
            }
            
            if (GUILayout.Button("Reload Parameters (F3)", _buttonStyle, GUILayout.Width(200)))
            {
                // This would trigger the same action as pressing F3
                MelonLogger.Msg("[GUIManager] Manual parameter reload requested");
            }
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draw the about tab with mod information
        /// </summary>
        private void DrawAboutTab()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>About SledTuner</b>", _headerStyle);
            GUILayout.Space(10);
            
            GUILayout.Label("SledTuner v2.0", _headerStyle);
            GUILayout.Label("A mod for Sledders that allows you to tune snowmobile parameters and teleport around the map", _labelStyle);
            GUILayout.Space(10);
            
            // Parameter Tuning section
            GUILayout.Label("<b>Parameter Tuning:</b>", _headerStyle);
            GUILayout.Label("Use the Parameters tab to adjust snowmobile handling, physics, and visual settings.", _labelStyle);
            GUILayout.Label("Changes can be saved as presets in the Presets tab for quick access later.", _labelStyle);
            GUILayout.Space(10);
            
            // Teleportation section
            GUILayout.Label("<b>Teleportation Features:</b>", _headerStyle);
            GUILayout.Label("The Teleport tab allows you to instantly move to any location on the map.", _labelStyle);
            GUILayout.Label("You can teleport to specific coordinates, map positions, or use keyboard shortcuts.", _labelStyle);
            GUILayout.Label("Position history is saved automatically for backtracking.", _labelStyle);
            GUILayout.Space(10);
            
            // Hotkeys section with better organization
            GUILayout.Label("<b>Hotkeys:</b>", _headerStyle);
            
            GUILayout.Label("Menu Controls:", _labelStyle, GUILayout.Width(150));
            GUILayout.Label("F2 - Toggle Menu", _labelStyle);
            GUILayout.Label("F3 - Force Re-initialization", _labelStyle);
            GUILayout.Label("F4 - Toggle Diagnostics Mode", _labelStyle);
            GUILayout.Label("F5 - Load Default Configuration", _labelStyle);
            GUILayout.Label("F6 - Save Current Settings as Default", _labelStyle);
            GUILayout.Label("Ctrl+F12 - Reset to Original Parameters", _labelStyle);
            GUILayout.Space(5);
            
            GUILayout.Label("Teleportation Controls:", _labelStyle, GUILayout.Width(150));
            GUILayout.Label("T - Teleport to map cursor position", _labelStyle);
            GUILayout.Label("P - Save current position", _labelStyle);
            GUILayout.Label("Backspace - Teleport to previous position", _labelStyle);
            GUILayout.Space(10);
            
            // More detailed teleport instructions
            GUILayout.Label("<b>How to Use Teleporter:</b>", _headerStyle);
            _aboutScrollPosition = GUILayout.BeginScrollView(_aboutScrollPosition, GUILayout.Height(150));
            
            GUILayout.Label("1. <b>Quick Teleporting:</b>", _labelStyle);
            GUILayout.Label("   • Open the map with the map key", _labelStyle);
            GUILayout.Label("   • Position the cursor at your desired location", _labelStyle);
            GUILayout.Label("   • Press T to instantly teleport there", _labelStyle);
            GUILayout.Label("   • Use P to save important positions for later", _labelStyle);
            GUILayout.Label("   • Press Backspace to return to previous locations", _labelStyle);
            GUILayout.Space(5);
            
            GUILayout.Label("2. <b>Using the Teleport Tab:</b>", _labelStyle);
            GUILayout.Label("   • View your current coordinates", _labelStyle);
            GUILayout.Label("   • Enter specific X/Y/Z coordinates to teleport precisely", _labelStyle);
            GUILayout.Label("   • Use the Map Position fields for map-relative teleporting", _labelStyle);
            GUILayout.Label("   • Track your teleport history in the log section", _labelStyle);
            GUILayout.Label("   • Save your current position as a checkpoint", _labelStyle);
            
            GUILayout.EndScrollView();
            GUILayout.Space(10);
            
            GUILayout.Label("<b>Credits:</b>", _labelStyle);
            GUILayout.Label("- Original SledTuner by Samisalami", _labelStyle);
            GUILayout.Label("- Teleporter functionality by SleddersTeleporter Team", _labelStyle);
            GUILayout.Label("- Enhanced unified version by SledTuner Team", _labelStyle);
            GUILayout.Space(20);
            
            if (GUILayout.Button("Check for Updates", _buttonStyle, GUILayout.Width(150)))
            {
                // Would check for updates
                MelonLogger.Msg("[GUIManager] Update check requested");
            }
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draw a tooltip near the mouse position
        /// </summary>
        private void DrawTooltip()
        {
            Vector2 mousePos = Event.current.mousePosition;
            Vector2 size = _tooltipStyle.CalcSize(new GUIContent(GUI.tooltip));
            
            // Ensure tooltip stays on screen
            float xPos = mousePos.x + 15;
            if (xPos + size.x > Screen.width)
                xPos = Screen.width - size.x - 10;
                
            float yPos = mousePos.y + 15;
            if (yPos + size.y > Screen.height)
                yPos = mousePos.y - size.y - 10;
                
            // Draw the tooltip
            GUI.Box(new Rect(xPos, yPos, size.x + 20, size.y + 10), GUI.tooltip, _tooltipStyle);
        }

        /// <summary>
        /// Draw the search results as a simple list
        /// </summary>
        private void DrawSearchResults()
        {
            GUILayout.Label($"Search Results for '{_searchQuery}' ({_searchResults.Count} found):", _headerStyle);
            GUILayout.Space(5);
            
            foreach (var result in _searchResults)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{result.ComponentName}</b> / {result.FieldName}:", _labelStyle, GUILayout.Width(250));
                
                // Get parameter type and draw appropriate control
                Type fieldType = _sledParameterManager.GetFieldType(result.ComponentName, result.FieldName);
                
                if (fieldType == typeof(float) || fieldType == typeof(double) || fieldType == typeof(int))
                {
                    DrawNumericParameterControl(result.ComponentName, result.FieldName, true);
                }
                else if (fieldType == typeof(bool))
                {
                    DrawBoolParameterControl(result.ComponentName, result.FieldName);
                }
                else
                {
                    GUILayout.Label(result.DisplayValue, _labelStyle, GUILayout.ExpandWidth(true));
                }
                
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }
        
        /// <summary>
        /// Draw a numeric parameter with slider, text field, and +/- buttons
        /// </summary>
        private void DrawNumericParameterControl(string compName, string fieldName, bool useCompactLayout = false)
        {
            float sliderMin = _sledParameterManager.GetSliderMin(compName, fieldName);
            float sliderMax = _sledParameterManager.GetSliderMax(compName, fieldName);
            double currentVal = _fieldPreview[compName][fieldName];
            double step = (_sledParameterManager.GetFieldType(compName, fieldName) == typeof(int)) ? 1.0 : 0.01;
            
            if (useCompactLayout)
            {
                // Compact layout for search results
                GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                
                // Slider
                float sliderVal = GUILayout.HorizontalSlider((float)currentVal, sliderMin, sliderMax, GUILayout.Width(120));
                
                // Text field
                string newText = GUILayout.TextField(((float)currentVal).ToString("F2"), _textFieldStyle, GUILayout.Width(60));
                if (float.TryParse(newText, out float parsedVal))
                {
                    sliderVal = parsedVal;
                }
                
                // +/- buttons
                GUILayout.BeginHorizontal(GUILayout.Width(60));
                string minusKey = compName + "." + fieldName + ".minus";
                bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                if (minusHeld) _minusHeldNow.Add(minusKey);
                
                string plusKey = compName + "." + fieldName + ".plus";
                bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
                if (plusHeld) _plusHeldNow.Add(plusKey);
                GUILayout.EndHorizontal();
                
                double finalVal = (double)sliderVal;
                
                if (Event.current.type == EventType.Repaint)
                {
                    if (_minusHeldNow.Contains(minusKey))
                        finalVal = Math.Max(sliderMin, finalVal - step);
                    if (_plusHeldNow.Contains(plusKey))
                        finalVal = Math.Min(sliderMax, finalVal + step);
                }
                
                if (Math.Abs(finalVal - currentVal) > 0.0001)
                {
                    _fieldPreview[compName][fieldName] = finalVal;
                    _fieldInputs[compName][fieldName] = finalVal.ToString("F2");
                    
                    // Apply changes immediately if not in manual mode
                    if (!_manualApply)
                    {
                        CommitReflectionFieldIfNeeded(compName + "." + fieldName);
                    }
                    
                    // Force repaint to update slider position
                    GUI.changed = true;
                }
                
                GUILayout.EndHorizontal();
            }
            else
            {
                // Regular layout for parameter view
                GUILayout.BeginHorizontal();
                
                // Label
                GUILayout.Label(fieldName + ":", _labelStyle, GUILayout.Width(120));
                
                // Slider
                float sliderVal = GUILayout.HorizontalSlider((float)currentVal, sliderMin, sliderMax, GUILayout.Width(150));
                
                // Text field
                string newText = GUILayout.TextField(((float)currentVal).ToString("F2"), _textFieldStyle, GUILayout.Width(60));
                if (float.TryParse(newText, out float parsedVal))
                {
                    sliderVal = parsedVal;
                }
                
                // +/- buttons
                GUILayout.BeginHorizontal(GUILayout.Width(60));
                string minusKey = compName + "." + fieldName + ".minus";
                bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                if (minusHeld) _minusHeldNow.Add(minusKey);
                
                string plusKey = compName + "." + fieldName + ".plus";
                bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
                if (plusHeld) _plusHeldNow.Add(plusKey);
                GUILayout.EndHorizontal();
                
                double finalVal = (double)sliderVal;
                
                if (Event.current.type == EventType.Repaint)
                {
                    if (_minusHeldNow.Contains(minusKey))
                        finalVal = Math.Max(sliderMin, finalVal - step);
                    if (_plusHeldNow.Contains(plusKey))
                        finalVal = Math.Min(sliderMax, finalVal + step);
                }
                
                if (Math.Abs(finalVal - currentVal) > 0.0001)
                {
                    _fieldPreview[compName][fieldName] = finalVal;
                    _fieldInputs[compName][fieldName] = finalVal.ToString("F2");
                    
                    // Apply changes immediately if not in manual mode
                    if (!_manualApply)
                    {
                        CommitReflectionFieldIfNeeded(compName + "." + fieldName);
                    }
                    
                    // Force repaint to update slider position
                    GUI.changed = true;
                }
                
                GUILayout.EndHorizontal();
            }
        }
        
        /// <summary>
        /// Draw a boolean parameter control
        /// </summary>
        private void DrawBoolParameterControl(string compName, string fieldName)
        {
            // Parse as bool
            bool curBool = false;
            bool.TryParse(_fieldInputs[compName][fieldName], out curBool);
            bool newBool = GUILayout.Toggle(curBool, curBool ? "On" : "Off", _toggleStyle, GUILayout.Width(80));
            
            if (newBool != curBool)
            {
                _fieldInputs[compName][fieldName] = newBool.ToString();
                // if manual is off, commit immediately
                if (!_manualApply)
                {
                    _sledParameterManager.SetFieldValue(compName, fieldName, newBool);
                    _sledParameterManager.ApplyParameters();
                }
            }
        }

        /// <summary>
        /// Draw the teleport tab
        /// </summary>
        private void DrawTeleportTab()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Teleport Tools</b>", _headerStyle);
            
            // Teleport status
            if (_teleportManager == null)
            {
                GUILayout.Box("Teleport Manager not available", GUI.skin.box, GUILayout.ExpandWidth(true));
                GUILayout.EndVertical();
                return;
            }
            
            // Show initialization status
            string statusMessage = _teleportManager.IsInitialized 
                ? "<color=green>✓ Teleporter Ready</color>"
                : "<color=red>✗ Teleporter Not Initialized</color>";
                
            GUILayout.Box(statusMessage, _headerStyle, GUILayout.ExpandWidth(true));
            
            if (!_teleportManager.IsInitialized)
            {
                if (GUILayout.Button("Initialize Teleporter", _buttonStyle, GUILayout.Width(200)))
                {
                    _teleportManager.Initialize();
                }
                GUILayout.EndVertical();
                return;
            }
            
            GUILayout.Space(10);
            
            // Current player position
            GUILayout.Label("<b>Current Position:</b>", _labelStyle);
            GUILayout.Box(_teleportManager.GetCurrentPositionString(), GUI.skin.box, GUILayout.ExpandWidth(true));
            
            GUILayout.Space(10);
            
            // Quick teleport buttons
            GUILayout.Label("<b>Quick Teleport:</b>", _labelStyle);
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Teleport to Map Cursor (T)", _buttonStyle))
            {
                _teleportManager.TeleportToMapCursor();
            }
            
            if (GUILayout.Button("Save Current Position (P)", _buttonStyle))
            {
                _teleportManager.SaveCurrentPosition();
                AddToTeleportHistory("Saved: " + _teleportManager.GetCurrentPositionString());
            }
            
            if (GUILayout.Button("Teleport Back (Backspace)", _buttonStyle))
            {
                if (_teleportManager.TeleportBack())
                {
                    AddToTeleportHistory("Teleported back");
                }
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // Coordinate teleport
            GUILayout.Label("<b>Teleport to Coordinates:</b>", _labelStyle);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("X:", _labelStyle, GUILayout.Width(20));
            _teleportX = GUILayout.TextField(_teleportX, _textFieldStyle, GUILayout.Width(80));
            _teleportX = Regex.Replace(_teleportX, "[^0-9\\.-]", "");
            
            GUILayout.Label("Y:", _labelStyle, GUILayout.Width(20));
            _teleportY = GUILayout.TextField(_teleportY, _textFieldStyle, GUILayout.Width(80));
            _teleportY = Regex.Replace(_teleportY, "[^0-9\\.-]", "");
            
            GUILayout.Label("Z:", _labelStyle, GUILayout.Width(20));
            _teleportZ = GUILayout.TextField(_teleportZ, _textFieldStyle, GUILayout.Width(80));
            _teleportZ = Regex.Replace(_teleportZ, "[^0-9\\.-]", "");
            
            if (GUILayout.Button("Teleport", _buttonStyle, GUILayout.Width(100)))
            {
                float x = 0f, y = 0f, z = 0f;
                bool validX = float.TryParse(_teleportX, out x);
                bool validY = float.TryParse(_teleportY, out y);
                bool validZ = float.TryParse(_teleportZ, out z);
                
                if (validX && validY && validZ)
                {
                    if (_teleportManager.TeleportToPosition(x, y, z))
                    {
                        AddToTeleportHistory($"Teleported to: X:{x} Y:{y} Z:{z}");
                    }
                }
                else
                {
                    MelonLogger.Warning("[GUIManager] Invalid teleport coordinates");
                }
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // Map coordinates (XZ plane)
            GUILayout.Label("<b>Teleport to Map Position:</b>", _labelStyle);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map X:", _labelStyle, GUILayout.Width(60));
            string mapX = GUILayout.TextField("0", _textFieldStyle, GUILayout.Width(80));
            mapX = Regex.Replace(mapX, "[^0-9\\.-]", "");
            
            GUILayout.Label("Map Y:", _labelStyle, GUILayout.Width(60));
            string mapY = GUILayout.TextField("0", _textFieldStyle, GUILayout.Width(80));
            mapY = Regex.Replace(mapY, "[^0-9\\.-]", "");
            
            if (GUILayout.Button("Teleport to Map", _buttonStyle, GUILayout.Width(120)))
            {
                float x = 0f, y = 0f;
                bool validX = float.TryParse(mapX, out x);
                bool validY = float.TryParse(mapY, out y);
                
                if (validX && validY)
                {
                    Vector2 mapPos = new Vector2(x, y);
                    if (_teleportManager.TeleportToMapPosition(mapPos))
                    {
                        AddToTeleportHistory($"Teleported to map position: {mapPos}");
                    }
                }
                else
                {
                    MelonLogger.Warning("[GUIManager] Invalid map coordinates");
                }
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // Teleport history
            GUILayout.Label("<b>Teleport History:</b>", _labelStyle);
            
            _teleportScrollPos = GUILayout.BeginScrollView(_teleportScrollPos, GUI.skin.box, GUILayout.Height(150));
            
            if (_teleportHistory.Count > 0)
            {
                foreach (string entry in _teleportHistory)
                {
                    GUILayout.Label(entry, _labelStyle);
                }
            }
            else
            {
                GUILayout.Label("No teleport history yet", _labelStyle);
            }
            
            GUILayout.EndScrollView();
            
            GUILayout.Space(10);
            
            // Help text
            GUILayout.Label("<b>Keyboard Shortcuts:</b>", _labelStyle);
            GUILayout.Label("• T: Teleport to map cursor position", _labelStyle);
            GUILayout.Label("• P: Save current position", _labelStyle);
            GUILayout.Label("• Backspace: Teleport to previous position", _labelStyle);
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// Adds an entry to the teleport history
        /// </summary>
        private void AddToTeleportHistory(string message)
        {
            // Add timestamp
            string entry = $"[{DateTime.Now.ToString("HH:mm:ss")}] {message}";
            _teleportHistory.Add(entry);
            
            // Limit history size
            if (_teleportHistory.Count > MAX_TELEPORT_HISTORY)
            {
                _teleportHistory.RemoveAt(0);
            }
        }
    }
}
