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
        // Public properties and fields
        public readonly Dictionary<string, string[]> ComponentsToInspect;
        public string JsonFolderPath { get; }
        public bool IsInitialized { get; private set; } = false;

        // References to game objects and components
        private GameObject _snowmobileBody;
        private Rigidbody _rigidbody;
        private Light _light;

        // Dictionaries to hold parameter values
        private Dictionary<string, Dictionary<string, object>> _originalValues;
        private Dictionary<string, Dictionary<string, object>> _currentValues;

        // Reflection cache
        private readonly Dictionary<string, Dictionary<string, MemberWrapper>> _reflectionCache = new Dictionary<string, Dictionary<string, MemberWrapper>>();

        // Parameter metadata
        private Dictionary<string, Dictionary<string, ParameterMetadata>> _parameterMetadata;

        // Helper structure for caching fields/properties
        private struct MemberWrapper
        {
            public FieldInfo Field;
            public PropertyInfo Property;
            public bool IsValid => Field != null || Property != null;
            public bool CanRead => Field != null || (Property != null && Property.CanRead);
            public bool CanWrite => Field != null || (Property != null && Property.CanWrite);
            public Type MemberType => Field != null ? Field.FieldType : Property?.PropertyType;
        }

        // Constructor
        public SledParameterManager()
        {
            // Define the components and field names to inspect:
            //   - "horizontalWeightTransferMode" and driverMaxDistance* removed from SnowmobileControllerBase
            //   - "hard"/"soft" removed from Shock, replaced with "compression","mass","maxCompression","velocity"
            //   - "ragdollThreshold"/"ragdollThresholdDownFactor" in RagDollCollisionController
            //   - RBC "constraints","interpolation","collisionDetectionMode" not in user's snippet
            ComponentsToInspect = new Dictionary<string, string[]>
            {
                ["SnowmobileController"] = new string[]
                {
                    "leanSteerFactorSoft", "leanSteerFactorTrail", "throttleExponent", "drowningDepth", "drowningTime",
                    "isEngineOn", "isStuck", "canRespawn", "hasDrowned", "rpmSensitivity", "rpmSensitivityDown",
                    "minThrottleOnClutchEngagement", "clutchRpmMin", "clutchRpmMax", "isHeadlightOn",
                    "wheelieThreshold", "driverTorgueFactorRoll", "driverTorgueFactorPitch",
                    "snowmobileTorgueFactor", "isWheeling"
                },
                ["SnowmobileControllerBase"] = new string[]
                {
                    "skisMaxAngle", "driverZCenter", "enableVerticalWeightTransfer",
                    "trailLeanDistance", "switchbackTransitionTime",
                    "toeAngle", "hopOverPreJump", "switchBackLeanDistance"
                },
                ["MeshInterpretter"] = new string[]
                {
                    "power", "powerEfficiency", "breakForce", "frictionForce", "trackMass", "coefficientOfFriction",
                    "snowPushForceFactor", "snowPushForceNormalizedFactor", "snowSupportForceFactor",
                    "maxSupportPressure", "lugHeight", "snowOutTrackWidth", "pitchFactor",
                    "drivetrainMinSpeed", "drivetrainMaxSpeed1", "drivetrainMaxSpeed2"
                },
                ["SnowParameters"] = new string[]
                {
                    "snowNormalConstantFactor", "snowNormalDepthFactor",
                    "snowFrictionFactor", "snowNormalSpeedFactor"
                },
                ["SuspensionController"] = new string[]
                {
                    "suspensionSubSteps", "antiRollBarFactor", "skiAutoTurn",
                    "trackRigidityFront", "trackRigidityRear", "reduceSuspensionForceByTilt"
                },
                ["Stabilizer"] = new string[]
                {
                    "trackSpeedGyroMultiplier", "idleGyro", "trackSpeedDamping"
                },
                ["RagDollCollisionController"] = new string[]
                {
                    "ragdollThreshold",
                    "ragdollThresholdDownFactor"
                },
                ["Rigidbody"] = new string[]
                {
                    "mass", "drag", "angularDrag", "useGravity", "maxAngularVelocity"
                },
                ["Light"] = new string[] { "r", "g", "b", "a" },
                ["Shock"] = new string[]
                {
                    "compression", // double typed in actual script
                    "mass",
                    "maxCompression",
                    "velocity"
                }
            };

            _originalValues = new Dictionary<string, Dictionary<string, object>>();
            _currentValues = new Dictionary<string, Dictionary<string, object>>();

            string basePath = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "SledTuner");
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);
            JsonFolderPath = basePath;

            InitializeParameterMetadata();
        }

        // Initialize metadata for each component's fields.
        private void InitializeParameterMetadata()
        {
            _parameterMetadata = new Dictionary<string, Dictionary<string, ParameterMetadata>>();
            // For brevity, assume your existing parameter metadata blocks remain:
            // e.g. SnowmobileController, SnowmobileControllerBase, MeshInterpretter, etc.

            // Reaffirm ragdoll metadata:
            _parameterMetadata["RagDollCollisionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["ragdollThreshold"] = new ParameterMetadata("Ragdoll Threshold", "Threshold for ragdoll activation", 0f, 1000f),
                ["ragdollThresholdDownFactor"] = new ParameterMetadata("Ragdoll Threshold Down Factor", "Down factor for ragdoll threshold", 0f, 10f)
            };

            // Add additional metadata for other components as needed.
        }

        public void InitializeComponents()
        {
            IsInitialized = false;
            MelonLogger.Msg("[SledTuner] Initializing sled components...");

            GameObject snowmobile = GameObject.Find("Snowmobile(Clone)");
            if (snowmobile == null)
            {
                MelonLogger.Warning("[SledTuner] 'Snowmobile(Clone)' not found.");
                return;
            }
            Transform bodyTransform = snowmobile.transform.Find("Body");
            if (bodyTransform == null)
            {
                MelonLogger.Warning("[SledTuner] 'Body' not found under 'Snowmobile(Clone)'.");
                return;
            }
            _snowmobileBody = bodyTransform.gameObject;
            _rigidbody = _snowmobileBody.GetComponent<Rigidbody>();

            Transform spotLightTransform = _snowmobileBody.transform.Find("Spot Light");
            if (spotLightTransform != null)
                _light = spotLightTransform.GetComponent<Light>();

            BuildReflectionCache();
            _originalValues = InspectSledComponents();
            _currentValues = InspectSledComponents();

            if (_originalValues != null)
            {
                IsInitialized = true;
                MelonLogger.Msg("[SledTuner] Sled components initialized successfully.");
            }
            else
            {
                MelonLogger.Warning("[SledTuner] Sled component inspection failed.");
            }
        }

        public void ApplyParameters()
        {
            foreach (var compEntry in _currentValues)
            {
                string compName = compEntry.Key;
                foreach (var fieldEntry in compEntry.Value)
                {
                    ApplyField(compName, fieldEntry.Key, fieldEntry.Value);
                }
            }
            MelonLogger.Msg("[SledTuner] Applied parameters.");
        }

        public void RevertParameters()
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
            MelonLogger.Msg("[SledTuner] Reverted parameters to original values.");
        }

        public Dictionary<string, Dictionary<string, object>> GetCurrentParameters()
        {
            return _currentValues;
        }

        public void SetParameters(Dictionary<string, Dictionary<string, object>> data)
        {
            _currentValues = data;
            ApplyParameters();
        }

        public void ResetParameters()
        {
            RevertParameters();
        }

        public string GetSledName()
        {
            if (_snowmobileBody == null)
                return null;

            Component controller = _snowmobileBody.GetComponent("SnowmobileController");
            if (controller == null)
            {
                MelonLogger.Warning("[SledTuner] SnowmobileController not found on Body.");
                return null;
            }

            PropertyInfo prop = controller.GetType().GetProperty("GKMNAIKNNMJ", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || !prop.CanRead)
            {
                MelonLogger.Warning("[SledTuner] Property GKMNAIKNNMJ not found or unreadable.");
                return null;
            }

            try
            {
                object value = prop.GetValue(controller, null);
                if (value == null)
                    return null;

                string name = value.ToString();
                const string suffix = " (VehicleScriptableObject)";
                if (name.EndsWith(suffix))
                    return name.Substring(0, name.Length - suffix.Length);
                return name;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[SledTuner] Error reading sled name: " + ex.Message);
                return null;
            }
        }

        public object GetFieldValue(string componentName, string fieldName)
        {
            if (_currentValues.TryGetValue(componentName, out var dict) &&
                dict.TryGetValue(fieldName, out var value))
            {
                return value;
            }
            return null;
        }

        public Type GetFieldType(string componentName, string fieldName)
        {
            if (_reflectionCache.TryGetValue(componentName, out var members) &&
                members.TryGetValue(fieldName, out var wrapper))
            {
                return wrapper.MemberType;
            }
            return null;
        }

        public float GetSliderMin(string componentName, string fieldName)
        {
            if (_parameterMetadata.TryGetValue(componentName, out var metaDict) &&
                metaDict.TryGetValue(fieldName, out var meta))
            {
                return meta.MinValue;
            }
            return -100f;
        }

        public float GetSliderMax(string componentName, string fieldName)
        {
            if (_parameterMetadata.TryGetValue(componentName, out var metaDict) &&
                metaDict.TryGetValue(fieldName, out var meta))
            {
                return meta.MaxValue;
            }
            return 100f;
        }

        public void SetFieldValue(string componentName, string fieldName, object value)
        {
            if (!_currentValues.ContainsKey(componentName))
                _currentValues[componentName] = new Dictionary<string, object>();
            _currentValues[componentName][fieldName] = value;
        }

        // === PRIVATE HELPER METHODS ===

        private void BuildReflectionCache()
        {
            _reflectionCache.Clear();

            foreach (var kvp in ComponentsToInspect)
            {
                string compName = kvp.Key;
                string[] fields = kvp.Value;
                Component comp = GetComponentByName(compName);
                Dictionary<string, MemberWrapper> memberDict = new Dictionary<string, MemberWrapper>();

                if (comp != null)
                {
                    Type type = comp.GetType();
                    foreach (string field in fields)
                    {
                        MemberWrapper wrapper = new MemberWrapper();

                        // Skip Light color channels (handled separately)
                        if (compName == "Light" && (field == "r" || field == "g" || field == "b" || field == "a"))
                        {
                            // No reflection needed for these.
                        }
                        else
                        {
                            // Attempt standard reflection first
                            FieldInfo fi = type.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (fi != null)
                            {
                                wrapper.Field = fi;
                            }
                            else
                            {
                                PropertyInfo pi = type.GetProperty(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (pi != null)
                                    wrapper.Property = pi;
                            }

                            // If not found and we are dealing with RagDollCollisionController, check alternate spellings
                            if (!wrapper.IsValid && compName == "RagDollCollisionController")
                            {
                                if (field == "ragdollThreshold" || field == "ragdollThresholdDownFactor")
                                {
                                    // Alternate spellings:
                                    string altName = (field == "ragdollThreshold") ? "ragdollTreshold" : "ragdollTresholdDownFactor";

                                    FieldInfo altFi = type.GetField(altName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (altFi != null)
                                    {
                                        MelonLogger.Warning($"[SledTuner] Found alternate field '{altName}' for {compName}; using it for '{field}'.");
                                        wrapper.Field = altFi;
                                    }
                                    else
                                    {
                                        PropertyInfo altPi = type.GetProperty(altName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (altPi != null)
                                        {
                                            MelonLogger.Warning($"[SledTuner] Found alternate property '{altName}' for {compName}; using it for '{field}'.");
                                            wrapper.Property = altPi;
                                        }
                                    }
                                }
                            }
                        }

                        memberDict[field] = wrapper;
                    }
                }
                else
                {
                    // If the component is not found, store empty wrappers for each field
                    foreach (string field in fields)
                    {
                        memberDict[field] = new MemberWrapper();
                    }
                }
                _reflectionCache[compName] = memberDict;
            }
        }

        private Dictionary<string, Dictionary<string, object>> InspectSledComponents()
        {
            var result = new Dictionary<string, Dictionary<string, object>>();

            foreach (var kvp in ComponentsToInspect)
            {
                string compName = kvp.Key;
                string[] fields = kvp.Value;
                Component comp = GetComponentByName(compName);

                if (comp == null)
                {
                    MelonLogger.Warning($"[SledTuner] Component '{compName}' not found during inspection; skipping.");
                    continue;
                }

                Dictionary<string, object> compValues = new Dictionary<string, object>();

                if (!_reflectionCache.ContainsKey(compName))
                {
                    foreach (string field in fields)
                    {
                        compValues[field] = $"(No reflection cache for {field})";
                    }
                    result[compName] = compValues;
                    continue;
                }

                // Read each field via TryReadMember
                foreach (string field in fields)
                {
                    compValues[field] = TryReadMember(comp, compName, field);
                }

                result[compName] = compValues;
            }

            MelonLogger.Msg("[SledTuner] Component inspection complete.");
            return result;
        }

        private object TryReadMember(Component comp, string compName, string fieldName)
        {
            // Handle Light color channels separately
            if (compName == "Light" && (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
            {
                var lightComp = (Light)comp;
                Color c = lightComp.color;
                switch (fieldName)
                {
                    case "r": return c.r;
                    case "g": return c.g;
                    case "b": return c.b;
                    case "a": return c.a;
                    default: return "(Not found: " + fieldName + ")";
                }
            }

            if (!_reflectionCache[compName].TryGetValue(fieldName, out MemberWrapper wrapper) || !wrapper.IsValid)
                return $"(Not found: {fieldName})";

            if (!wrapper.CanRead)
                return "(Not readable)";

            try
            {
                object raw = (wrapper.Field != null)
                    ? wrapper.Field.GetValue(comp)
                    : wrapper.Property.GetValue(comp, null);

                return ConvertOrSkip(raw, wrapper.MemberType);
            }
            catch (Exception ex)
            {
                return $"Error reading '{fieldName}': {ex.Message}";
            }
        }

        private object ConvertOrSkip(object raw, Type fieldType)
        {
            if (raw == null)
                return null;

            // Skip UnityEngine.Object types or complex types
            if (fieldType != null && typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                return "(Skipped UnityEngine.Object)";

            if (fieldType != null && fieldType.IsEnum)
                return raw;

            if (fieldType != null && !fieldType.IsPrimitive && fieldType != typeof(string) && fieldType != typeof(decimal) && !fieldType.IsEnum)
            {
                return "(Skipped complex type)";
            }

            return raw;
        }

        private Component GetComponentByName(string compName)
        {
            if (_snowmobileBody == null)
                return null;

            if (compName == "Rigidbody")
            {
                return _snowmobileBody.GetComponent<Rigidbody>();
            }
            else if (compName == "Light")
            {
                Transform t = _snowmobileBody.transform.Find("Spot Light");
                if (t != null)
                    return t.GetComponent<Light>();
                return null;
            }
            else if (compName == "RagDollCollisionController" || compName == "RagDollManager")
            {
                Transform ikPlayer = _snowmobileBody.transform.Find("IK Player (Drivers)");
                if (ikPlayer == null)
                    return null;
                return ikPlayer.GetComponent(compName);
            }
            else if (compName == "Shock")
            {
                Transform frontSuspension = _snowmobileBody.transform.Find("Front Suspension");
                if (frontSuspension != null)
                {
                    Component shockComp = frontSuspension.GetComponent(compName);
                    if (shockComp != null)
                        return shockComp;
                }
                Transform rearSuspension = _snowmobileBody.transform.Find("Rear Suspension");
                if (rearSuspension != null)
                {
                    Component shockComp = rearSuspension.GetComponent(compName);
                    if (shockComp != null)
                        return shockComp;
                }
                return null;
            }
            else
            {
                return _snowmobileBody.GetComponent(compName);
            }
        }

        private void ApplyField(string compName, string fieldName, object value)
        {
            Component comp = GetComponentByName(compName);
            if (comp == null)
                return;

            // Special handling for Light color channels
            if (compName == "Light" && (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
            {
                var lightComp = (Light)comp;
                Color c = lightComp.color;

                float floatVal = 0f;
                if (value is double dVal)
                    floatVal = (float)dVal;
                else if (value is float fVal)
                    floatVal = fVal;
                else if (value is int iVal)
                    floatVal = iVal;
                else
                {
                    try { floatVal = Convert.ToSingle(value); }
                    catch { }
                }

                switch (fieldName)
                {
                    case "r":
                        c.r = Mathf.Clamp01(floatVal);
                        break;
                    case "g":
                        c.g = Mathf.Clamp01(floatVal);
                        break;
                    case "b":
                        c.b = Mathf.Clamp01(floatVal);
                        break;
                    case "a":
                        c.a = Mathf.Clamp01(floatVal);
                        break;
                }

                lightComp.color = c;
                return;
            }

            if (!_reflectionCache.TryGetValue(compName, out var memberDict) ||
                !memberDict.TryGetValue(fieldName, out var wrapper) ||
                !wrapper.IsValid)
            {
                return;
            }

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
    }
}
