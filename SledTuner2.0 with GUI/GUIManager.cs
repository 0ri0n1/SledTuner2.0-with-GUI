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
        // Persistent foldout states for Advanced (Tree) view.
        private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        // === GUI STYLES (initialized lazily in OnGUI) ===
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _headerStyle;

        // === ADDITIONAL FEATURES STATE ===
        private bool _manualApply = true;         // If true, changes take effect only after pressing "Apply".
        private bool _showHelp = false;           // Toggle for help panel.
        private bool _advancedView = true;        // _advancedView = true means using the full tuner menu; false = simple tuner menu.
        private bool _treeViewEnabled = true;     // Within Advanced view, _treeViewEnabled toggles collapsible (tree) layout.

        // === WINDOW CONTROLS & RESIZING ===
        private bool _isMinimized = false;
        private Rect _prevWindowRect;
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Rect _resizeStartWindowRect;
        private ResizeEdges _resizeEdges;
        private float _opacity = 1f;              // 0 (transparent) to 1 (opaque)

        private struct ResizeEdges
        {
            public bool left, right, top, bottom;
        }

        // === SIMPLE VIEW LOCAL PARAMETERS (for the simple tuner menu) ===
        private float speed = 10f;
        private float gravity = -9.81f;
        private float power = 143000f;
        private float lugHeight = 0.18f;
        private float trackLength = 1f;
        private float pitchFactor = 7f;
        private float lightR = 1f;
        private float lightG = 1f;
        private float lightB = 1f;
        private float lightA = 1f;
        private bool notdriverInvincible = true;
        private bool test = false;
        private bool apply = false;

        // Original values for Reset (for simple view)
        private float originalPower = 143000f;
        private float originalLugHeight = 0.18f;
        private float originalTrackLength = 1f;
        private float originalGravity = -9.81f;
        private float originalPitchFactor = 7f;

        // === COLOR PREVIEW TEXTURE ===
        private Texture2D _colorPreviewTexture;

        // === CONSTRUCTOR ===
        public GUIManager(SledParameterManager sledParameterManager, ConfigManager configManager)
        {
            _sledParameterManager = sledParameterManager;
            _configManager = configManager;
            _windowRect = new Rect(Screen.width * 0.2f, Screen.height * 0.2f,
                                   Screen.width * 0.6f, Screen.height * 0.6f);
            _resizeEdges = new ResizeEdges();
            _advancedView = true;
            _treeViewEnabled = true;
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
        /// (Used by both Advanced and Simple views.)
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
            foreach (var comp in _fieldInputs.Keys)
            {
                if (!_foldoutStates.ContainsKey(comp))
                    _foldoutStates[comp] = true;
            }
            MelonLogger.Msg("[GUIManager] Fields repopulated.");
        }

        /// <summary>
        /// Draw the GUI window; call this from Main.OnGUI().
        /// </summary>
        public void DrawMenu()
        {
            if (!_menuOpen)
                return;

            if (_windowStyle == null)
            {
                _windowStyle = new GUIStyle(GUI.skin.window);
                _labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
                _textFieldStyle = new GUIStyle(GUI.skin.textField);
                _buttonStyle = new GUIStyle(GUI.skin.button);
                _toggleStyle = new GUIStyle(GUI.skin.toggle);
                _foldoutStyle = new GUIStyle(GUI.skin.toggle) { richText = true, fontSize = 13 };
                _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            }

            Color prevColor = GUI.color;
            GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, _opacity);

            _windowRect = GUILayout.Window(1234, _windowRect, WindowFunction, "SledTuner Menu", _windowStyle);

            GUI.color = prevColor;
            HandleResize();
        }

        // === PRIVATE GUI DRAWING METHODS ===

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

            if (_advancedView)
            {
                DrawAdvancedTunerMenu();
            }
            else
            {
                DrawSimpleTunerMenu();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
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

        private void DrawAdvancedTunerMenu()
        {
            DrawConfigButtons();
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            _manualApply = GUILayout.Toggle(_manualApply, "Manual Apply", _toggleStyle, GUILayout.Width(120));
            _treeViewEnabled = GUILayout.Toggle(_treeViewEnabled, "Tree View", _toggleStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            if (_treeViewEnabled)
                DrawTreeViewParameters();
            else
                DrawAdvancedFlatParameters();
            GUILayout.EndScrollView();

            GUILayout.Space(5);
            DrawFooter();
            GUILayout.Space(5);
            DrawOpacitySlider();
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

            GUILayout.Label("FlySpeed");
            speed = FloatFieldWithSlider(speed, 0f, 200f, 0.1f);

            GUILayout.Label("Gravity");
            gravity = FloatFieldWithSlider(gravity, -10f, 10f, 0.1f);

            GUILayout.Label("Power");
            power = FloatFieldWithSlider(power, 0f, 300000f, 1000f);

            GUILayout.Label("Lug Height");
            lugHeight = FloatFieldWithSlider(lugHeight, 0f, 2f, 0.01f);

            GUILayout.Label("Track Length");
            trackLength = FloatFieldWithSlider(trackLength, 0.5f, 2f, 0.01f);

            GUILayout.Label("Pitch Factor");
            pitchFactor = FloatFieldWithSlider(pitchFactor, 2f, 30f, 0.1f);

            GUILayout.Space(10);
            GUILayout.Label("Headlight Color (RGBA)");
            lightR = FloatFieldWithSliderWithButtons("R", lightR, 0f, 1f, 0.1f);
            lightG = FloatFieldWithSliderWithButtons("G", lightG, 0f, 1f, 0.1f);
            lightB = FloatFieldWithSliderWithButtons("B", lightB, 0f, 1f, 0.1f);
            lightA = FloatFieldWithSliderWithButtons("A", lightA, 0f, 1f, 0.1f);

            GUILayout.Space(5);
            GUILayout.Label("Color Preview:");
            Color currentColor = new Color(lightR, lightG, lightB, lightA);
            UpdateColorPreviewTexture(currentColor);
            GUILayout.Box(_colorPreviewTexture, GUILayout.Width(30), GUILayout.Height(30));

            GUILayout.Space(10);
            notdriverInvincible = GUILayout.Toggle(notdriverInvincible, "Driver Ragdoll", _toggleStyle, GUILayout.Width(150));
            test = GUILayout.Toggle(test, "Test", _toggleStyle, GUILayout.Width(150));

            GUILayout.Space(10);
            GUILayout.Label("Made by Samisalami", _labelStyle, GUILayout.Width(200));
            GUILayout.EndVertical();
        }

        private void DrawAdvancedFlatParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                GUILayout.Label("<b>Component: " + comp.Key + "</b>", _labelStyle);
                DrawSimpleParametersForComponent(comp.Key, comp.Value);
                if (comp.Key == "Light")
                {
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
                GUILayout.Space(10);
            }
        }

        private void DrawTreeViewParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                if (!_foldoutStates.ContainsKey(comp.Key))
                    _foldoutStates[comp.Key] = true;

                _foldoutStates[comp.Key] = GUILayout.Toggle(_foldoutStates[comp.Key], "<b>" + comp.Key + "</b>", _foldoutStyle);
                if (_foldoutStates[comp.Key])
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    DrawSimpleParametersForComponent(comp.Key, comp.Value);
                    if (comp.Key == "Light")
                    {
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
                    GUILayout.EndVertical();
                }
                GUILayout.Space(10);
            }
        }

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
                        newVal = parsedVal;
                    GUILayout.BeginHorizontal(GUILayout.Width(60));
                    if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(25)))
                        newVal = Mathf.Max(sliderMin, newVal - step);
                    if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(25)))
                        newVal = Mathf.Min(sliderMax, newVal + step);
                    GUILayout.EndHorizontal();
                    string finalValStr = newVal.ToString();
                    if (finalValStr != field.Value)
                    {
                        _fieldInputs[compName][field.Key] = finalValStr;
                        object convertedValue = ConvertInput(finalValStr, fieldType);
                        _sledParameterManager.SetFieldValue(compName, field.Key, convertedValue);
                        if (!_manualApply)
                            _sledParameterManager.ApplyParameters();
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
                            _sledParameterManager.ApplyParameters();
                    }
                }
                GUILayout.EndHorizontal();
            }
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
            GUILayout.Label("Opacity:", GUILayout.Width(70));
            float newOpacity = GUILayout.HorizontalSlider(_opacity, 0.1f, 1f, GUILayout.Width(150));
            _opacity = newOpacity;
            GUILayout.Label((_opacity * 100f).ToString("F0") + "%", GUILayout.Width(40));
            GUILayout.EndHorizontal();
        }

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

        private float FloatFieldWithSlider(float currentVal, float min, float max, float step)
        {
            float newVal = GUILayout.HorizontalSlider(currentVal, min, max, GUILayout.Width(150));
            string textVal = GUILayout.TextField(newVal.ToString("F2"), GUILayout.Width(50));
            if (float.TryParse(textVal, out float parsed))
                newVal = parsed;
            GUILayout.BeginHorizontal(GUILayout.Width(60));
            if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(25)))
                newVal = Mathf.Max(min, newVal - step);
            if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(25)))
                newVal = Mathf.Min(max, newVal + step);
            GUILayout.EndHorizontal();
            return newVal;
        }

        private float FloatFieldWithSliderWithButtons(string label, float currentVal, float min, float max, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(20));
            float newVal = GUILayout.HorizontalSlider(currentVal, min, max, GUILayout.Width(150));
            string textVal = GUILayout.TextField(newVal.ToString("F2"), _textFieldStyle, GUILayout.Width(40));
            if (float.TryParse(textVal, out float parsed))
                newVal = parsed;
            if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(25)))
                newVal = Mathf.Max(min, newVal - step);
            if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(25)))
                newVal = Mathf.Min(max, newVal + step);
            GUILayout.EndHorizontal();
            return newVal;
        }

        private void ResetValues()
        {
            speed = 10f;
            gravity = originalGravity;
            power = originalPower;
            lugHeight = originalLugHeight;
            trackLength = originalTrackLength;
            pitchFactor = originalPitchFactor;
            lightR = 1f;
            lightG = 1f;
            lightB = 1f;
            lightA = 1f;
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
                "  - Adjust parameters using sliders, text fields, or +/- buttons.\n" +
                "  - If 'Manual Apply' is enabled, changes take effect only after pressing 'Apply'.\n" +
                "  - Use the window buttons to Minimize, Maximize, or Close the menu.\n" +
                "  - Footer buttons include toggles for Ragdoll, Tree Renderer, and Teleport.\n" +
                "  - In Advanced view, use the 'Tree View' toggle to collapse or expand components.\n" +
                "  - 'Switch View' toggles between Advanced and Simple tuner menus.\n", _labelStyle);
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
    }
}
