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
        // Dictionary mapping component names to arrays of field names to inspect.
        public readonly Dictionary<string, string[]> ComponentsToInspect;

        private GameObject _snowmobileBody;
        private Rigidbody _rigidbody;
        private Light _light;

        private Dictionary<string, Dictionary<string, object>> _originalValues;
        private Dictionary<string, Dictionary<string, object>> _currentValues;

        public string JsonFolderPath { get; }
        public bool IsInitialized { get; private set; } = false;

        // Cache for reflection information.
        private readonly Dictionary<string, Dictionary<string, MemberWrapper>> _reflectionCache = new Dictionary<string, Dictionary<string, MemberWrapper>>();

        // Parameter metadata for each component field.
        private Dictionary<string, Dictionary<string, ParameterMetadata>> _parameterMetadata;

        // --- Constructor ---
        public SledParameterManager()
        {
            // Set up the components to inspect.
            ComponentsToInspect = new Dictionary<string, string[]>
            {
                ["SnowmobileController"] = new string[]
                {
                    "leanSteerFactorSoft",
                    "leanSteerFactorTrail",
                    "throttleExponent",
                    "drowningDepth",
                    "drowningTime",
                    "isEngineOn",
                    "isStuck",
                    "canRespawn",
                    "hasDrowned",
                    "rpmSensitivity",
                    "rpmSensitivityDown",
                    "minThrottleOnClutchEngagement",
                    "clutchRpmMin",
                    "clutchRpmMax",
                    "isHeadlightOn",
                    "wheelieThreshold",
                    "driverTorgueFactorRoll",
                    "driverTorgueFactorPitch",
                    "snowmobileTorgueFactor"
                },
                ["SnowmobileControllerBase"] = new string[]
                {
                    "skisMaxAngle",
                    "driverZCenter",
                    "enableVerticalWeightTransfer",
                    "trailLeanDistance",
                    "switchbackTransitionTime",
                    // New parameters:
                    "hopOverPreJump",
                    "toeAngle",
                    "driverMaxDistanceSwitchBack",
                    "driverMaxDistanceHighStance"
                },
                ["MeshInterpretter"] = new string[]
                {
                    "power",
                    "powerEfficiency",
                    "breakForce",
                    "frictionForce",
                    "trackMass",
                    "coefficientOfFriction",
                    "snowPushForceFactor",
                    "snowPushForceNormalizedFactor",
                    "snowSupportForceFactor",
                    "maxSupportPressure",
                    "lugHeight",
                    "snowOutTrackWidth",
                    "pitchFactor",
                    "drivetrainMaxSpeed1",
                    "drivetrainMaxSpeed2"
                },
                ["SnowParameters"] = new string[]
                {
                    "snowNormalConstantFactor",
                    "snowNormalDepthFactor",
                    "snowFrictionFactor"
                },
                ["SuspensionController"] = new string[]
                {
                    "suspensionSubSteps",
                    "antiRollBarFactor",
                    "skiAutoTurn",
                    "trackRigidityFront",
                    "trackRigidityRear"
                },
                ["Stabilizer"] = new string[]
                {
                    "trackSpeedGyroMultiplier",
                    "idleGyro"
                },
                ["RagDollCollisionController"] = new string[]
                {
                    "ragdollTreshold",
                    "ragdollTresholdDownFactor"
                },
                ["Rigidbody"] = new string[]
                {
                    "mass",
                    "drag",
                    "angularDrag",
                    "useGravity",
                    "maxAngularVelocity"
                },
                ["Light"] = new string[]
                {
                    "r",
                    "g",
                    "b",
                    "a"
                }
            };

            _originalValues = new Dictionary<string, Dictionary<string, object>>();
            _currentValues = new Dictionary<string, Dictionary<string, object>>();

            // Set up JSON folder.
            string basePath = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "SledTuner");
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);
            JsonFolderPath = basePath;

            // Initialize parameter metadata.
            InitializeParameterMetadata();
        }

        /// <summary>
        /// Initializes the metadata for parameters.
        /// </summary>
        private void InitializeParameterMetadata()
        {
            _parameterMetadata = new Dictionary<string, Dictionary<string, ParameterMetadata>>();

            // SnowmobileController metadata.
            _parameterMetadata["SnowmobileController"] = new Dictionary<string, ParameterMetadata>
            {
                ["leanSteerFactorSoft"] = new ParameterMetadata("Steering Sensitivity (Soft)", "Sensitivity on soft terrain", 0f, 5f),
                ["leanSteerFactorTrail"] = new ParameterMetadata("Steering Sensitivity (Trail)", "Sensitivity on trail", 0f, 5f),
                ["throttleExponent"] = new ParameterMetadata("Throttle Exponent", "Exponent applied to throttle input", 0.1f, 5f),
                ["drowningDepth"] = new ParameterMetadata("Drowning Depth", "Depth threshold for drowning", 0f, 10f),
                ["drowningTime"] = new ParameterMetadata("Drowning Time", "Time before drowning occurs", 0f, 20f),
                ["isEngineOn"] = new ParameterMetadata("Engine On", "Indicates if the engine is running", 0f, 1f, ControlType.Toggle),
                ["isStuck"] = new ParameterMetadata("Is Stuck", "Indicates if the sled is stuck", 0f, 1f, ControlType.Toggle),
                ["canRespawn"] = new ParameterMetadata("Can Respawn", "Indicates if the sled can respawn", 0f, 1f, ControlType.Toggle),
                ["hasDrowned"] = new ParameterMetadata("Has Drowned", "Indicates if the sled has drowned", 0f, 1f, ControlType.Toggle),
                ["rpmSensitivity"] = new ParameterMetadata("RPM Sensitivity", "Sensitivity of RPM increase", 0.01f, 0.09f),
                ["rpmSensitivityDown"] = new ParameterMetadata("RPM Sensitivity Down", "Sensitivity of RPM decrease", 0.01f, 0.09f),
                ["minThrottleOnClutchEngagement"] = new ParameterMetadata("Min Throttle (Clutch)", "Minimum throttle on clutch engagement", 0f, 1f),
                ["clutchRpmMin"] = new ParameterMetadata("Clutch RPM Min", "Minimum RPM for clutch engagement", 0f, 15000f),
                ["clutchRpmMax"] = new ParameterMetadata("Clutch RPM Max", "Maximum RPM for clutch engagement", 0f, 15000f),
                ["isHeadlightOn"] = new ParameterMetadata("Headlight On", "Indicates if the headlight is on", 0f, 1f, ControlType.Toggle),
                ["wheelieThreshold"] = new ParameterMetadata("Wheelie Threshold", "Threshold for initiating a wheelie", -100f, 100f),
                ["driverTorgueFactorRoll"] = new ParameterMetadata("Driver Torque Factor Roll", "Roll torque factor applied to driver", 0f, 1000f),
                ["driverTorgueFactorPitch"] = new ParameterMetadata("Driver Torque Factor Pitch", "Pitch torque factor applied to driver", 0f, 1000f),
                ["snowmobileTorgueFactor"] = new ParameterMetadata("Snowmobile Torque Factor", "Torque factor for the snowmobile", 0f, 10f)
            };

            // SnowmobileControllerBase metadata.
            _parameterMetadata["SnowmobileControllerBase"] = new Dictionary<string, ParameterMetadata>
            {
                ["skisMaxAngle"] = new ParameterMetadata("Skis Max Angle", "Maximum angle of the skis", 0f, 90f),
                ["driverZCenter"] = new ParameterMetadata("Driver Z Center", "Vertical center offset for driver", -1f, 1f),
                ["enableVerticalWeightTransfer"] = new ParameterMetadata("Vertical Weight Transfer", "Enable vertical weight transfer", 0f, 1f, ControlType.Toggle),
                ["trailLeanDistance"] = new ParameterMetadata("Trail Lean Distance", "Distance for trail leaning", 0f, 10f),
                ["switchbackTransitionTime"] = new ParameterMetadata("Switchback Transition Time", "Time to transition during a switchback", 0.1f, 1.0f),
                // New parameters:
                ["hopOverPreJump"] = new ParameterMetadata("Hop Over Pre-Jump", "Pre-jump parameter for obstacle clearance", 0f, 5f),
                ["toeAngle"] = new ParameterMetadata("Toe Angle", "Angle of the toe", 0f, 45f),
                ["driverMaxDistanceSwitchBack"] = new ParameterMetadata("Driver Max Distance SwitchBack", "Maximum driver distance during a switchback", 0f, 10f),
                ["driverMaxDistanceHighStance"] = new ParameterMetadata("Driver Max Distance High Stance", "Maximum driver distance in high stance", 0f, 10f)
            };

            // MeshInterpretter metadata.
            _parameterMetadata["MeshInterpretter"] = new Dictionary<string, ParameterMetadata>
            {
                ["power"] = new ParameterMetadata("Power", "Engine power", 0f, 1000000f),
                ["powerEfficiency"] = new ParameterMetadata("Power Efficiency", "Efficiency of power usage", 0f, 10f),
                ["breakForce"] = new ParameterMetadata("Brake Force", "Force applied during braking", 0f, 2000000f),
                ["frictionForce"] = new ParameterMetadata("Friction Force", "Friction force applied", 0f, 50000f),
                ["trackMass"] = new ParameterMetadata("Track Mass", "Mass of the track", 0f, 100f),
                ["coefficientOfFriction"] = new ParameterMetadata("Coefficient of Friction", "Coefficient of friction", 0.01f, 0.9f),
                ["snowPushForceFactor"] = new ParameterMetadata("Snow Push Force Factor", "Force factor for pushing snow", 0f, 200f),
                ["snowPushForceNormalizedFactor"] = new ParameterMetadata("Snow Push Force Normalized", "Normalized factor for pushing snow", 0f, 10000f),
                ["snowSupportForceFactor"] = new ParameterMetadata("Snow Support Force Factor", "Support force factor for snow", 0f, 10000f),
                ["maxSupportPressure"] = new ParameterMetadata("Max Support Pressure", "Maximum support pressure", 0.1f, 1f),
                ["lugHeight"] = new ParameterMetadata("Lug Height", "Height of the lugs", 0f, 100f),
                ["snowOutTrackWidth"] = new ParameterMetadata("Snow Out Track Width", "Track width outside of snow", 0f, 5f),
                ["pitchFactor"] = new ParameterMetadata("Pitch Factor", "Factor affecting pitch", 0f, 200f),
                ["drivetrainMaxSpeed1"] = new ParameterMetadata("Drivetrain Max Speed 1", "Maximum speed for drivetrain configuration 1", 0f, 100f),
                ["drivetrainMaxSpeed2"] = new ParameterMetadata("Drivetrain Max Speed 2", "Maximum speed for drivetrain configuration 2", 0f, 500f)
            };

            // SnowParameters metadata.
            _parameterMetadata["SnowParameters"] = new Dictionary<string, ParameterMetadata>
            {
                ["snowNormalConstantFactor"] = new ParameterMetadata("Snow Normal Constant Factor", "Constant factor for snow normals", 0f, 10f),
                ["snowNormalDepthFactor"] = new ParameterMetadata("Snow Normal Depth Factor", "Depth factor for snow normals", 0f, 10f),
                ["snowFrictionFactor"] = new ParameterMetadata("Snow Friction Factor", "Friction factor for snow", 0f, 1f)
            };

            // SuspensionController metadata.
            _parameterMetadata["SuspensionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["suspensionSubSteps"] = new ParameterMetadata("Suspension Sub-Steps", "Number of sub-steps for suspension simulation", 1f, 500f),
                ["antiRollBarFactor"] = new ParameterMetadata("Anti-Roll Bar Factor", "Factor for anti-roll bar effect", 0f, 10000f),
                ["skiAutoTurn"] = new ParameterMetadata("Ski Auto Turn", "Automatically turn skis", 0f, 1f, ControlType.Toggle),
                ["trackRigidityFront"] = new ParameterMetadata("Track Rigidity (Front)", "Rigidity of the front track", 0f, 100f),
                ["trackRigidityRear"] = new ParameterMetadata("Track Rigidity (Rear)", "Rigidity of the rear track", 0f, 100f)
            };

            // Stabilizer metadata.
            _parameterMetadata["Stabilizer"] = new Dictionary<string, ParameterMetadata>
            {
                ["trackSpeedGyroMultiplier"] = new ParameterMetadata("Track Speed Gyro Multiplier", "Multiplier for gyro effect based on track speed", 0f, 100f),
                ["idleGyro"] = new ParameterMetadata("Idle Gyro", "Gyro value when idle", 0f, 1000f)
            };

            // RagDollCollisionController metadata.
            _parameterMetadata["RagDollCollisionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["ragdollTreshold"] = new ParameterMetadata("Ragdoll Threshold", "Threshold for ragdoll activation", 0f, 10f),
                ["ragdollTresholdDownFactor"] = new ParameterMetadata("Ragdoll Threshold Down Factor", "Down factor for ragdoll threshold", 0f, 10f)
            };

            // Rigidbody metadata.
            _parameterMetadata["Rigidbody"] = new Dictionary<string, ParameterMetadata>
            {
                ["mass"] = new ParameterMetadata("Mass", "Mass of the sled", 0f, 1000f),
                ["drag"] = new ParameterMetadata("Drag", "Linear drag", 0f, 10f),
                ["angularDrag"] = new ParameterMetadata("Angular Drag", "Angular drag", 0f, 10f),
                ["useGravity"] = new ParameterMetadata("Use Gravity", "Toggle gravity usage", 0f, 1f, ControlType.Toggle),
                ["maxAngularVelocity"] = new ParameterMetadata("Max Angular Velocity", "Maximum angular velocity", 0f, 100f)
            };

            // Light metadata.
            _parameterMetadata["Light"] = new Dictionary<string, ParameterMetadata>
            {
                ["r"] = new ParameterMetadata("Light Red", "Red channel of light color", 0f, 1f),
                ["g"] = new ParameterMetadata("Light Green", "Green channel of light color", 0f, 1f),
                ["b"] = new ParameterMetadata("Light Blue", "Blue channel of light color", 0f, 1f),
                ["a"] = new ParameterMetadata("Light Alpha", "Alpha (transparency) channel of light color", 0f, 1f)
            };
        }

        // === COMPONENT INITIALIZATION AND REFLECTION METHODS ===

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

            MelonLogger.Msg("[SledTuner] Searching for 'Spot Light' under the Body transform...");
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

            foreach (KeyValuePair<string, string[]> kvp in ComponentsToInspect)
            {
                string compName = kvp.Key;
                string[] fields = kvp.Value;
                Component comp = GetComponentByName(compName);
                Dictionary<string, MemberWrapper> memberLookup = new Dictionary<string, MemberWrapper>();

                if (comp != null)
                {
                    Type compType = comp.GetType();
                    foreach (string fieldName in fields)
                    {
                        MemberWrapper wrapper = new MemberWrapper();
                        FieldInfo fi = compType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (fi != null)
                        {
                            wrapper.Field = fi;
                        }
                        else
                        {
                            PropertyInfo pi = compType.GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (pi != null)
                                wrapper.Property = pi;
                        }
                        memberLookup[fieldName] = wrapper;
                    }
                    MelonLogger.Msg($"[SledTuner] Component '{compName}': Found {memberLookup.Count} fields.");
                }
                else
                {
                    MelonLogger.Warning($"[SledTuner] Component '{compName}' not found during reflection; adding empty entries.");
                    foreach (string fieldName in fields)
                        memberLookup[fieldName] = new MemberWrapper();
                }
                _reflectionCache[compName] = memberLookup;
            }
            MelonLogger.Msg("[SledTuner] Reflection cache build complete.");
        }

        public object GetFieldValue(string componentName, string fieldName)
        {
            if (_currentValues.TryGetValue(componentName, out Dictionary<string, object> dictionary) &&
                dictionary.TryGetValue(fieldName, out object value))
            {
                return value;
            }
            return null;
        }

        public Type GetFieldType(string componentName, string fieldName)
        {
            if (_reflectionCache.TryGetValue(componentName, out Dictionary<string, MemberWrapper> members) &&
                members.TryGetValue(fieldName, out MemberWrapper wrapper))
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
            if (_parameterMetadata.TryGetValue(componentName, out Dictionary<string, ParameterMetadata> dict) &&
                dict.TryGetValue(fieldName, out ParameterMetadata meta))
            {
                return meta.MinValue;
            }
            return -100f;
        }

        public float GetSliderMax(string componentName, string fieldName)
        {
            if (_parameterMetadata.TryGetValue(componentName, out Dictionary<string, ParameterMetadata> dict) &&
                dict.TryGetValue(fieldName, out ParameterMetadata meta))
            {
                return meta.MaxValue;
            }
            return 100f;
        }

        public void ApplyParameters()
        {
            foreach (KeyValuePair<string, Dictionary<string, object>> compKvp in _currentValues)
            {
                string compName = compKvp.Key;
                foreach (KeyValuePair<string, object> fieldKvp in compKvp.Value)
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
            foreach (KeyValuePair<string, Dictionary<string, object>> compKvp in _originalValues)
            {
                string compName = compKvp.Key;
                foreach (KeyValuePair<string, object> fieldKvp in compKvp.Value)
                {
                    ApplyField(compName, fieldKvp.Key, fieldKvp.Value);
                }
            }
            MelonLogger.Msg("[SledTuner] Reset to original parameters.");
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

        public string GetSledName()
        {
            if (_snowmobileBody == null)
                return null;
            Component comp = _snowmobileBody.GetComponent("SnowmobileController");
            if (comp == null)
            {
                MelonLogger.Warning("[SledTuner] SnowmobileController not found on the body.");
                return null;
            }
            PropertyInfo prop = comp.GetType().GetProperty("GKMNAIKNNMJ", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || !prop.CanRead)
            {
                MelonLogger.Warning("[SledTuner] GKMNAIKNNMJ property missing or unreadable.");
                return null;
            }
            try
            {
                object val = prop.GetValue(comp, null);
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
            Dictionary<string, Dictionary<string, object>> result = new Dictionary<string, Dictionary<string, object>>();

            foreach (KeyValuePair<string, string[]> kvp in ComponentsToInspect)
            {
                string compName = kvp.Key;
                string[] fields = kvp.Value;
                Component comp = GetComponentByName(compName);
                if (comp == null)
                {
                    MelonLogger.Warning($"[SledTuner] Component '{compName}' not found during inspection; skipping.");
                    continue;
                }
                Dictionary<string, object> compDict = new Dictionary<string, object>();
                if (!_reflectionCache.ContainsKey(compName))
                {
                    foreach (string field in fields)
                        compDict[field] = "(No reflection cache for " + field + ")";
                    result[compName] = compDict;
                    continue;
                }
                foreach (string field in fields)
                {
                    compDict[field] = TryReadCachedMember(comp, compName, field);
                }
                result[compName] = compDict;
            }
            MelonLogger.Msg("[SledTuner] Component inspection complete.");
            return result;
        }

        private object TryReadCachedMember(Component comp, string compName, string fieldName)
        {
            if (!_reflectionCache[compName].TryGetValue(fieldName, out MemberWrapper wrapper) || !wrapper.IsValid)
                return "(Not found: " + fieldName + ")";
            if (!wrapper.CanRead)
                return "(Not readable)";
            try
            {
                object raw = (wrapper.Field != null) ? wrapper.Field.GetValue(comp) : wrapper.Property.GetValue(comp, null);
                return ConvertOrSkip(raw, wrapper.MemberType);
            }
            catch (Exception ex)
            {
                return "Error reading '" + fieldName + "': " + ex.Message;
            }
        }

        private void ApplyField(string compName, string fieldName, object value)
        {
            Component comp = GetComponentByName(compName);
            if (comp == null)
                return;
            if (!_reflectionCache.TryGetValue(compName, out Dictionary<string, MemberWrapper> memberDict) ||
                !memberDict.TryGetValue(fieldName, out MemberWrapper wrapper) ||
                !wrapper.IsValid)
                return;
            if (!wrapper.CanWrite)
            {
                MelonLogger.Warning($"[SledTuner] {fieldName} in {compName} is read-only.");
                return;
            }
            try
            {
                object convertedValue = ConvertValue(value, wrapper.MemberType);
                if (wrapper.Field != null)
                    wrapper.Field.SetValue(comp, convertedValue);
                else
                    wrapper.Property.SetValue(comp, convertedValue, null);
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
                if (targetType == typeof(float) && raw is double d)
                    return (float)d;
                if (targetType == typeof(int) && raw is long l)
                    return (int)l;
                if (targetType == typeof(bool) && raw is bool b)
                    return b;
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
            if (fieldType != null && !fieldType.IsPrimitive && fieldType != typeof(string) && fieldType != typeof(decimal))
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

        // --- STRUCT FOR REFLECTION WRAPPER ---
        private struct MemberWrapper
        {
            public FieldInfo Field;
            public PropertyInfo Property;
            public bool IsValid => Field != null || Property != null;
            public bool CanRead => Field != null || (Property != null && Property.CanRead);
            public bool CanWrite => Field != null || (Property != null && Property.CanWrite);
            public Type MemberType => (Field != null) ? Field.FieldType : Property?.PropertyType;
        }
    }
}
