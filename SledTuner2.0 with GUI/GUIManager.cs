using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;

namespace SledTunerProject
{
    public class GUIManager
    {
        // === REFERENCES ===
        private SledParameterManager _sledParameterManager;
        private ConfigManager _configManager;

        // === WINDOW & FIELD STATE ===
        private bool _menuOpen = false;
        private Rect _windowRect;
        private Vector2 _scrollPos = Vector2.zero;
        // Field inputs: Component name -> (Field name -> string value)
        private Dictionary<string, Dictionary<string, string>> _fieldInputs = new Dictionary<string, Dictionary<string, string>>();

        // === GUI STYLES (Initialized lazily in OnGUI) ===
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _headerStyle;

        // === ADDITIONAL FEATURES STATE ===
        private bool _manualApply = true;     // If true, changes are not auto-applied.
        private bool _showHelp = false;         // Toggle for help panel.
        private bool _advancedView = false;     // Toggle between simple and advanced view.

        // === WINDOW CONTROLS & RESIZING ===
        private bool _isMinimized = false;
        private Rect _prevWindowRect;
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Rect _resizeStartWindowRect;
        private ResizeEdges _resizeEdges;
        private float _opacity = 1f;            // 0 (transparent) to 1 (opaque)

        private struct ResizeEdges
        {
            public bool left, right, top, bottom;
        }

        // === COLOR PREVIEW TEXTURE (for headlight color) ===
        private Texture2D _colorPreviewTexture;

        // === CONSTRUCTOR ===
        public GUIManager(SledParameterManager sledParameterManager, ConfigManager configManager)
        {
            _sledParameterManager = sledParameterManager;
            _configManager = configManager;
            // Initialize window to cover 60% of the screen, centered.
            _windowRect = new Rect(Screen.width * 0.2f, Screen.height * 0.2f,
                                     Screen.width * 0.6f, Screen.height * 0.6f);
            _resizeEdges = new ResizeEdges();
        }

        // === PUBLIC METHODS ===

        /// <summary>
        /// Toggle the GUI menu on or off.
        /// When opening, refresh field values.
        /// </summary>
        public void ToggleMenu()
        {
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
        /// Refresh the field inputs dictionary from SledParameterManager.
        /// </summary>
        public void RePopulateFields()
        {
            _fieldInputs.Clear();
            Dictionary<string, Dictionary<string, object>> currentParams = _sledParameterManager.GetCurrentParameters();
            foreach (var compEntry in _sledParameterManager.ComponentsToInspect)
            {
                string compName = compEntry.Key;
                _fieldInputs[compName] = new Dictionary<string, string>();
                foreach (string field in compEntry.Value)
                {
                    object val = _sledParameterManager.GetFieldValue(compName, field);
                    _fieldInputs[compName][field] = (val != null) ? val.ToString() : "(No data)";
                }
            }
            MelonLogger.Msg("[GUIManager] Fields repopulated.");
        }

        /// <summary>
        /// Draw the GUI window; call this from your Main.OnGUI().
        /// </summary>
        public void DrawMenu()
        {
            if (!_menuOpen)
                return;

            // Lazy initialize GUI styles (requires GUI.skin; do this in OnGUI)
            if (_windowStyle == null)
            {
                _windowStyle = new GUIStyle(GUI.skin.window);
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 12
                };
                _textFieldStyle = new GUIStyle(GUI.skin.textField);
                _buttonStyle = new GUIStyle(GUI.skin.button);
                _toggleStyle = new GUIStyle(GUI.skin.toggle);
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
            }

            // Apply window opacity.
            Color prevColor = GUI.color;
            GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, _opacity);

            // Draw the window.
            _windowRect = GUILayout.Window(1234, _windowRect, WindowFunction, "SledTuner Menu", _windowStyle);

            GUI.color = prevColor; // restore color

            // Handle window resizing.
            HandleResize();
        }

        // === PRIVATE GUI DRAWING METHODS ===

        /// <summary>
        /// Main window function that draws the contents.
        /// </summary>
        private void WindowFunction(int windowID)
        {
            // --- Title Bar with window controls ---
            DrawTitleBar();
            GUILayout.Space(5);

            // --- Config Buttons: Load, Save, Reset, Apply ---
            DrawConfigButtons();
            GUILayout.Space(5);

            // --- Mode Toggles: Manual Apply and Advanced View ---
            DrawModeToggles();
            GUILayout.Space(5);

            // --- Main Parameters Area ---
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            if (_advancedView)
                DrawTreeViewParameters();
            else
                DrawSimpleParameters();
            GUILayout.EndScrollView();

            // --- Footer Buttons: Extra Toggles and Teleport ---
            GUILayout.Space(5);
            DrawFooter();
            GUILayout.Space(5);
            DrawOpacitySlider();

            // Make the window draggable (by its top area).
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        /// <summary>
        /// Draw the title bar with header and window control buttons.
        /// </summary>
        private void DrawTitleBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("SledTuner Menu", _headerStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("?", _buttonStyle, GUILayout.Width(30)))
            {
                _showHelp = !_showHelp;
            }
            if (GUILayout.Button("_", _buttonStyle, GUILayout.Width(30)))
            {
                MinimizeWindow();
            }
            if (GUILayout.Button("[ ]", _buttonStyle, GUILayout.Width(30)))
            {
                MaximizeWindow();
            }
            if (GUILayout.Button("X", _buttonStyle, GUILayout.Width(30)))
            {
                CloseWindow();
            }
            GUILayout.EndHorizontal();

            if (_showHelp)
            {
                DrawHelpPanel();
                GUILayout.Space(5);
            }
        }

        /// <summary>
        /// Draw the configuration buttons: Load, Save, Reset, Apply.
        /// </summary>
        private void DrawConfigButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.LoadConfiguration();
                RePopulateFields();
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

        /// <summary>
        /// Draw toggles for Manual Apply and Advanced View.
        /// </summary>
        private void DrawModeToggles()
        {
            GUILayout.BeginHorizontal();
            _manualApply = GUILayout.Toggle(_manualApply, "Manual Apply", _toggleStyle, GUILayout.Width(120));
            _advancedView = GUILayout.Toggle(_advancedView, "Advanced View", _toggleStyle, GUILayout.Width(120));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draw parameters in a flat, simple view.
        /// </summary>
        private void DrawSimpleParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                GUILayout.Label("<b>Component: " + comp.Key + "</b>", _labelStyle);
                foreach (var field in comp.Value)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(field.Key + ":", _labelStyle, GUILayout.Width(150));
                    Type fieldType = _sledParameterManager.GetFieldType(comp.Key, field.Key);

                    // For numeric fields, draw slider + plus/minus buttons.
                    if (fieldType == typeof(float) || fieldType == typeof(int))
                    {
                        float currentVal = 0f;
                        float.TryParse(field.Value, out currentVal);
                        float sliderMin = _sledParameterManager.GetSliderMin(comp.Key, field.Key);
                        float sliderMax = _sledParameterManager.GetSliderMax(comp.Key, field.Key);
                        float step = (fieldType == typeof(float)) ? 0.1f : 1f;

                        float newVal = GUILayout.HorizontalSlider(currentVal, sliderMin, sliderMax, GUILayout.Width(150));
                        string newText = GUILayout.TextField(newVal.ToString("F2"), _textFieldStyle, GUILayout.Width(50));
                        if (float.TryParse(newText, out float parsedVal))
                        {
                            newVal = parsedVal;
                        }
                        GUILayout.BeginHorizontal(GUILayout.Width(60));
                        if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(25)))
                        {
                            newVal = Mathf.Max(sliderMin, newVal - step);
                        }
                        if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(25)))
                        {
                            newVal = Mathf.Min(sliderMax, newVal + step);
                        }
                        GUILayout.EndHorizontal();

                        string finalValStr = newVal.ToString();
                        if (finalValStr != field.Value)
                        {
                            _fieldInputs[comp.Key][field.Key] = finalValStr;
                            object convertedValue = ConvertInput(finalValStr, fieldType);
                            _sledParameterManager.SetFieldValue(comp.Key, field.Key, convertedValue);
                            if (!_manualApply)
                            {
                                _sledParameterManager.ApplyParameters();
                            }
                        }
                    }
                    // For boolean fields, draw a toggle.
                    else if (fieldType == typeof(bool))
                    {
                        bool currentBool = false;
                        bool.TryParse(field.Value, out currentBool);
                        bool newBool = GUILayout.Toggle(currentBool, currentBool ? "On" : "Off", _toggleStyle, GUILayout.Width(80));
                        if (newBool != currentBool)
                        {
                            _fieldInputs[comp.Key][field.Key] = newBool.ToString();
                            _sledParameterManager.SetFieldValue(comp.Key, field.Key, newBool);
                            _sledParameterManager.ApplyParameters();
                        }
                    }
                    // For other types, use a plain text field.
                    else
                    {
                        string newValue = GUILayout.TextField(field.Value, _textFieldStyle, GUILayout.ExpandWidth(true));
                        if (newValue != field.Value)
                        {
                            _fieldInputs[comp.Key][field.Key] = newValue;
                            object conv = ConvertInput(newValue, fieldType);
                            _sledParameterManager.SetFieldValue(comp.Key, field.Key, conv);
                            if (!_manualApply)
                            {
                                _sledParameterManager.ApplyParameters();
                            }
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                // Special handling for the Light component: draw a live color preview after its parameters.
                if (comp.Key == "Light")
                {
                    GUILayout.Space(5);
                    GUILayout.Label("<b>Headlight Color Preview:</b>", _labelStyle);
                    DrawLightColorPreview();
                }
                GUILayout.Space(10);
            }
        }

        /// <summary>
        /// Draw parameters in a tree view (collapsible foldouts).
        /// </summary>
        private void DrawTreeViewParameters()
        {
            // For simplicity, use a temporary dictionary to hold foldout states.
            Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
            foreach (var comp in _fieldInputs)
            {
                if (!foldouts.ContainsKey(comp.Key))
                    foldouts[comp.Key] = true; // default expanded

                foldouts[comp.Key] = GUILayout.Toggle(foldouts[comp.Key], "<b>" + comp.Key + "</b>", _foldoutStyle);
                if (foldouts[comp.Key])
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    DrawSimpleParametersForComponent(comp.Key, comp.Value);
                    GUILayout.EndVertical();
                }
                GUILayout.Space(10);
            }
        }

        /// <summary>
        /// Helper to draw parameters for one component (used in tree view).
        /// </summary>
        private void DrawSimpleParametersForComponent(string compName, Dictionary<string, string> fields)
        {
            foreach (var field in fields)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(field.Key + ":", _labelStyle, GUILayout.Width(150));
                Type fieldType = _sledParameterManager.GetFieldType(compName, field.Key);
                if (fieldType == typeof(float) || fieldType == typeof(int))
                {
                    float currentVal = 0f;
                    float.TryParse(field.Value, out currentVal);
                    float sliderMin = _sledParameterManager.GetSliderMin(compName, field.Key);
                    float sliderMax = _sledParameterManager.GetSliderMax(compName, field.Key);
                    float step = (fieldType == typeof(float)) ? 0.1f : 1f;
                    float newVal = GUILayout.HorizontalSlider(currentVal, sliderMin, sliderMax, GUILayout.Width(150));
                    string newText = GUILayout.TextField(newVal.ToString("F2"), _textFieldStyle, GUILayout.Width(50));
                    if (float.TryParse(newText, out float parsedVal))
                    {
                        newVal = parsedVal;
                    }
                    GUILayout.BeginHorizontal(GUILayout.Width(60));
                    if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(25)))
                    {
                        newVal = Mathf.Max(sliderMin, newVal - step);
                    }
                    if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(25)))
                    {
                        newVal = Mathf.Min(sliderMax, newVal + step);
                    }
                    GUILayout.EndHorizontal();
                    string finalValStr = newVal.ToString();
                    if (finalValStr != field.Value)
                    {
                        _fieldInputs[compName][field.Key] = finalValStr;
                        object convertedValue = ConvertInput(finalValStr, fieldType);
                        _sledParameterManager.SetFieldValue(compName, field.Key, convertedValue);
                        if (!_manualApply)
                        {
                            _sledParameterManager.ApplyParameters();
                        }
                    }
                }
                else if (fieldType == typeof(bool))
                {
                    bool currentBool = false;
                    bool.TryParse(field.Value, out currentBool);
                    bool newBool = GUILayout.Toggle(currentBool, currentBool ? "On" : "Off", _toggleStyle, GUILayout.Width(80));
                    if (newBool != currentBool)
                    {
                        _fieldInputs[compName][field.Key] = newBool.ToString();
                        _sledParameterManager.SetFieldValue(compName, field.Key, newBool);
                        _sledParameterManager.ApplyParameters();
                    }
                }
                else
                {
                    string newValue = GUILayout.TextField(field.Value, _textFieldStyle, GUILayout.ExpandWidth(true));
                    if (newValue != field.Value)
                    {
                        _fieldInputs[compName][field.Key] = newValue;
                        object conv = ConvertInput(newValue, fieldType);
                        _sledParameterManager.SetFieldValue(compName, field.Key, conv);
                        if (!_manualApply)
                        {
                            _sledParameterManager.ApplyParameters();
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// Draw a live preview of the headlight color using the Light component’s RGBA values.
        /// The preview is drawn as a fixed 30×30 rectangle.
        /// </summary>
        private void DrawLightColorPreview()
        {
            // Retrieve the current RGBA values from SledParameterManager.
            float r = GetFieldFloat("Light", "r", 1f);
            float g = GetFieldFloat("Light", "g", 1f);
            float b = GetFieldFloat("Light", "b", 1f);
            float a = GetFieldFloat("Light", "a", 1f);
            Color currentColor = new Color(r, g, b, a);

            // Create or update the preview texture (1x1 pixel).
            if (_colorPreviewTexture == null)
            {
                _colorPreviewTexture = new Texture2D(1, 1);
                _colorPreviewTexture.hideFlags = HideFlags.HideAndDontSave;
            }
            _colorPreviewTexture.SetPixel(0, 0, currentColor);
            _colorPreviewTexture.Apply();

            // Draw the texture in a fixed 30×30 rect (similar to the size of window control buttons).
            Rect previewRect = GUILayoutUtility.GetRect(30, 30, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(previewRect, _colorPreviewTexture, ScaleMode.StretchToFill);
        }

        /// <summary>
        /// Helper method to get a float value from SledParameterManager for a given field.
        /// </summary>
        private float GetFieldFloat(string compName, string fieldName, float defaultValue)
        {
            object obj = _sledParameterManager.GetFieldValue(compName, fieldName);
            if (obj != null && float.TryParse(obj.ToString(), out float val))
                return val;
            return defaultValue;
        }

        /// <summary>
        /// Draw the footer that contains extra toggle buttons and a Teleport button.
        /// </summary>
        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle Ragdoll", _buttonStyle, GUILayout.Width(120)))
            {
                _configManager.ToggleRagdoll();
            }
            if (GUILayout.Button("Toggle Tree Renderer", _buttonStyle, GUILayout.Width(150)))
            {
                _configManager.ToggleTreeRenderer();
            }
            if (GUILayout.Button("Teleport", _buttonStyle, GUILayout.Width(100)))
            {
                _configManager.TeleportSled();
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draw an opacity slider to adjust the menu’s transparency.
        /// </summary>
        private void DrawOpacitySlider()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Opacity:", GUILayout.Width(70));
            float newOpacity = GUILayout.HorizontalSlider(_opacity, 0.1f, 1f, GUILayout.Width(150));
            _opacity = newOpacity;
            GUILayout.Label((_opacity * 100f).ToString("F0") + "%", GUILayout.Width(40));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Apply all current GUI values to SledParameterManager.
        /// </summary>
        private void ApplyChanges()
        {
            foreach (var compEntry in _fieldInputs)
            {
                string compName = compEntry.Key;
                foreach (var fieldEntry in compEntry.Value)
                {
                    string fieldName = fieldEntry.Key;
                    string valueStr = fieldEntry.Value;
                    Type ft = _sledParameterManager.GetFieldType(compName, fieldName);
                    object conv = ConvertInput(valueStr, ft);
                    _sledParameterManager.SetFieldValue(compName, fieldName, conv);
                }
            }
            _sledParameterManager.ApplyParameters();
            MelonLogger.Msg("[GUIManager] Changes applied.");
        }

        // === UTILITY METHODS ===

        /// <summary>
        /// Converts a string input to the target type.
        /// </summary>
        private object ConvertInput(string input, Type targetType)
        {
            if (targetType == typeof(float))
            {
                if (float.TryParse(input, out float f))
                    return f;
                return 0f;
            }
            else if (targetType == typeof(int))
            {
                if (int.TryParse(input, out int i))
                    return i;
                return 0;
            }
            else if (targetType == typeof(bool))
            {
                if (bool.TryParse(input, out bool b))
                    return b;
                return false;
            }
            return input;
        }

        /// <summary>
        /// Handle window resizing by checking if the mouse is near the edges.
        /// </summary>
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

        // === WINDOW CONTROL METHODS ===

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

        /// <summary>
        /// Draw a help panel with usage instructions.
        /// </summary>
        private void DrawHelpPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Help:</b>\n\n" +
                "Hotkeys:\n  F2 - Toggle Menu\n  F3 - Refresh Fields\n\n" +
                "Instructions:\n" +
                "  - Adjust parameters using sliders, text fields, or +/- buttons.\n" +
                "  - If 'Manual Apply' is enabled, changes take effect only after pressing 'Apply'.\n" +
                "  - Use the window buttons to Minimize, Maximize, or Close the menu.\n" +
                "  - Footer buttons include toggles for Ragdoll, Tree Renderer, and Teleport.\n" +
                "  - 'Advanced View' reveals additional options (if available).\n", _labelStyle);
            GUILayout.EndVertical();
        }
    }
}
