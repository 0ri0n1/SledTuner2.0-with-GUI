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
        private readonly Dictionary<string, Dictionary<string, MemberWrapper>> _reflectionCache
            = new Dictionary<string, Dictionary<string, MemberWrapper>>();

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
            // Define the components and field names to inspect, reflecting the
            // final known real fields for Shock, minus "hard"/"soft", plus "compression"/"mass"/"maxCompression"/"velocity".
            // Also removing horizontalWeightTransferMode and driverMaxDistance* from SnowmobileControllerBase,
            // and keeping ragdollThreshold fields for RagDollCollisionController.

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

                // Final Shock fields as per the screenshot, minus "hard" and "soft":
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

            // Set the folder path for JSON saving/loading
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

            // SledParameterManager wants to reflect your final "shock" changes:
            // Instead of the "springFactor" etc., we have "compression", "mass", "maxCompression", "velocity" ...
            // with appropriate slider ranges or toggles if needed.

            // SnowmobileController
            _parameterMetadata["SnowmobileController"] = new Dictionary<string, ParameterMetadata>
            {
                ["leanSteerFactorSoft"] = new ParameterMetadata("Steering Sensitivity (Soft)", "Sensitivity on soft terrain", 0f, 5f),
                ["leanSteerFactorTrail"] = new ParameterMetadata("Steering Sensitivity (Trail)", "Sensitivity on trail", 0f, 5f),
                ["throttleExponent"] = new ParameterMetadata("Throttle Exponent", "Exponent on throttle input", 0.1f, 5f),
                ["drowningDepth"] = new ParameterMetadata("Drowning Depth", "Depth threshold for drowning", 0f, 10f),
                ["drowningTime"] = new ParameterMetadata("Drowning Time", "Time before drowning occurs", 0f, 20f),
                ["isEngineOn"] = new ParameterMetadata("Engine On", "Is the engine running", 0f, 1f, ControlType.Toggle),
                ["isStuck"] = new ParameterMetadata("Is Stuck", "Is the sled stuck", 0f, 1f, ControlType.Toggle),
                ["canRespawn"] = new ParameterMetadata("Can Respawn", "Can the sled respawn", 0f, 1f, ControlType.Toggle),
                ["hasDrowned"] = new ParameterMetadata("Has Drowned", "Has the sled drowned", 0f, 1f, ControlType.Toggle),
                ["rpmSensitivity"] = new ParameterMetadata("RPM Sensitivity", "RPM increase sensitivity", 0.01f, 0.09f),
                ["rpmSensitivityDown"] = new ParameterMetadata("RPM Sensitivity Down", "RPM decrease sensitivity", 0.01f, 0.09f),
                ["minThrottleOnClutchEngagement"] = new ParameterMetadata("Min Throttle (Clutch)", "Min throttle on clutch", 0f, 1f),
                ["clutchRpmMin"] = new ParameterMetadata("Clutch RPM Min", "Min RPM for clutch", 0f, 15000f),
                ["clutchRpmMax"] = new ParameterMetadata("Clutch RPM Max", "Max RPM for clutch", 0f, 15000f),
                ["isHeadlightOn"] = new ParameterMetadata("Headlight On", "Is headlight on", 0f, 1f, ControlType.Toggle),
                ["wheelieThreshold"] = new ParameterMetadata("Wheelie Threshold", "Wheelie threshold", -100f, 100f),
                ["driverTorgueFactorRoll"] = new ParameterMetadata("Driver Torque Factor Roll", "Roll torque factor", 0f, 1000f),
                ["driverTorgueFactorPitch"] = new ParameterMetadata("Driver Torque Factor Pitch", "Pitch torque factor", 0f, 1000f),
                ["snowmobileTorgueFactor"] = new ParameterMetadata("Snowmobile Torque Factor", "Sled torque factor", 0f, 10f),
                ["isWheeling"] = new ParameterMetadata("Is Wheeling", "Is sled wheeling", 0f, 1f, ControlType.Toggle)
            };

            // SnowmobileControllerBase
            _parameterMetadata["SnowmobileControllerBase"] = new Dictionary<string, ParameterMetadata>
            {
                ["skisMaxAngle"] = new ParameterMetadata("Skis Max Angle", "Max angle of skis", 0f, 90f),
                ["driverZCenter"] = new ParameterMetadata("Driver Z Center", "Vertical center offset", -1f, 1f),
                ["enableVerticalWeightTransfer"] = new ParameterMetadata("Vertical Weight Transfer", "Toggle vertical transfer", 0f, 1f, ControlType.Toggle),
                ["trailLeanDistance"] = new ParameterMetadata("Trail Lean Distance", "Distance for trail leaning", 0f, 10f),
                ["switchbackTransitionTime"] = new ParameterMetadata("Switchback Transition Time", "Time to transition in switchback", 0.1f, 1f),
                ["toeAngle"] = new ParameterMetadata("Toe Angle", "Angle of the toe", 0f, 90f),
                ["hopOverPreJump"] = new ParameterMetadata("Hop Over Pre-Jump", "Pre-jump hop adjustment", -1f, 1f),
                ["switchBackLeanDistance"] = new ParameterMetadata("Switchback Lean Distance", "Distance for leaning in switchback", 0f, 5f)
            };

            // MeshInterpretter
            _parameterMetadata["MeshInterpretter"] = new Dictionary<string, ParameterMetadata>
            {
                ["power"] = new ParameterMetadata("Power", "Engine power", 0f, 1000000f),
                ["powerEfficiency"] = new ParameterMetadata("Power Efficiency", "Efficiency of power usage", 0f, 10f),
                ["breakForce"] = new ParameterMetadata("Brake Force", "Force applied during braking", 0f, 2000000f),
                ["frictionForce"] = new ParameterMetadata("Friction Force", "Friction force applied", 0f, 50000f),
                ["trackMass"] = new ParameterMetadata("Track Mass", "Mass of the track", 0f, 100f),
                ["coefficientOfFriction"] = new ParameterMetadata("Coefficient of Friction", "Friction coefficient", 0.01f, 0.9f),
                ["snowPushForceFactor"] = new ParameterMetadata("Snow Push Force Factor", "Snow push force factor", 0f, 200f),
                ["snowPushForceNormalizedFactor"] = new ParameterMetadata("Snow Push Force Normalized", "Normalized factor pushing snow", 0f, 10000f),
                ["snowSupportForceFactor"] = new ParameterMetadata("Snow Support Force Factor", "Snow support factor", 0f, 10000f),
                ["maxSupportPressure"] = new ParameterMetadata("Max Support Pressure", "Maximum support pressure", 0.1f, 1f),
                ["lugHeight"] = new ParameterMetadata("Lug Height", "Lug height", 0f, 100f),
                ["snowOutTrackWidth"] = new ParameterMetadata("Snow Out Track Width", "Track width outside snow", 0f, 5f),
                ["pitchFactor"] = new ParameterMetadata("Pitch Factor", "Factor for pitch", 0f, 200f),
                ["drivetrainMinSpeed"] = new ParameterMetadata("Drivetrain Min Speed", "Minimum drivetrain speed", 0f, 50f),
                ["drivetrainMaxSpeed1"] = new ParameterMetadata("Drivetrain Max Speed 1", "Max speed config 1", 0f, 100f),
                ["drivetrainMaxSpeed2"] = new ParameterMetadata("Drivetrain Max Speed 2", "Max speed config 2", 0f, 500f)
            };

            // SnowParameters
            _parameterMetadata["SnowParameters"] = new Dictionary<string, ParameterMetadata>
            {
                ["snowNormalConstantFactor"] = new ParameterMetadata("Snow Normal Constant Factor", "Constant factor for snow normals", 0f, 10f),
                ["snowNormalDepthFactor"] = new ParameterMetadata("Snow Normal Depth Factor", "Depth factor for snow normals", 0f, 10f),
                ["snowFrictionFactor"] = new ParameterMetadata("Snow Friction Factor", "Friction factor for snow", 0f, 1f),
                ["snowNormalSpeedFactor"] = new ParameterMetadata("Snow Normal Speed Factor", "Speed factor for snow normals", 0f, 10f)
            };

            // SuspensionController
            _parameterMetadata["SuspensionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["suspensionSubSteps"] = new ParameterMetadata("Suspension Sub-Steps", "Number of sub-steps for suspension", 1f, 500f),
                ["antiRollBarFactor"] = new ParameterMetadata("Anti-Roll Bar Factor", "Factor for anti-roll bar", 0f, 10000f),
                ["skiAutoTurn"] = new ParameterMetadata("Ski Auto Turn", "Auto-turn skis", 0f, 1f, ControlType.Toggle),
                ["trackRigidityFront"] = new ParameterMetadata("Track Rigidity (Front)", "Front track rigidity", 0f, 100f),
                ["trackRigidityRear"] = new ParameterMetadata("Track Rigidity (Rear)", "Rear track rigidity", 0f, 100f),
                ["reduceSuspensionForceByTilt"] = new ParameterMetadata("Reduce Suspension Force By Tilt", "Reduce force by tilt", 0f, 1f, ControlType.Toggle)
            };

            // Stabilizer
            _parameterMetadata["Stabilizer"] = new Dictionary<string, ParameterMetadata>
            {
                ["trackSpeedGyroMultiplier"] = new ParameterMetadata("Track Speed Gyro Multiplier", "Multiplier for gyro effect", 0f, 100f),
                ["idleGyro"] = new ParameterMetadata("Idle Gyro", "Gyro value when idle", 0f, 1000f),
                ["trackSpeedDamping"] = new ParameterMetadata("Track Speed Damping", "Damping for track speed", 0f, 100f)
            };

            // RagDollCollisionController
            _parameterMetadata["RagDollCollisionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["ragdollThreshold"] = new ParameterMetadata("Ragdoll Threshold", "Threshold for ragdoll activation", 0f, 1000f),
                ["ragdollThresholdDownFactor"] = new ParameterMetadata("Ragdoll Threshold Down Factor", "Down factor for ragdoll threshold", 0f, 10f)
            };

            // Rigidbody
            _parameterMetadata["Rigidbody"] = new Dictionary<string, ParameterMetadata>
            {
                ["mass"] = new ParameterMetadata("Mass", "Mass of the sled", 0f, 1000f),
                ["drag"] = new ParameterMetadata("Drag", "Linear drag", 0f, 10f),
                ["angularDrag"] = new ParameterMetadata("Angular Drag", "Angular drag", 0f, 10f),
                ["useGravity"] = new ParameterMetadata("Use Gravity", "Toggle gravity usage", 0f, 1f, ControlType.Toggle),
                ["maxAngularVelocity"] = new ParameterMetadata("Max Angular Velocity", "Max angular velocity", 0f, 100f),
            };

            // Light
            _parameterMetadata["Light"] = new Dictionary<string, ParameterMetadata>
            {
                ["r"] = new ParameterMetadata("Light Red", "Red channel of Light color", 0f, 1f),
                ["g"] = new ParameterMetadata("Light Green", "Green channel of Light color", 0f, 1f),
                ["b"] = new ParameterMetadata("Light Blue", "Blue channel of Light color", 0f, 1f),
                ["a"] = new ParameterMetadata("Light Alpha", "Alpha channel of Light color", 0f, 1f)
            };

            // Shock (final fields from screenshot, minus "hard"/"soft")
            _parameterMetadata["Shock"] = new Dictionary<string, ParameterMetadata>
            {
                ["compression"] = new ParameterMetadata("Compression", "Compression amount", 0f, 100f),
                ["mass"] = new ParameterMetadata("Shock Mass", "Mass of the shock", 0f, 300f),
                ["maxCompression"] = new ParameterMetadata("Max Compression", "Maximum compression travel", 0f, 100f),
                ["velocity"] = new ParameterMetadata("Shock Velocity", "Current velocity of the shock", 0f, 300f)
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

                        // We'll skip attaching a field or property for Light color channels 
                        // because they're handled manually
                        if (compName == "Light" &&
                            (field == "r" || field == "g" || field == "b" || field == "a"))
                        {
                            // do nothing special here
                        }
                        else
                        {
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
                        }

                        memberDict[field] = wrapper;
                    }
                }
                else
                {
                    // If we didn't find the component at all, store empty wrappers
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

                // If there's no reflection cache for compName, fill with placeholders
                if (!_reflectionCache.ContainsKey(compName))
                {
                    foreach (string field in fields)
                    {
                        compValues[field] = $"(No reflection cache for {field})";
                    }
                    result[compName] = compValues;
                    continue;
                }

                // Read each field
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
            // Special handling for Light color channels
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

            // Check the reflection cache
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

            // If it's a UnityEngine.Object or a complex type, skip
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

            // Otherwise, it's numeric/bool/string => return as is
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
                // Looks for "IK Player (Drivers)" under the Body
                Transform ikPlayer = _snowmobileBody.transform.Find("IK Player (Drivers)");
                if (ikPlayer == null)
                    return null;

                // Must match script name exactly: "RagDollCollisionController"
                return ikPlayer.GetComponent(compName);
            }
            else if (compName == "Shock")
            {
                // Check front suspension first
                Transform frontSuspension = _snowmobileBody.transform.Find("Front Suspension");
                if (frontSuspension != null)
                {
                    Component shockComp = frontSuspension.GetComponent(compName);
                    if (shockComp != null)
                        return shockComp;
                }
                // Check rear suspension next
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
                // Default approach: get by name from the Body
                return _snowmobileBody.GetComponent(compName);
            }
        }

        private void ApplyField(string compName, string fieldName, object value)
        {
            Component comp = GetComponentByName(compName);
            if (comp == null)
                return;

            // Special handling for Light color channels
            if (compName == "Light" &&
                (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
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

            // Check reflection cache
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
                // handle numeric conversions
                if (targetType == typeof(float) && raw is double dVal)
                    return (float)dVal;
                if (targetType == typeof(int) && raw is long lVal)
                    return (int)lVal;
                if (targetType == typeof(bool) && raw is bool bVal)
                    return bVal;
                if (targetType.IsInstanceOfType(raw))
                    return raw;

                // fallback to .NET conversion
                return Convert.ChangeType(raw, targetType);
            }
            catch
            {
                // if conversion fails, just return raw
                return raw;
            }
        }
    }
}
