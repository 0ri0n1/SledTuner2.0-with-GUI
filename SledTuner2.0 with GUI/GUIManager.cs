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
        private Dictionary<string, Dictionary<string, string>> _fieldInputs = new Dictionary<string, Dictionary<string, string>>();
        private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        // === GUI STYLES ===
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _headerStyle;

        // === STATE ===
        private bool _manualApply = true;
        private bool _showHelp = false;
        private bool _advancedView = true;
        private bool _treeViewEnabled = true;

        // === WINDOW / RESIZE ===
        private bool _isMinimized = false;
        private Rect _prevWindowRect;
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Rect _resizeStartWindowRect;
        private ResizeEdges _resizeEdges;
        private float _opacity = 1f;

        private struct ResizeEdges
        {
            public bool left, right, top, bottom;
        }

        // === SIMPLE VIEW LOCAL PARAMS ===
        private float speed = 10f;
        private float gravity = -9.81f;
        private float power = 143000f;
        private float lugHeight = 0.18f;
        private float trackLength = 1f;
        private float pitchFactor = 7f;
        private bool notdriverInvincible = true;
        private bool test = false;
        private bool apply = false;

        private float originalPower = 143000f;
        private float originalLugHeight = 0.18f;
        private float originalTrackLength = 1f;
        private float originalGravity = -9.81f;
        private float originalPitchFactor = 7f;

        private Texture2D _colorPreviewTexture;

        // NEW: Track hold states, but we only apply increments on Repaint
        private Dictionary<string, bool> _pressingMinus = new Dictionary<string, bool>();
        private Dictionary<string, bool> _pressingPlus = new Dictionary<string, bool>();
        private Dictionary<string, float> _holdTimeMinus = new Dictionary<string, float>();
        private Dictionary<string, float> _holdTimePlus = new Dictionary<string, float>();

        public GUIManager(SledParameterManager sledParameterManager, ConfigManager configManager)
        {
            _sledParameterManager = sledParameterManager;
            _configManager = configManager;
            _windowRect = new Rect(Screen.width * 0.2f, Screen.height * 0.2f,
                                   Screen.width * 0.6f, Screen.height * 0.6f);
            _resizeEdges = new ResizeEdges();
        }

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

        public void DrawMenu()
        {
            if (!_menuOpen) return;

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

            // IMPORTANT: Only apply increments once, e.g. on EventType.Repaint
            if (Event.current.type == EventType.Repaint)
            {
                UpdateHeldButtons();
            }

            GUI.color = prevColor;
            HandleResize();
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

        // NEW: This method updates increments on Repaint
        private void UpdateHeldButtons()
        {
            float dt = Time.deltaTime;

            // minus
            foreach (var kvp in _pressingMinus)
            {
                string key = kvp.Key; // e.g. "simple.speed.minus"
                bool isHeld = kvp.Value;
                if (!isHeld)
                {
                    _holdTimeMinus[key] = 0f;
                    continue;
                }

                // It's held. Do acceleration
                if (!_holdTimeMinus.ContainsKey(key))
                    _holdTimeMinus[key] = 0f;
                _holdTimeMinus[key] += dt;

                // key might be "compName.fieldName.minus"
                float baseStep = 0.01f;
                float holdTime = _holdTimeMinus[key];
                float dynamicStep = baseStep + (0.05f * holdTime);
                if (dynamicStep > 0.5f) dynamicStep = 0.5f;

                // Now parse out the actual comp/field
                string noSuffix = key.Replace(".minus", "");
                string[] parts = noSuffix.Split('.');
                if (parts.Length < 2) continue;

                string compName = parts[0];
                string fieldName = parts[1];
                // If it's "simple.speed", handle local variable
                if (compName == "simple")
                {
                    switch (fieldName)
                    {
                        case "speed": speed = Mathf.Max(speed - (dynamicStep * dt), 0f); break;
                        case "gravity": gravity = gravity - (dynamicStep * dt); break;
                        case "power": power = Mathf.Max(power - (dynamicStep * dt), 0f); break;
                        case "lugHeight": lugHeight = Mathf.Max(lugHeight - (dynamicStep * dt), 0f); break;
                        case "trackLength": trackLength = Mathf.Max(trackLength - (dynamicStep * dt), 0.1f); break;
                        case "pitchFactor": pitchFactor = Mathf.Max(pitchFactor - (dynamicStep * dt), 0.1f); break;
                    }
                }
                else
                {
                    // Reflection-based field
                    double oldVal;
                    double.TryParse(_fieldInputs[compName][fieldName], out oldVal);
                    double newVal = oldVal - (dynamicStep * dt);
                    // clamp?
                    float min = _sledParameterManager.GetSliderMin(compName, fieldName);
                    if (newVal < min) newVal = min;
                    _fieldInputs[compName][fieldName] = newVal.ToString("F2");
                    _sledParameterManager.SetFieldValue(compName, fieldName, newVal);
                    if (!_manualApply)
                        _sledParameterManager.ApplyParameters();
                }
            }

            // plus
            foreach (var kvp in _pressingPlus)
            {
                string key = kvp.Key;
                bool isHeld = kvp.Value;
                if (!isHeld)
                {
                    _holdTimePlus[key] = 0f;
                    continue;
                }

                if (!_holdTimePlus.ContainsKey(key))
                    _holdTimePlus[key] = 0f;
                _holdTimePlus[key] += dt;

                float baseStep = 0.01f;
                float holdTime = _holdTimePlus[key];
                float dynamicStep = baseStep + (0.05f * holdTime);
                if (dynamicStep > 0.5f) dynamicStep = 0.5f;

                string noSuffix = key.Replace(".plus", "");
                string[] parts = noSuffix.Split('.');
                if (parts.Length < 2) continue;

                string compName = parts[0];
                string fieldName = parts[1];
                if (compName == "simple")
                {
                    switch (fieldName)
                    {
                        case "speed": speed = Mathf.Min(speed + (dynamicStep * dt), 999999f); break;
                        case "gravity": gravity = gravity + (dynamicStep * dt); break;
                        case "power": power = Mathf.Min(power + (dynamicStep * dt), 999999f); break;
                        case "lugHeight": lugHeight = Mathf.Min(lugHeight + (dynamicStep * dt), 999f); break;
                        case "trackLength": trackLength = Mathf.Min(trackLength + (dynamicStep * dt), 999f); break;
                        case "pitchFactor": pitchFactor = Mathf.Min(pitchFactor + (dynamicStep * dt), 999f); break;
                    }
                }
                else
                {
                    double oldVal;
                    double.TryParse(_fieldInputs[compName][fieldName], out oldVal);
                    double newVal = oldVal + (dynamicStep * dt);
                    float max = _sledParameterManager.GetSliderMax(compName, fieldName);
                    if (newVal > max) newVal = max;
                    _fieldInputs[compName][fieldName] = newVal.ToString("F2");
                    _sledParameterManager.SetFieldValue(compName, fieldName, newVal);
                    if (!_manualApply)
                        _sledParameterManager.ApplyParameters();
                }
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
            speed = DrawRepeatSlider("simple.speed", speed, 0f, 200f);

            GUILayout.Label("Gravity");
            gravity = DrawRepeatSlider("simple.gravity", gravity, -10f, 10f);

            GUILayout.Label("Power");
            power = DrawRepeatSlider("simple.power", power, 0f, 300000f);

            GUILayout.Label("Lug Height");
            lugHeight = DrawRepeatSlider("simple.lugHeight", lugHeight, 0f, 2f);

            GUILayout.Label("Track Length");
            trackLength = DrawRepeatSlider("simple.trackLength", trackLength, 0.5f, 2f);

            GUILayout.Label("Pitch Factor");
            pitchFactor = DrawRepeatSlider("simple.pitchFactor", pitchFactor, 2f, 30f);

            GUILayout.Space(10);
            GUILayout.Label("Headlight Color (RGBA)");
            if (_fieldInputs.ContainsKey("Light"))
            {
                DrawColorReflectionWithHold("Light", _fieldInputs["Light"]);
            }
            else
            {
                GUILayout.Label("(No Light component found)", _labelStyle);
            }

            float rVal = 1f, gVal = 1f, bVal = 1f, aVal = 1f;
            if (_fieldInputs.ContainsKey("Light"))
            {
                if (_fieldInputs["Light"].TryGetValue("r", out string tmp)) float.TryParse(tmp, out rVal);
                if (_fieldInputs["Light"].TryGetValue("g", out tmp)) float.TryParse(tmp, out gVal);
                if (_fieldInputs["Light"].TryGetValue("b", out tmp)) float.TryParse(tmp, out bVal);
                if (_fieldInputs["Light"].TryGetValue("a", out tmp)) float.TryParse(tmp, out aVal);
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

        // NEW: "DrawRepeatSlider" just draws a normal slider + text, but we store minus/plus press states.
        private float DrawRepeatSlider(string uniqueKey, float currentVal, float min, float max)
        {
            float val = GUILayout.HorizontalSlider(currentVal, min, max, GUILayout.Width(150));
            string textVal = GUILayout.TextField(val.ToString("F2"), GUILayout.Width(50));
            if (float.TryParse(textVal, out float parsed))
                val = parsed;

            GUILayout.BeginHorizontal(GUILayout.Width(60));

            string minusKey = uniqueKey + ".minus";
            string plusKey = uniqueKey + ".plus";

            // Instead of changing the value immediately, we store isHeld in a dictionary:
            bool pressingMinus = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
            bool pressingPlus = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));

            _pressingMinus[minusKey] = pressingMinus;
            _pressingPlus[plusKey] = pressingPlus;

            GUILayout.EndHorizontal();
            return val;
        }

        // NEW: Similar approach for Light color channels in SimpleTuner
        private void DrawColorReflectionWithHold(string compName, Dictionary<string, string> lightFields)
        {
            string[] channels = { "r", "g", "b", "a" };
            foreach (string channel in channels)
            {
                if (!lightFields.ContainsKey(channel)) continue;

                float currentVal = 0f;
                float.TryParse(lightFields[channel], out currentVal);
                float newVal = GUILayout.HorizontalSlider(currentVal, 0f, 1f, GUILayout.Width(150));
                string textVal = GUILayout.TextField(newVal.ToString("F2"), GUILayout.Width(40));
                if (float.TryParse(textVal, out float parsedVal))
                    newVal = parsedVal;

                GUILayout.BeginHorizontal(GUILayout.Width(60));
                string minusKey = compName + "." + channel + ".minus";
                string plusKey = compName + "." + channel + ".plus";

                bool pressingMinus = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                bool pressingPlus = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));

                _pressingMinus[minusKey] = pressingMinus;
                _pressingPlus[plusKey] = pressingPlus;

                GUILayout.EndHorizontal();

                if (Mathf.Abs(newVal - currentVal) > 0.0001f)
                {
                    lightFields[channel] = newVal.ToString("F2");
                    _sledParameterManager.SetFieldValue(compName, channel, newVal);
                    if (!_manualApply)
                        _sledParameterManager.ApplyParameters();
                }
                GUILayout.Space(5);
            }
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
                // skip Light color in this function
                if (compName == "Light" && (field.Key == "r" || field.Key == "g" || field.Key == "b" || field.Key == "a"))
                    continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label(field.Key + ":", _labelStyle, GUILayout.Width(150));
                Type fieldType = _sledParameterManager.GetFieldType(compName, field.Key);

                string fieldStringVal = field.Value;
                if (fieldType == typeof(float) || fieldType == typeof(int) || fieldType == typeof(double))
                {
                    double.TryParse(fieldStringVal, out double currentVal);
                    float sliderMin = _sledParameterManager.GetSliderMin(compName, field.Key);
                    float sliderMax = _sledParameterManager.GetSliderMax(compName, field.Key);

                    // We'll do a normal slider + text for immediate updates, but store the minus/plus as pressed booleans
                    float newValFloat = GUILayout.HorizontalSlider((float)currentVal, sliderMin, sliderMax, GUILayout.Width(150));
                    string newText = GUILayout.TextField(newValFloat.ToString("F2"), _textFieldStyle, GUILayout.Width(50));
                    if (float.TryParse(newText, out float parsedVal))
                        newValFloat = parsedVal;

                    double baseStep = (fieldType == typeof(int)) ? 1.0 : 0.01;
                    string minusKey = compName + "." + field.Key + ".minus";
                    string plusKey = compName + "." + field.Key + ".plus";

                    GUILayout.BeginHorizontal(GUILayout.Width(60));
                    bool pressingMinus = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                    bool pressingPlus = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
                    _pressingMinus[minusKey] = pressingMinus;
                    _pressingPlus[plusKey] = pressingPlus;
                    GUILayout.EndHorizontal();

                    double finalVal = (double)newValFloat;
                    if (finalVal.ToString() != fieldStringVal)
                    {
                        _fieldInputs[compName][field.Key] = finalVal.ToString();
                        _sledParameterManager.SetFieldValue(compName, field.Key, finalVal);
                        if (!_manualApply)
                            _sledParameterManager.ApplyParameters();
                    }
                }
                else if (fieldType == typeof(bool))
                {
                    bool currentBool = false;
                    bool.TryParse(fieldStringVal, out currentBool);
                    bool newBool = GUILayout.Toggle(currentBool, currentBool ? "On" : "Off", _toggleStyle, GUILayout.Width(80));
                    if (newBool != currentBool)
                    {
                        _fieldInputs[compName][field.Key] = newBool.ToString();
                        _sledParameterManager.SetFieldValue(compName, field.Key, newBool);
                        _sledParameterManager.ApplyParameters();
                    }
                }
                else if (fieldType == typeof(Vector2) || fieldType == typeof(Vector3) || fieldType == typeof(Vector4))
                {
                    GUILayout.Label("(Vector type handled separately)", _labelStyle, GUILayout.ExpandWidth(true));
                }
                else
                {
                    string newValue = GUILayout.TextField(fieldStringVal, _textFieldStyle, GUILayout.ExpandWidth(true));
                    if (newValue != fieldStringVal)
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

            if (compName == "Light")
            {
                DrawColorFieldsFromReflection(compName, fields);
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
                if (float.TryParse(input, out float f)) return f;
                return 0f;
            }
            if (targetType == typeof(int))
            {
                if (int.TryParse(input, out int i)) return i;
                return 0;
            }
            if (targetType == typeof(bool))
            {
                if (bool.TryParse(input, out bool b)) return b;
                return false;
            }
            if (targetType == typeof(double))
            {
                if (double.TryParse(input, out double d)) return d;
                return 0.0;
            }
            if (targetType == typeof(Vector2))
            {
                if (TryParseVector2(input, out Vector2 v2)) return v2;
                return Vector2.zero;
            }
            if (targetType == typeof(Vector3))
            {
                if (TryParseVector3(input, out Vector3 v3)) return v3;
                return Vector3.zero;
            }
            if (targetType == typeof(Vector4))
            {
                if (TryParseVector4(input, out Vector4 v4)) return v4;
                return Vector4.zero;
            }
            return input;
        }

        private void ResetValues()
        {
            speed = 10f;
            gravity = originalGravity;
            power = originalPower;
            lugHeight = originalLugHeight;
            trackLength = originalTrackLength;
            pitchFactor = originalPitchFactor;

            notdriverInvincible = true;
            test = false;
        }

        private bool TryParseVector2(string input, out Vector2 vector)
        {
            vector = Vector2.zero;
            if (string.IsNullOrEmpty(input)) return false;
            string[] parts = input.Split(',');
            if (parts.Length != 2) return false;
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y))
            {
                vector = new Vector2(x, y);
                return true;
            }
            return false;
        }

        private bool TryParseVector3(string input, out Vector3 vector)
        {
            vector = Vector3.zero;
            if (string.IsNullOrEmpty(input)) return false;
            string[] parts = input.Split(',');
            if (parts.Length != 3) return false;
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
            {
                vector = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private bool TryParseVector4(string input, out Vector4 vector)
        {
            vector = Vector4.zero;
            if (string.IsNullOrEmpty(input)) return false;
            string[] parts = input.Split(',');
            if (parts.Length != 4) return false;
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z) &&
                float.TryParse(parts[3], out float w))
            {
                vector = new Vector4(x, y, z, w);
                return true;
            }
            return false;
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
                _windowRect = new Rect(Screen.width - 160f, 10f, 150f, 30f);
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
                "  - If 'Manual Apply' is enabled, changes take effect only after pressing 'Apply'.\n" +
                "  - Use the window buttons to Minimize, Maximize, or Close the menu.\n" +
                "  - Footer buttons include toggles for Ragdoll, Tree Renderer, and Teleport.\n" +
                "  - In Advanced view, use the 'Tree View' toggle to collapse or expand components.\n" +
                "  - 'Switch View' toggles between Advanced and Simple tuner menus.\n",
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
        private void DrawColorFieldsFromReflection(string compName, Dictionary<string, string> fields)
        {
            string[] channels = { "r", "g", "b", "a" };
            foreach (string channel in channels)
            {
                if (!fields.ContainsKey(channel)) continue;

                float currentVal = 0f;
                float.TryParse(fields[channel], out currentVal);
                float newVal = GUILayout.HorizontalSlider(currentVal, 0f, 1f, GUILayout.Width(150));
                string textVal = GUILayout.TextField(newVal.ToString("F2"), GUILayout.Width(40));
                if (float.TryParse(textVal, out float parsedVal))
                    newVal = parsedVal;

                GUILayout.BeginHorizontal(GUILayout.Width(60));
                string minusKey = compName + "." + channel + ".minus";
                string plusKey = compName + "." + channel + ".plus";

                bool pressingMinus = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                bool pressingPlus = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));

                _pressingMinus[minusKey] = pressingMinus;
                _pressingPlus[plusKey] = pressingPlus;

                GUILayout.EndHorizontal();

                if (Mathf.Abs(newVal - currentVal) > 0.0001f)
                {
                    fields[channel] = newVal.ToString("F2");
                    _sledParameterManager.SetFieldValue(compName, channel, newVal);
                    if (!_manualApply)
                        _sledParameterManager.ApplyParameters();
                }
                GUILayout.Space(5);
            }
        }
    }
}
