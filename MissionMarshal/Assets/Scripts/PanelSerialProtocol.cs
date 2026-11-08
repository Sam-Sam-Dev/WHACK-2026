using System;
using System.Globalization;

/// <summary>
/// ESP32 line parser, independent of ports and scene objects. Unknown messages,
/// boot text, diagnostics and malformed values return false; never use Parse().
/// Add new output formats here, then apply them in ComponentManager.
/// </summary>
public static class PanelSerialProtocol
{
    public enum Component { None, Fingerprint, Keypad, Ultrasonic, Joystick }
    public enum Kind { Selection, FingerprintMatch, FingerprintNoMatch, FingerprintStored,
        KeypadPress, Distance, Joystick, Error, FingerprintProgress, FingerprintCancelled }

    public struct Message
    {
        public Kind Type;
        public Component Component;
        public int First, Second;
        public float Value;
        public string Text;
    }

    public static bool TryParse(string line, out Message message)
    {
        message = default;
        if (string.IsNullOrWhiteSpace(line) || line.Length > 1024) return false;
        string[] words = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return false;
        string prefix = words[0] + " " + words[1];
        if (words[1] == "ERROR" &&
            (words[0] == "PANEL" || words[0] == "FP" || words[0] == "KEYPAD" ||
             words[0] == "JOYSTICK" || words[0] == "ULTRASONIC"))
        {
            message.Type = Kind.Error;
            message.Text = line;
            if (words[0] == "FP") message.Component = Component.Fingerprint;
            return true;
        }
        if (prefix == "FP CANCELLED" || (prefix == "FP CANCEL" && words.Length == 3 && words[2] == "idle"))
        {
            message.Type = Kind.FingerprintCancelled;
            message.Component = Component.Fingerprint;
            return true;
        }
        if ((prefix == "FP ENROLL" || prefix == "FP READ") && words.Length >= 3)
        {
            string prompt = words[2];
            if (prompt == "place_finger" || prompt == "place_finger_first_scan" ||
                prompt == "first_scan_ok" || prompt == "place_same_finger_second_scan")
            {
                message.Type = Kind.FingerprintProgress;
                message.Component = Component.Fingerprint;
                message.Text = prompt;
                return true;
            }
        }
        if (prefix == "PANEL IDLE")
        {
            message.Type = Kind.Selection;
            return true;
        }
        if (prefix == "PANEL ACTIVE" || prefix == "PANEL STATUS")
        {
            if (!Field(words, prefix == "PANEL ACTIVE" ? "component" : "active", out string name) ||
                !TryComponent(name, out message.Component)) return false;
            message.Type = Kind.Selection;
            return true;
        }
        if (prefix == "FP NO_MATCH")
        {
            message.Type = Kind.FingerprintNoMatch;
            message.Component = Component.Fingerprint;
            return true;
        }
        if (prefix == "FP MATCH" || prefix == "FP STORED")
        {
            if (!Integer(words, "id", out message.First) || message.First < 0 || message.First > 65535)
                return false;
            if (prefix == "FP MATCH" &&
                (!Integer(words, "confidence", out message.Second) || message.Second < 0)) return false;
            message.Type = prefix == "FP MATCH" ? Kind.FingerprintMatch : Kind.FingerprintStored;
            message.Component = Component.Fingerprint;
            return true;
        }
        if (prefix == "KEYPAD PRESS")
        {
            if (!Integer(words, "row", out int row) || !Integer(words, "col", out int col) ||
                row < 1 || row > 4 || col < 1 || col > 4) return false;
            message.First = col - 1; // Normalize ONCE: X is column, counting left to right.
            message.Second = 4 - row; // Y is row, counting down from the top.
            message.Type = Kind.KeypadPress;
            message.Component = Component.Keypad;
            return true;
        }
        if (prefix == "ULTRASONIC DISTANCE")
        {
            if (!Field(words, "cm", out string value) ||
                !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float distance) ||
                float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0) return false;
            message.Value = Math.Min(distance, 125f); // Includes ignoring the -1 timeout sentinel.
            message.Type = Kind.Distance;
            message.Component = Component.Ultrasonic;
            return true;
        }
        if (prefix == "JOYSTICK POSITION")
        {
            if (!Integer(words, "x", out message.First) || !Integer(words, "y", out message.Second) ||
                message.First < 0 || message.First > 4095 || message.Second < 0 || message.Second > 4095)
                return false;
            message.Type = Kind.Joystick;
            message.Component = Component.Joystick;
            return true;
        }
        return false;
    }

    public static bool TryComponent(string name, out Component component)
    {
        component = Component.None;
        switch (name)
        {
            case "NONE": return true;
            case "FP": component = Component.Fingerprint; return true;
            case "KEYPAD": component = Component.Keypad; return true;
            case "ULTRASONIC": component = Component.Ultrasonic; return true;
            case "JOYSTICK": component = Component.Joystick; return true;
            default: return false;
        }
    }

    // Duplicated fields are ambiguous: reject rather than guess which one wins.
    private static bool Field(string[] words, string key, out string value)
    {
        value = null;
        string prefix = key + "=";
        foreach (string word in words)
            if (word.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (value != null) return false;
                value = word.Substring(prefix.Length);
            }
        return !string.IsNullOrEmpty(value);
    }

    private static bool Integer(string[] words, string key, out int value)
    {
        value = 0;
        return Field(words, key, out string text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Remove neutral drift without losing either direction or full travel.</summary>
    public static float ApplyDeadZone(float value, float deadZone)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
        if (float.IsNaN(deadZone) || float.IsInfinity(deadZone)) deadZone = .15f;
        deadZone = Math.Max(0f, Math.Min(deadZone, .95f));
        float magnitude = Math.Min(Math.Abs(value), 1f);
        return magnitude <= deadZone ? 0f : Math.Sign(value) * (magnitude - deadZone) / (1f - deadZone);
    }

    /// <summary>Raw 0, configurable centre, and raw 4095 map to -1, 0, and +1.</summary>
    public static float NormalizeAxis(int raw, float centre)
    {
        if (float.IsNaN(centre) || float.IsInfinity(centre)) centre = 2047.5f;
        centre = Math.Max(1f, Math.Min(centre, 4094f));
        float value = raw < centre ? (raw - centre) / centre : (raw - centre) / (4095f - centre);
        return Math.Max(-1f, Math.Min(value, 1f));
    }
}
