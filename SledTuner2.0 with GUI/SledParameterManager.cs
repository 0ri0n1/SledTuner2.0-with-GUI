using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace SledTunerProject
{
    public class SledParameterManager
    {
        // Define which components and fields to inspect.
        public readonly Dictionary<string, string[]> ComponentsToInspect;

        private GameObject _snowmobileBody;
        private Rigidbody _rigidbody;
        private Light _light; // This is the Light component from "Snowmobile(Clone) -> Body -> Spot Light"

        private Dictionary<string, Dictionary<string, object>> _originalValues;
        private Dictionary<string, Dictionary<string, object>> _currentValues;

        public string JsonFolderPath { get; }
        public bool IsInitialized { get; private set; } = false;

        // Reflection cache: componentName -> (memberName -> MemberWrapper)
        private readonly Dictionary<string, Dictionary<string, MemberWrapper>> _reflectionCache =
            new Dictionary<string, Dictionary<string, MemberWrapper>>();

        // Helper struct to wrap FieldInfo/PropertyInfo.
        private struct MemberWrapper
        {
            public FieldInfo Field;
            public PropertyInfo Property;
            public bool IsValid => Field != null || Property != null;
            public bool CanRead => Field != null || (Property != null && Property.CanRead);
            public bool CanWrite => Field != null || (Property != null && Property.CanWrite);
            public Type MemberType => Field != null ? Field.FieldType : Property?.PropertyType;
        }

        // Parameter metadata dictionary: componentName -> (fieldName -> ParameterMetadata)
        private Dictionary<string, Dictionary<string, ParameterMetadata>> _parameterMetadata;

        public SledParameterManager()
        {
            ComponentsToInspect = new Dictionary<string, string[]>
            {
                ["SnowmobileController"] = new string[]
                {
                    "leanSteerFactorSoft", "leanSteerFactorTrail", "throttleExponent", "drowningDepth", "drowningTime",
                    "isEngineOn", "isStuck", "canRespawn", "hasDrowned", "rpmSensitivity", "rpmSensitivityDown",
                    "minThrottleOnClutchEngagement", "clutchRpmMin", "clutchRpmMax", "isHeadlightOn",
                    "wheelieThreshold", "driverTorgueFactorRoll", "driverTorgueFactorPitch", "snowmobileTorgueFactor"
                },
                ["SnowmobileControllerBase"] = new string[]
                {
                    "skisMaxAngle", "driverZCenter", "enableVerticalWeightTransfer", "trailLeanDistance", "switchbackTransitionTime"
                },
                ["MeshInterpretter"] = new string[]
                {
                    "power", "powerEfficiency", "breakForce", "frictionForce", "trackMass", "coefficientOfFriction",
                    "snowPushForceFactor", "snowPushForceNormalizedFactor", "snowSupportForceFactor", "maxSupportPressure",
                    "lugHeight", "snowOutTrackWidth", "pitchFactor", "drivetrainMaxSpeed1", "drivetrainMaxSpeed2"
                },
                ["SnowParameters"] = new string[]
                {
                    "snowNormalConstantFactor", "snowNormalDepthFactor", "snowFrictionFactor"
                },
                ["SuspensionController"] = new string[]
                {
                    "suspensionSubSteps", "antiRollBarFactor", "skiAutoTurn", "trackRigidityFront", "trackRigidityRear"
                },
                ["Stabilizer"] = new string[]
                {
                    "trackSpeedGyroMultiplier", "idleGyro"
                },
                ["RagDollCollisionController"] = new string[]
                {
                    "ragdollTreshold", "ragdollTresholdDownFactor"
                },
                ["Rigidbody"] = new string[]
                {
                    "mass", "drag", "angularDrag", "useGravity", "maxAngularVelocity"
                },
                // For Light, we define custom fields for each channel.
                ["Light"] = new string[] { "r", "g", "b", "a" }
            };

            _originalValues = new Dictionary<string, Dictionary<string, object>>();
            _currentValues = new Dictionary<string, Dictionary<string, object>>();

            string basePath = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "SledTuner");
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);
            JsonFolderPath = basePath;

            InitializeParameterMetadata();
        }

        private void InitializeParameterMetadata()
        {
            _parameterMetadata = new Dictionary<string, Dictionary<string, ParameterMetadata>>();

            _parameterMetadata["SnowmobileController"] = new Dictionary<string, ParameterMetadata>
            {
                ["leanSteerFactorSoft"] = new ParameterMetadata("Steering Sensitivity (Soft)", "Sensitivity on soft terrain", 0f, 5f),
                // ... add other parameters as needed ...
                ["snowmobileTorgueFactor"] = new ParameterMetadata("Snowmobile Torque Factor", "Torque factor for the snowmobile", 0f, 10f)
            };

            _parameterMetadata["SnowmobileControllerBase"] = new Dictionary<string, ParameterMetadata>
            {
                ["skisMaxAngle"] = new ParameterMetadata("Skis Max Angle", "Maximum angle of the skis", 0f, 90f),
                ["driverZCenter"] = new ParameterMetadata("Driver Z Center", "Vertical center offset for driver", -1f, 1f),
                ["enableVerticalWeightTransfer"] = new ParameterMetadata("Vertical Weight Transfer", "Enable vertical weight transfer", 0f, 1f, ControlType.Toggle),
                ["trailLeanDistance"] = new ParameterMetadata("Trail Lean Distance", "Distance for trail leaning", 0f, 10f),
                ["switchbackTransitionTime"] = new ParameterMetadata("Switchback Transition Time", "Time to transition during a switchback", 0.1f, 1.0f)
            };

            _parameterMetadata["MeshInterpretter"] = new Dictionary<string, ParameterMetadata>
            {
                ["power"] = new ParameterMetadata("Power", "Engine power", 0f, 1000000f),
                // ... add other parameters ...
                ["drivetrainMaxSpeed2"] = new ParameterMetadata("Drivetrain Max Speed 2", "Maximum speed for drivetrain configuration 2", 0f, 500f)
            };

            _parameterMetadata["SnowParameters"] = new Dictionary<string, ParameterMetadata>
            {
                ["snowNormalConstantFactor"] = new ParameterMetadata("Snow Normal Constant Factor", "Constant factor for snow normals", 0f, 10f),
                ["snowNormalDepthFactor"] = new ParameterMetadata("Snow Normal Depth Factor", "Depth factor for snow normals", 0f, 10f),
                ["snowFrictionFactor"] = new ParameterMetadata("Snow Friction Factor", "Friction factor for snow", 0f, 1f)
            };

            _parameterMetadata["SuspensionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["suspensionSubSteps"] = new ParameterMetadata("Suspension Sub-Steps", "Number of sub-steps for suspension simulation", 1f, 500f),
                ["antiRollBarFactor"] = new ParameterMetadata("Anti-Roll Bar Factor", "Factor for anti-roll bar effect", 0f, 10000f),
                ["skiAutoTurn"] = new ParameterMetadata("Ski Auto Turn", "Automatically turn skis", 0f, 1f, ControlType.Toggle),
                ["trackRigidityFront"] = new ParameterMetadata("Track Rigidity (Front)", "Rigidity of the front track", 0f, 100f),
                ["trackRigidityRear"] = new ParameterMetadata("Track Rigidity (Rear)", "Rigidity of the rear track", 0f, 100f)
            };

            _parameterMetadata["Stabilizer"] = new Dictionary<string, ParameterMetadata>
            {
                ["trackSpeedGyroMultiplier"] = new ParameterMetadata("Track Speed Gyro Multiplier", "Multiplier for gyro effect based on track speed", 0f, 100f),
                ["idleGyro"] = new ParameterMetadata("Idle Gyro", "Gyro value when idle", 0f, 1000f)
            };

            _parameterMetadata["RagDollCollisionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["ragdollTreshold"] = new ParameterMetadata("Ragdoll Threshold", "Threshold for ragdoll activation", 0f, 10f),
                ["ragdollTresholdDownFactor"] = new ParameterMetadata("Ragdoll Threshold Down Factor", "Down factor for ragdoll threshold", 0f, 10f)
            };

            _parameterMetadata["Rigidbody"] = new Dictionary<string, ParameterMetadata>
            {
                ["mass"] = new ParameterMetadata("Mass", "Mass of the sled", 0f, 1000f),
                ["drag"] = new ParameterMetadata("Drag", "Linear drag", 0f, 10f),
                ["angularDrag"] = new ParameterMetadata("Angular Drag", "Angular drag", 0f, 10f),
                ["useGravity"] = new ParameterMetadata("Use Gravity", "Toggle gravity usage", 0f, 1f, ControlType.Toggle),
                ["maxAngularVelocity"] = new ParameterMetadata("Max Angular Velocity", "Maximum angular velocity", 0f, 100f)
            };

            _parameterMetadata["Light"] = new Dictionary<string, ParameterMetadata>
            {
                ["r"] = new ParameterMetadata("Light Red", "Red channel of Light color", 0f, 1f),
                ["g"] = new ParameterMetadata("Light Green", "Green channel of Light color", 0f, 1f),
                ["b"] = new ParameterMetadata("Light Blue", "Blue channel of Light color", 0f, 1f),
                ["a"] = new ParameterMetadata("Light Alpha", "Alpha channel of Light color", 0f, 1f)
            };
        }

        // === COMPONENT INITIALIZATION AND REFLECTION ===

        public void InitializeComponents()
        {
            IsInitialized = false;
            MelonLogger.Msg("[SledTuner] Initializing Sled components...");

            GameObject snowmobile = GameObject.Find("Snowmobile(Clone)");
            if (snowmobile == null)
            {
                MelonLogger.Warning("[SledTuner] 'Snowmobile(Clone)' not found in the scene.");
                return;
            }

            // STRICTLY require the "Body" child to be present.
            Transform bodyT = snowmobile.transform.Find("Body");
            if (bodyT == null)
            {
                MelonLogger.Warning("[SledTuner] 'Body' child not found under Snowmobile(Clone). Aborting initialization.");
                return;
            }
            _snowmobileBody = bodyT.gameObject;
            MelonLogger.Msg("[SledTuner] Found snowmobile Body GameObject.");

            _rigidbody = _snowmobileBody.GetComponent<Rigidbody>();
            if (_rigidbody != null)
                MelonLogger.Msg("[SledTuner] Rigidbody found on Body.");
            else
                MelonLogger.Warning("[SledTuner] No Rigidbody found on Body.");

            // Look for the "Spot Light" under the Body.
            Transform spotLightT = _snowmobileBody.transform.Find("Spot Light");
            if (spotLightT != null)
            {
                _light = spotLightT.GetComponent<Light>();
                if (_light != null)
                    MelonLogger.Msg("[SledTuner] Successfully retrieved Light component from Spot Light.");
                else
                    MelonLogger.Warning("[SledTuner] Spot Light found but no Light component attached!");
            }
            else
            {
                MelonLogger.Warning("[SledTuner] 'Spot Light' not found as a child of Body.");
            }

            BuildReflectionCache();

            _originalValues = InspectSledComponents();
            _currentValues = InspectSledComponents();

            MelonLogger.Msg($"[SledTuner] Current parameter components: {string.Join(", ", _currentValues.Keys)}");

            IsInitialized = true;
            MelonLogger.Msg("[SledTuner] Sled components initialized successfully.");
        }

        private void BuildReflectionCache()
        {
            MelonLogger.Msg("[SledTuner] Building reflection cache...");
            _reflectionCache.Clear();

            foreach (var kvp in ComponentsToInspect)
            {
                string compName = kvp.Key;
                string[] fields = kvp.Value;
                Component comp = GetComponentByName(compName);
                var memberLookup = new Dictionary<string, MemberWrapper>();

                // For "Light", we use custom handling.
                if (compName == "Light")
                {
                    foreach (string fName in fields)
                    {
                        memberLookup[fName] = new MemberWrapper(); // empty wrapper
                    }
                    _reflectionCache[compName] = memberLookup;
                    MelonLogger.Msg("[SledTuner] Custom handling for Light in reflection cache.");
                    continue;
                }

                if (comp != null)
                {
                    Type compType = comp.GetType();
                    foreach (string fName in fields)
                    {
                        var wrapper = new MemberWrapper();
                        FieldInfo fi = compType.GetField(fName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (fi != null)
                        {
                            wrapper.Field = fi;
                        }
                        else
                        {
                            PropertyInfo pi = compType.GetProperty(fName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (pi != null)
                                wrapper.Property = pi;
                        }
                        memberLookup[fName] = wrapper;
                    }
                    MelonLogger.Msg($"[SledTuner] Component '{compName}': Found {memberLookup.Count} fields.");
                }
                else
                {
                    MelonLogger.Warning($"[SledTuner] Component '{compName}' not found during reflection; adding empty entries.");
                    foreach (string fName in fields)
                        memberLookup[fName] = new MemberWrapper();
                }
                _reflectionCache[compName] = memberLookup;
            }
            MelonLogger.Msg("[SledTuner] Reflection cache build complete.");
        }

        // === COMPONENT INSPECTION AND PARAMETER APPLICATION ===

        public object GetFieldValue(string componentName, string fieldName)
        {
            if (componentName == "Light" && _light != null)
            {
                Color c = _light.color;
                switch (fieldName)
                {
                    case "r": return c.r;
                    case "g": return c.g;
                    case "b": return c.b;
                    case "a": return c.a;
                }
                return null;
            }

            if (_currentValues.TryGetValue(componentName, out var fields) &&
                fields.TryGetValue(fieldName, out var value))
            {
                return value;
            }
            return null;
        }

        public Type GetFieldType(string componentName, string fieldName)
        {
            if (componentName == "Light")
                return typeof(float);

            if (_reflectionCache.TryGetValue(componentName, out var members) &&
                members.TryGetValue(fieldName, out var wrapper))
            {
                return wrapper.MemberType;
            }
            return null;
        }

        public void SetFieldValue(string componentName, string fieldName, object value)
        {
            if (!_currentValues.ContainsKey(componentName))
                _currentValues[componentName] = new Dictionary<string, object>();
            _currentValues[componentName][fieldName] = value;
        }

        public float GetSliderMin(string componentName, string fieldName)
        {
            if (_parameterMetadata.TryGetValue(componentName, out var fieldDict) &&
                fieldDict.TryGetValue(fieldName, out var meta))
            {
                return meta.MinValue;
            }
            return -100f;
        }

        public float GetSliderMax(string componentName, string fieldName)
        {
            if (_parameterMetadata.TryGetValue(componentName, out var fieldDict) &&
                fieldDict.TryGetValue(fieldName, out var meta))
            {
                return meta.MaxValue;
            }
            return 100f;
        }

        public void ApplyParameters()
        {
            foreach (var compKvp in _currentValues)
            {
                string compName = compKvp.Key;
                // Custom handling for Light: combine channel values into a Color.
                if (compName == "Light" && _light != null)
                {
                    float r = Convert.ToSingle(_currentValues["Light"]["r"]);
                    float g = Convert.ToSingle(_currentValues["Light"]["g"]);
                    float b = Convert.ToSingle(_currentValues["Light"]["b"]);
                    float a = Convert.ToSingle(_currentValues["Light"]["a"]);
                    _light.color = new Color(r, g, b, a);
                    continue;
                }
                foreach (var fieldKvp in compKvp.Value)
                {
                    ApplyField(compName, fieldKvp.Key, fieldKvp.Value);
                }
            }
            MelonLogger.Msg("[SledTuner] Applied parameters.");
        }

        public void RevertParameters()
        {
            ResetParameters();
            MelonLogger.Msg("[SledTuner] Reverted parameters.");
        }

        public void ResetParameters()
        {
            if (_originalValues == null)
                return;
            foreach (var compKvp in _originalValues)
            {
                string compName = compKvp.Key;
                foreach (var fieldKvp in compKvp.Value)
                {
                    ApplyField(compName, fieldKvp.Key, fieldKvp.Value);
                }
            }
            MelonLogger.Msg("[SledTuner] Reset to original parameters.");
        }

        public Dictionary<string, Dictionary<string, object>> GetCurrentParameters() => _currentValues;

        public void SetParameters(Dictionary<string, Dictionary<string, object>> data)
        {
            _currentValues = data;
            ApplyParameters();
        }

        public string GetSledName()
        {
            if (_snowmobileBody == null) return null;
            Component c = _snowmobileBody.GetComponent("SnowmobileController");
            if (c == null)
            {
                MelonLogger.Warning("[SledTuner] SnowmobileController not found on the body.");
                return null;
            }
            PropertyInfo prop = c.GetType().GetProperty("GKMNAIKNNMJ", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || !prop.CanRead)
            {
                MelonLogger.Warning("[SledTuner] GKMNAIKNNMJ property missing or unreadable.");
                return null;
            }
            try
            {
                object val = prop.GetValue(c, null);
                if (val == null)
                    return null;
                string text = val.ToString();
                const string suffix = " (VehicleScriptableObject)";
                if (text.EndsWith(suffix))
                    return text.Substring(0, text.Length - suffix.Length);
                return text;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[SledTuner] Error reading sled name: " + ex.Message);
                return null;
            }
        }

        private Dictionary<string, Dictionary<string, object>> InspectSledComponents()
        {
            MelonLogger.Msg("[SledTuner] Inspecting sled components...");
            var dictionary = new Dictionary<string, Dictionary<string, object>>();
            foreach (var kvp in ComponentsToInspect)
            {
                string compName = kvp.Key;
                string[] fields = kvp.Value;
                if (compName == "Light")
                {
                    if (_light != null)
                    {
                        var lightDict = new Dictionary<string, object>
                        {
                            ["r"] = _light.color.r,
                            ["g"] = _light.color.g,
                            ["b"] = _light.color.b,
                            ["a"] = _light.color.a
                        };
                        dictionary[compName] = lightDict;
                    }
                    continue;
                }
                Component comp = GetComponentByName(compName);
                if (comp == null)
                {
                    MelonLogger.Warning($"[SledTuner] Component '{compName}' not found during inspection; skipping.");
                    continue;
                }
                var compDict = new Dictionary<string, object>();
                if (!_reflectionCache.ContainsKey(compName))
                {
                    foreach (string fName in fields)
                        compDict[fName] = $"(No reflection cache for {fName})";
                    dictionary[compName] = compDict;
                    continue;
                }
                foreach (string fName in fields)
                {
                    compDict[fName] = TryReadCachedMember(comp, compName, fName);
                }
                dictionary[compName] = compDict;
            }
            MelonLogger.Msg("[SledTuner] Component inspection complete.");
            return dictionary;
        }

        private object TryReadCachedMember(Component comp, string compName, string fieldName)
        {
            if (!_reflectionCache[compName].TryGetValue(fieldName, out MemberWrapper wrapper) || !wrapper.IsValid)
                return $"(Not found: {fieldName})";
            if (!wrapper.CanRead)
                return "(Not readable)";
            try
            {
                object raw = wrapper.Field != null ? wrapper.Field.GetValue(comp) : wrapper.Property.GetValue(comp, null);
                return ConvertOrSkip(raw, wrapper.MemberType);
            }
            catch (Exception ex)
            {
                return $"Error reading '{fieldName}': {ex.Message}";
            }
        }

        private void ApplyField(string compName, string fieldName, object value)
        {
            Component comp = GetComponentByName(compName);
            if (comp == null)
                return;
            if (!_reflectionCache.TryGetValue(compName, out var memberDict) ||
                !memberDict.TryGetValue(fieldName, out var wrapper) || !wrapper.IsValid)
                return;
            if (!wrapper.CanWrite)
            {
                MelonLogger.Warning($"[SledTuner] {fieldName} in {compName} is read-only.");
                return;
            }
            try
            {
                object converted = ConvertValue(value, wrapper.MemberType);
                if (wrapper.Field != null)
                    wrapper.Field.SetValue(comp, converted);
                else
                    wrapper.Property.SetValue(comp, converted, null);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SledTuner] Error setting {compName}.{fieldName}: {ex.Message}");
            }
        }

        private object ConvertValue(object raw, Type targetType)
        {
            if (raw == null || targetType == null)
                return null;
            try
            {
                if (targetType == typeof(float) && raw is double dVal)
                    return (float)dVal;
                if (targetType == typeof(int) && raw is long lVal)
                    return (int)lVal;
                if (targetType == typeof(bool) && raw is bool bVal)
                    return bVal;
                if (targetType.IsInstanceOfType(raw))
                    return raw;
                return Convert.ChangeType(raw, targetType);
            }
            catch
            {
                return raw;
            }
        }

        private object ConvertOrSkip(object raw, Type fieldType)
        {
            if (raw == null)
                return null;
            if (fieldType != null && typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                return "(Skipped UnityEngine.Object)";
            if (fieldType != null &&
                !fieldType.IsPrimitive &&
                fieldType != typeof(string) &&
                fieldType != typeof(decimal))
                return "(Skipped complex type)";
            return raw;
        }

        private Component GetComponentByName(string compName)
        {
            if (_snowmobileBody == null && compName != "RagDollCollisionController")
                return null;
            if (compName == "Rigidbody" && _rigidbody != null)
                return _rigidbody;
            if (compName == "RagDollCollisionController")
            {
                GameObject driverGO = GameObject.Find("IK Player (Drivers)");
                if (driverGO == null)
                {
                    MelonLogger.Warning("[SledTuner] 'IK Player (Drivers)' not found.");
                    return null;
                }
                return driverGO.GetComponent("RagDollCollisionController");
            }
            return _snowmobileBody.GetComponent(compName);
        }
    }
}
