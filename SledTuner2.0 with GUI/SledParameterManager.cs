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

        // Reflection cache and metadata
        private readonly Dictionary<string, Dictionary<string, MemberWrapper>> _reflectionCache
            = new Dictionary<string, Dictionary<string, MemberWrapper>>();
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
            // Define the components and the field names to inspect.
            ComponentsToInspect = new Dictionary<string, string[]>
            {
                ["SnowmobileController"] = new string[]
                {
                    "leanSteerFactorSoft", "leanSteerFactorTrail", "throttleExponent", "drowningDepth", "drowningTime",
                    "isEngineOn", "isStuck", "canRespawn", "hasDrowned", "rpmSensitivity", "rpmSensitivityDown",
                    "minThrottleOnClutchEngagement", "clutchRpmMin", "clutchRpmMax", "isHeadlightOn",
                    "wheelieThreshold", "driverTorgueFactorRoll", "driverTorgueFactorPitch", "snowmobileTorgueFactor", "isWheeling"
                },
                ["SnowmobileControllerBase"] = new string[]
                {
                    "skisMaxAngle", "driverZCenter", "enableVerticalWeightTransfer", "trailLeanDistance", "switchbackTransitionTime",
                    "driverMaxDistanceHighStance", "driverMaxDistanceLowStance", "driverMaxDistanceSwitchBack",
                    "horizontalWeightTransferMode", "toeAngle", "hopOverPreJump", "switchBackLeanDistance"
                },
                ["MeshInterpretter"] = new string[]
                {
                    "power", "powerEfficiency", "breakForce", "frictionForce", "trackMass", "coefficientOfFriction",
                    "snowPushForceFactor", "snowPushForceNormalizedFactor", "snowSupportForceFactor", "maxSupportPressure",
                    "lugHeight", "snowOutTrackWidth", "pitchFactor", "drivetrainMinSpeed", "drivetrainMaxSpeed1", "drivetrainMaxSpeed2"
                },
                ["SnowParameters"] = new string[]
                {
                    "snowNormalConstantFactor", "snowNormalDepthFactor", "snowFrictionFactor", "snowNormalSpeedFactor"
                },
                ["SuspensionController"] = new string[]
                {
                    "suspensionSubSteps", "antiRollBarFactor", "skiAutoTurn", "trackRigidityFront", "trackRigidityRear",
                    "reduceSuspensionForceByTilt"
                },
                ["Stabilizer"] = new string[]
                {
                    "trackSpeedGyroMultiplier", "idleGyro", "trackSpeedDamping"
                },
                ["RagDollCollisionController"] = new string[]
                {
                    "ragdollThreshold", "ragdollThresholdDownFactor"
                },
                ["Rigidbody"] = new string[]
                {
                    "mass", "drag", "angularDrag", "useGravity", "maxAngularVelocity", "constraints", "interpolation", "collisionDetectionMode"
                },
                ["Light"] = new string[] { "r", "g", "b", "a" },
                ["Shock"] = new string[]
                {
                    "springFactor", "damperFactor", "fastCompressionVelocityThreshold", "fastReboundVelocityThreshold",
                    "compressionRatio", "compressionFastRatio", "reboundRatio", "reboundFastRatio"
                }
            };

            _originalValues = new Dictionary<string, Dictionary<string, object>>();
            _currentValues = new Dictionary<string, Dictionary<string, object>>();

            // Set the folder path for JSON saving/loading.
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
                ["snowmobileTorgueFactor"] = new ParameterMetadata("Snowmobile Torque Factor", "Torque factor for the snowmobile", 0f, 10f),
                ["isWheeling"] = new ParameterMetadata("Is Wheeling", "Indicates if the sled is performing a wheelie", 0f, 1f, ControlType.Toggle)
            };

            _parameterMetadata["SnowmobileControllerBase"] = new Dictionary<string, ParameterMetadata>
            {
                ["skisMaxAngle"] = new ParameterMetadata("Skis Max Angle", "Maximum angle of the skis", 0f, 90f),
                ["driverZCenter"] = new ParameterMetadata("Driver Z Center", "Vertical center offset for driver", -1f, 1f),
                ["enableVerticalWeightTransfer"] = new ParameterMetadata("Vertical Weight Transfer", "Enable vertical weight transfer", 0f, 1f, ControlType.Toggle),
                ["trailLeanDistance"] = new ParameterMetadata("Trail Lean Distance", "Distance for trail leaning", 0f, 10f),
                ["switchbackTransitionTime"] = new ParameterMetadata("Switchback Transition Time", "Time to transition during a switchback", 0.1f, 1f),
                ["driverMaxDistanceHighStance"] = new ParameterMetadata("Driver Max Distance (High Stance)", "Maximum driver distance in high stance", 0f, 2f),
                ["driverMaxDistanceLowStance"] = new ParameterMetadata("Driver Max Distance (Low Stance)", "Maximum driver distance in low stance", 0f, 2f),
                ["driverMaxDistanceSwitchBack"] = new ParameterMetadata("Driver Max Distance (Switchback)", "Driver distance during switchback", 0f, 2f),
                ["horizontalWeightTransferMode"] = new ParameterMetadata("Horizontal Weight Transfer Mode", "Mode for horizontal weight transfer", 0f, 1f),
                ["toeAngle"] = new ParameterMetadata("Toe Angle", "Angle of the toe", 0f, 90f),
                ["hopOverPreJump"] = new ParameterMetadata("Hop Over Pre-Jump", "Pre-jump hop adjustment", -1f, 1f),
                ["switchBackLeanDistance"] = new ParameterMetadata("Switchback Lean Distance", "Distance for lean during switchback", 0f, 5f)
            };

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
                ["lugHeight"] = new ParameterMetadata("Lug Height", "Lug height", 0f, 100f),
                ["snowOutTrackWidth"] = new ParameterMetadata("Snow Out Track Width", "Track width outside of snow", 0f, 5f),
                ["pitchFactor"] = new ParameterMetadata("Pitch Factor", "Factor affecting pitch", 0f, 200f),
                ["drivetrainMinSpeed"] = new ParameterMetadata("Drivetrain Min Speed", "Minimum speed for drivetrain", 0f, 50f),
                ["drivetrainMaxSpeed1"] = new ParameterMetadata("Drivetrain Max Speed 1", "Maximum speed for drivetrain configuration 1", 0f, 100f),
                ["drivetrainMaxSpeed2"] = new ParameterMetadata("Drivetrain Max Speed 2", "Maximum speed for drivetrain configuration 2", 0f, 500f)
            };

            _parameterMetadata["SnowParameters"] = new Dictionary<string, ParameterMetadata>
            {
                ["snowNormalConstantFactor"] = new ParameterMetadata("Snow Normal Constant Factor", "Constant factor for snow normals", 0f, 10f),
                ["snowNormalDepthFactor"] = new ParameterMetadata("Snow Normal Depth Factor", "Depth factor for snow normals", 0f, 10f),
                ["snowFrictionFactor"] = new ParameterMetadata("Snow Friction Factor", "Friction factor for snow", 0f, 1f),
                ["snowNormalSpeedFactor"] = new ParameterMetadata("Snow Normal Speed Factor", "Speed factor for snow normals", 0f, 10f)
            };

            _parameterMetadata["SuspensionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["suspensionSubSteps"] = new ParameterMetadata("Suspension Sub-Steps", "Number of sub-steps for suspension simulation", 1f, 500f),
                ["antiRollBarFactor"] = new ParameterMetadata("Anti-Roll Bar Factor", "Factor for anti-roll bar effect", 0f, 10000f),
                ["skiAutoTurn"] = new ParameterMetadata("Ski Auto Turn", "Automatically turn skis", 0f, 1f, ControlType.Toggle),
                ["trackRigidityFront"] = new ParameterMetadata("Track Rigidity (Front)", "Rigidity of the front track", 0f, 100f),
                ["trackRigidityRear"] = new ParameterMetadata("Track Rigidity (Rear)", "Rigidity of the rear track", 0f, 100f),
                ["reduceSuspensionForceByTilt"] = new ParameterMetadata("Reduce Suspension Force By Tilt", "Reduce suspension force based on tilt", 0f, 1f, ControlType.Toggle)
            };

            _parameterMetadata["Stabilizer"] = new Dictionary<string, ParameterMetadata>
            {
                ["trackSpeedGyroMultiplier"] = new ParameterMetadata("Track Speed Gyro Multiplier", "Multiplier for gyro effect", 0f, 100f),
                ["idleGyro"] = new ParameterMetadata("Idle Gyro", "Gyro value when idle", 0f, 1000f),
                ["trackSpeedDamping"] = new ParameterMetadata("Track Speed Damping", "Damping for track speed", 0f, 100f)
            };

            _parameterMetadata["RagDollCollisionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["ragdollThreshold"] = new ParameterMetadata("Ragdoll Threshold", "Threshold for ragdoll activation", 0f, 1000f),
                ["ragdollThresholdDownFactor"] = new ParameterMetadata("Ragdoll Threshold Down Factor", "Down factor for ragdoll threshold", 0f, 10f)
            };

            _parameterMetadata["Rigidbody"] = new Dictionary<string, ParameterMetadata>
            {
                ["mass"] = new ParameterMetadata("Mass", "Mass of the sled", 0f, 1000f),
                ["drag"] = new ParameterMetadata("Drag", "Linear drag", 0f, 10f),
                ["angularDrag"] = new ParameterMetadata("Angular Drag", "Angular drag", 0f, 10f),
                ["useGravity"] = new ParameterMetadata("Use Gravity", "Toggle gravity usage", 0f, 1f, ControlType.Toggle),
                ["maxAngularVelocity"] = new ParameterMetadata("Max Angular Velocity", "Maximum angular velocity", 0f, 100f),
                ["constraints"] = new ParameterMetadata("Constraints", "Rigidbody constraints", 0f, 6f),
                ["interpolation"] = new ParameterMetadata("Interpolation", "Rigidbody interpolation", 0f, 2f),
                ["collisionDetectionMode"] = new ParameterMetadata("Collision Detection Mode", "Collision detection mode", 0f, 3f)
            };

            _parameterMetadata["Light"] = new Dictionary<string, ParameterMetadata>
            {
                ["r"] = new ParameterMetadata("Light Red", "Red channel of Light color", 0f, 1f),
                ["g"] = new ParameterMetadata("Light Green", "Green channel of Light color", 0f, 1f),
                ["b"] = new ParameterMetadata("Light Blue", "Blue channel of Light color", 0f, 1f),
                ["a"] = new ParameterMetadata("Light Alpha", "Alpha channel of Light color", 0f, 1f)
            };

            _parameterMetadata["Shock"] = new Dictionary<string, ParameterMetadata>
            {
                ["springFactor"] = new ParameterMetadata("Spring Factor", "Spring factor for shock", 0f, 20000f),
                ["damperFactor"] = new ParameterMetadata("Damper Factor", "Damper factor for shock", 0f, 2000f),
                ["fastCompressionVelocityThreshold"] = new ParameterMetadata("Fast Compression Velocity Threshold", "Threshold for fast compression", 0f, 10f),
                ["fastReboundVelocityThreshold"] = new ParameterMetadata("Fast Rebound Velocity Threshold", "Threshold for fast rebound", 0f, 10f),
                ["compressionRatio"] = new ParameterMetadata("Compression Ratio", "Compression ratio", 0f, 2f),
                ["compressionFastRatio"] = new ParameterMetadata("Compression Fast Ratio", "Fast compression ratio", 0f, 2f),
                ["reboundRatio"] = new ParameterMetadata("Rebound Ratio", "Rebound ratio", 0f, 2f),
                ["reboundFastRatio"] = new ParameterMetadata("Rebound Fast Ratio", "Fast rebound ratio", 0f, 2f)
            };
        }

        // === PUBLIC API METHODS ===

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
            foreach (var compKvp in _currentValues)
            {
                string compName = compKvp.Key;
                foreach (var fieldKvp in compKvp.Value)
                {
                    ApplyField(compName, fieldKvp.Key, fieldKvp.Value);
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

                        if (compName == "Light" &&
                            (field == "r" || field == "g" || field == "b" || field == "a"))
                        {
                            // We'll handle color channels manually
                        }
                        else
                        {
                            FieldInfo fi = type.GetField(field,
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (fi != null)
                            {
                                wrapper.Field = fi;
                            }
                            else
                            {
                                PropertyInfo pi = type.GetProperty(field,
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (pi != null)
                                    wrapper.Property = pi;
                            }
                        }

                        memberDict[field] = wrapper;
                    }
                }
                else
                {
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
            if (compName == "Light" &&
                (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
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

            if (fieldType != null && typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                return "(Skipped UnityEngine.Object)";

            if (fieldType != null && fieldType.IsEnum)
                return raw;

            if (fieldType != null && !fieldType.IsPrimitive
                && fieldType != typeof(string)
                && fieldType != typeof(decimal)
                && !fieldType.IsEnum)
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
                // Locate "IK Player (Drivers)" under the Body
                Transform ikPlayer = _snowmobileBody.transform.Find("IK Player (Drivers)");
                if (ikPlayer == null)
                    return null;
                return ikPlayer.GetComponent(compName);
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

            if (compName == "Light" &&
                (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
            {
                var lightComp = (Light)comp;
                Color c = lightComp.color;

                float floatVal = 0f;
                if (value is double dVal) floatVal = (float)dVal;
                else if (value is float fVal) floatVal = fVal;
                else if (value is int iVal) floatVal = iVal;
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

            if (!_reflectionCache.TryGetValue(compName, out var memberDict)
                || !memberDict.TryGetValue(fieldName, out var wrapper)
                || !wrapper.IsValid)
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
