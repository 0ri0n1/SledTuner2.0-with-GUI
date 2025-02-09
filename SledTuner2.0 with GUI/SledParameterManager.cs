using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using HarmonyLib; // For AccessTools

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

        // Cached accessor dictionary (replacing the reflection-based MemberWrapper)
        // Keyed by component name then by field/property name.
        private readonly Dictionary<string, Dictionary<string, FieldAccessor>> _accessorCache
            = new Dictionary<string, Dictionary<string, FieldAccessor>>();

        // Parameter metadata (unchanged)
        private Dictionary<string, Dictionary<string, ParameterMetadata>> _parameterMetadata;

        // Helper struct for compiled field/property accessors
        private struct FieldAccessor
        {
            public Func<object, object> Getter;
            public Action<object, object> Setter;
            public Type MemberType;
        }

        // Constructor
        public SledParameterManager()
        {
            // Define the components and field names to inspect
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
            // For brevity, assume user’s existing parameter metadata blocks remain:
            // e.g. SnowmobileController, SnowmobileControllerBase, MeshInterpretter, etc.

            // Just reaffirm ragdoll metadata:
            _parameterMetadata["RagDollCollisionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["ragdollThreshold"] = new ParameterMetadata("Ragdoll Threshold", "Threshold for ragdoll activation", 0f, 1000f),
                ["ragdollThresholdDownFactor"] = new ParameterMetadata("Ragdoll Threshold Down Factor", "Down factor for ragdoll threshold", 0f, 10f)
            };

            // (Other component metadata would be added similarly.)
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

            BuildAccessorCache();
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

            PropertyInfo prop = controller.GetType().GetProperty(
                "GKMNAIKNNMJ",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
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
            if (_accessorCache.TryGetValue(componentName, out var dict) &&
                dict.TryGetValue(fieldName, out var accessor))
            {
                return accessor.MemberType;
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

        // === PRIVATE HELPER METHODS USING COMPILED ACCESSORS (HARMONY STYLE) ===

        /// <summary>
        /// Builds (and caches) accessor delegates for all the fields/properties we want to inspect.
        /// </summary>
        private void BuildAccessorCache()
        {
            _accessorCache.Clear();

            foreach (var kvp in ComponentsToInspect)
            {
                string compName = kvp.Key;
                string[] fields = kvp.Value;
                Component comp = GetComponentByName(compName);
                Dictionary<string, FieldAccessor> accessorDict = new Dictionary<string, FieldAccessor>();

                if (comp != null)
                {
                    Type type = comp.GetType();
                    foreach (string field in fields)
                    {
                        // For Light color channels, we will handle manually later.
                        if (compName == "Light" && (field == "r" || field == "g" || field == "b" || field == "a"))
                        {
                            continue;
                        }

                        // Try to get a FieldInfo first.
                        FieldInfo fi = AccessTools.Field(type, field);
                        if (fi != null)
                        {
                            accessorDict[field] = new FieldAccessor
                            {
                                Getter = CreateGetter(fi),
                                Setter = CreateSetter(fi),
                                MemberType = fi.FieldType
                            };
                        }
                        else
                        {
                            // If no field found, try to get a PropertyInfo.
                            PropertyInfo pi = AccessTools.Property(type, field);
                            if (pi != null)
                            {
                                accessorDict[field] = new FieldAccessor
                                {
                                    Getter = CreateGetter(pi),
                                    Setter = CreateSetter(pi),
                                    MemberType = pi.PropertyType
                                };
                            }
                            else
                            {
                                MelonLogger.Warning($"[SledTuner] Could not find field or property '{field}' in {compName}.");
                            }
                        }
                    }
                }
                else
                {
                    // If the component wasn’t found, add empty accessors so that later attempts can report “not found.”
                    foreach (string field in fields)
                    {
                        accessorDict[field] = new FieldAccessor();
                    }
                }
                _accessorCache[compName] = accessorDict;
            }
        }

        /// <summary>
        /// Uses the cached accessors to inspect each component and return its current values.
        /// </summary>
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

                foreach (string field in fields)
                {
                    compValues[field] = TryReadMember(comp, compName, field);
                }

                result[compName] = compValues;
            }

            MelonLogger.Msg("[SledTuner] Component inspection complete.");
            return result;
        }

        /// <summary>
        /// Uses the accessor delegate (if available) to read the value of a given field/property.
        /// Special handling is provided for Light color channels.
        /// </summary>
        private object TryReadMember(Component comp, string compName, string fieldName)
        {
            // Special handling for Light color channels
            if (compName == "Light" && (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
            {
                var lightComp = (Light)comp;
                Color c = lightComp.color;
                switch (fieldName)
                {
                    case "r":
                        return c.r;
                    case "g":
                        return c.g;
                    case "b":
                        return c.b;
                    case "a":
                        return c.a;
                    default:
                        return "(Not found: " + fieldName + ")";
                }
            }

            if (!_accessorCache.TryGetValue(compName, out var accessorDict) ||
                !accessorDict.TryGetValue(fieldName, out FieldAccessor accessor) ||
                accessor.Getter == null)
            {
                return $"(Not found: {fieldName})";
            }

            try
            {
                object raw = accessor.Getter(comp);
                return ConvertOrSkip(raw, accessor.MemberType);
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

            if (fieldType != null && typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                return "(Skipped UnityEngine.Object)";

            if (fieldType != null && fieldType.IsEnum)
                return raw;

            if (fieldType != null && !fieldType.IsPrimitive &&
                fieldType != typeof(string) &&
                fieldType != typeof(decimal) &&
                !fieldType.IsEnum)
            {
                return "(Skipped complex type)";
            }

            return raw;
        }

        /// <summary>
        /// Returns the GameObject component by name from the sled’s body.
        /// </summary>
        private Component GetComponentByName(string compName)
        {
            if (_snowmobileBody == null)
                return null;

            if (compName == "Rigidbody")
                return _snowmobileBody.GetComponent<Rigidbody>();
            else if (compName == "Light")
            {
                Transform t = _snowmobileBody.transform.Find("Spot Light");
                return t != null ? t.GetComponent<Light>() : null;
            }
            else if (compName == "RagDollCollisionController" || compName == "RagDollManager")
            {
                Transform ikPlayer = _snowmobileBody.transform.Find("IK Player (Drivers)");
                return ikPlayer != null ? ikPlayer.GetComponent(compName) : null;
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

        /// <summary>
        /// Uses the cached accessor delegate to apply (set) a field/property value on the component.
        /// Special handling is provided for Light color channels.
        /// </summary>
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
                    try { floatVal = Convert.ToSingle(value); } catch { }
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

            if (!_accessorCache.TryGetValue(compName, out var accessorDict) ||
                !accessorDict.TryGetValue(fieldName, out FieldAccessor accessor) ||
                accessor.Setter == null)
            {
                return;
            }

            try
            {
                object converted = ConvertValue(value, accessor.MemberType);
                accessor.Setter(comp, converted);
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

        #region Accessor Builder Helpers

        /// <summary>
        /// Creates a getter delegate for the given FieldInfo.
        /// </summary>
        private static Func<object, object> CreateGetter(FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            Expression instanceCast = Expression.Convert(instanceParam, field.DeclaringType);
            Expression fieldAccess = Expression.Field(instanceCast, field);
            Expression castFieldValue = Expression.Convert(fieldAccess, typeof(object));
            return Expression.Lambda<Func<object, object>>(castFieldValue, instanceParam).Compile();
        }

        /// <summary>
        /// Creates a setter delegate for the given FieldInfo.
        /// </summary>
        private static Action<object, object> CreateSetter(FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            ParameterExpression valueParam = Expression.Parameter(typeof(object), "value");
            Expression instanceCast = Expression.Convert(instanceParam, field.DeclaringType);
            Expression valueCast = Expression.Convert(valueParam, field.FieldType);
            Expression fieldAccess = Expression.Field(instanceCast, field);
            BinaryExpression assign = Expression.Assign(fieldAccess, valueCast);
            return Expression.Lambda<Action<object, object>>(assign, instanceParam, valueParam).Compile();
        }

        /// <summary>
        /// Creates a getter delegate for the given PropertyInfo.
        /// </summary>
        private static Func<object, object> CreateGetter(PropertyInfo property)
        {
            MethodInfo getter = property.GetGetMethod(true);
            if (getter == null) return null;
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            Expression instanceCast = Expression.Convert(instanceParam, property.DeclaringType);
            Expression callGetter = Expression.Call(instanceCast, getter);
            Expression castResult = Expression.Convert(callGetter, typeof(object));
            return Expression.Lambda<Func<object, object>>(castResult, instanceParam).Compile();
        }

        /// <summary>
        /// Creates a setter delegate for the given PropertyInfo.
        /// </summary>
        private static Action<object, object> CreateSetter(PropertyInfo property)
        {
            MethodInfo setter = property.GetSetMethod(true);
            if (setter == null) return null;
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            ParameterExpression valueParam = Expression.Parameter(typeof(object), "value");
            Expression instanceCast = Expression.Convert(instanceParam, property.DeclaringType);
            Expression valueCast = Expression.Convert(valueParam, property.PropertyType);
            Expression callSetter = Expression.Call(instanceCast, setter, valueCast);
            return Expression.Lambda<Action<object, object>>(callSetter, instanceParam, valueParam).Compile();
        }

        #endregion
    }
}
