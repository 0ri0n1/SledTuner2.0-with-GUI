using System;

namespace SledTunerProject
{
    public enum ControlType
    {
        Slider,
        Toggle,
        Text,
        Dropdown
    }

    public class ParameterMetadata
    {
        public string DisplayName;
        public string Description;
        public float MinValue;
        public float MaxValue;
        public ControlType Control;

        public ParameterMetadata(string displayName, string description, float minValue, float maxValue, ControlType control = ControlType.Slider)
        {
            DisplayName = displayName;
            Description = description;
            MinValue = minValue;
            MaxValue = maxValue;
            Control = control;
        }
    }
}
