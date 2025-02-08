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
        public readonly Dictionary<string, string[]> ComponentsToInspect;

        private GameObject _snowmobileBody;
        private Rigidbody _rigidbody;
        private Light _light;

        private Dictionary<string, Dictionary<string, object>> _originalValues;
        private Dictionary<string, Dictionary<string, object>> _currentValues;

        public string JsonFolderPath { get; }
        public bool IsInitialized { get; private set; } = false;

        private readonly Dictionary<string, Dictionary<string, MemberWrapper>> _reflectionCache = new Dictionary<string, Dictionary<string, MemberWrapper>>();

        private struct MemberWrapper
        {
            public FieldInfo Field;
            public PropertyInfo Property;
            public bool IsValid => Field != null || Property != null;
            public bool CanRead => Field != null || (Property != null && Property.CanRead);
            public bool CanWrite => Field != null || (Property != null && Property.CanWrite);
            public Type MemberType => Field != null ? Field.FieldType : Property?.PropertyType;
        }

        private Dictionary<string, Dictionary<string, ParameterMetadata>> _parameterMetadata;

        public SledParameterManager()
        {
            // Initialize the components-to-inspect dictionary with all the required fields.
            Dictionary<string, string[]> dict = new Dictionary<string, string[]>();

            // SnowmobileController (added "isWheeling")
            dict["SnowmobileController"] = new string[]
            {
                "leanSteerFactorSoft", "leanSteerFactorTrail", "throttleExponent", "drowningDepth", "drowningTime",
                "isEngineOn", "isStuck", "canRespawn", "hasDrowned", "rpmSensitivity", "rpmSensitivityDown",
                "minThrottleOnClutchEngagement", "clutchRpmMin", "clutchRpmMax", "isHeadlightOn",
                "wheelieThreshold", "driverTorgueFactorRoll", "driverTorgueFactorPitch", "snowmobileTorgueFactor", "isWheeling"
            };

            // SnowmobileControllerBase (added missing fields)
            dict["SnowmobileControllerBase"] = new string[]
            {
                "skisMaxAngle", "driverZCenter", "enableVerticalWeightTransfer", "trailLeanDistance", "switchbackTransitionTime",
                "driverMaxDistanceHighStance", "driverMaxDistanceLowStance", "driverMaxDistanceSwitchBack", "horizontalWeightTransferMode",
                "toeAngle", "hopOverPreJump", "switchBackLeanDistance"
            };

            // MeshInterpretter (added "drivetrainMinSpeed")
            dict["MeshInterpretter"] = new string[]
            {
                "power", "powerEfficiency", "breakForce", "frictionForce", "trackMass", "coefficientOfFriction",
                "snowPushForceFactor", "snowPushForceNormalizedFactor", "snowSupportForceFactor", "maxSupportPressure",
                "lugHeight", "snowOutTrackWidth", "pitchFactor", "drivetrainMinSpeed", "drivetrainMaxSpeed1", "drivetrainMaxSpeed2"
            };

            // SnowParameters (added "snowNormalSpeedFactor")
            dict["SnowParameters"] = new string[]
            {
                "snowNormalConstantFactor", "snowNormalDepthFactor", "snowFrictionFactor", "snowNormalSpeedFactor"
            };

            // SuspensionController (added "reduceSuspensionForceByTilt")
            dict["SuspensionController"] = new string[]
            {
                "suspensionSubSteps", "antiRollBarFactor", "skiAutoTurn", "trackRigidityFront", "trackRigidityRear", "reduceSuspensionForceByTilt"
            };

            // Stabilizer (added "trackSpeedDamping")
            dict["Stabilizer"] = new string[]
            {
                "trackSpeedGyroMultiplier", "idleGyro", "trackSpeedDamping"
            };

            // RagDollCollisionController remains the same.
            dict["RagDollCollisionController"] = new string[]
            {
                "ragdollTreshold", "ragdollTresholdDownFactor"
            };

            // Rigidbody (added "constraints", "interpolation", "collisionDetectionMode")
            dict["Rigidbody"] = new string[]
            {
                "mass", "drag", "angularDrag", "useGravity", "maxAngularVelocity", "constraints", "interpolation", "collisionDetectionMode"
            };

            // Light remains unchanged.
            dict["Light"] = new string[] { "r", "g", "b", "a" };

            // Shock component (unchanged)
            dict["Shock"] = new string[]
            {
                "springFactor", "damperFactor", "fastCompressionVelocityThreshold", "fastReboundVelocityThreshold",
                "compressionRatio", "compressionFastRatio", "reboundRatio", "reboundFastRatio"
            };

            ComponentsToInspect = dict;
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

            // SnowmobileController metadata (added "isWheeling")
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

            // SnowmobileControllerBase metadata (added missing fields)
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

            // MeshInterpretter metadata (added "drivetrainMinSpeed")
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

            // SnowParameters metadata (added "snowNormalSpeedFactor")
            _parameterMetadata["SnowParameters"] = new Dictionary<string, ParameterMetadata>
            {
                ["snowNormalConstantFactor"] = new ParameterMetadata("Snow Normal Constant Factor", "Constant factor for snow normals", 0f, 10f),
                ["snowNormalDepthFactor"] = new ParameterMetadata("Snow Normal Depth Factor", "Depth factor for snow normals", 0f, 10f),
                ["snowFrictionFactor"] = new ParameterMetadata("Snow Friction Factor", "Friction factor for snow", 0f, 1f),
                ["snowNormalSpeedFactor"] = new ParameterMetadata("Snow Normal Speed Factor", "Speed factor for snow normals", 0f, 10f)
            };

            // SuspensionController metadata (added "reduceSuspensionForceByTilt")
            _parameterMetadata["SuspensionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["suspensionSubSteps"] = new ParameterMetadata("Suspension Sub-Steps", "Number of sub-steps for suspension simulation", 1f, 500f),
                ["antiRollBarFactor"] = new ParameterMetadata("Anti-Roll Bar Factor", "Factor for anti-roll bar effect", 0f, 10000f),
                ["skiAutoTurn"] = new ParameterMetadata("Ski Auto Turn", "Automatically turn skis", 0f, 1f, ControlType.Toggle),
                ["trackRigidityFront"] = new ParameterMetadata("Track Rigidity (Front)", "Rigidity of the front track", 0f, 100f),
                ["trackRigidityRear"] = new ParameterMetadata("Track Rigidity (Rear)", "Rigidity of the rear track", 0f, 100f),
                ["reduceSuspensionForceByTilt"] = new ParameterMetadata("Reduce Suspension Force By Tilt", "Reduce suspension force based on tilt", 0f, 1f, ControlType.Toggle)
            };

            // Stabilizer metadata (added "trackSpeedDamping")
            _parameterMetadata["Stabilizer"] = new Dictionary<string, ParameterMetadata>
            {
                ["trackSpeedGyroMultiplier"] = new ParameterMetadata("Track Speed Gyro Multiplier", "Multiplier for gyro effect based on track speed", 0f, 100f),
                ["idleGyro"] = new ParameterMetadata("Idle Gyro", "Gyro value when idle", 0f, 1000f),
                ["trackSpeedDamping"] = new ParameterMetadata("Track Speed Damping", "Damping for track speed", 0f, 100f)
            };

            // RagDollCollisionController metadata
            _parameterMetadata["RagDollCollisionController"] = new Dictionary<string, ParameterMetadata>
            {
                ["ragdollTreshold"] = new ParameterMetadata("Ragdoll Threshold", "Threshold for ragdoll activation", 0f, 1000f),
                ["ragdollTresholdDownFactor"] = new ParameterMetadata("Ragdoll Threshold Down Factor", "Down factor for ragdoll threshold", 0f, 10f)
            };

            // Rigidbody metadata (added missing fields)
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

            // Light metadata
            _parameterMetadata["Light"] = new Dictionary<string, ParameterMetadata>
            {
                ["r"] = new ParameterMetadata("Light Red", "Red channel of Light color", 0f, 1f),
                ["g"] = new ParameterMetadata("Light Green", "Green channel of Light color", 0f, 1f),
                ["b"] = new ParameterMetadata("Light Blue", "Blue channel of Light color", 0f, 1f),
                ["a"] = new ParameterMetadata("Light Alpha", "Alpha channel of Light color", 0f, 1f)
            };

            // Shock metadata
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
    }
}
