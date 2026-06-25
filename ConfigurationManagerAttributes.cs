// Sourced from https://github.com/BepInEx/BepInEx.ConfigurationManager
// Include this file directly in your project instead of referencing the plugin DLL.
// ConfigurationManager discovers these properties via reflection at runtime, so
// the mod works whether or not ConfigurationManager is installed.

using System;
using BepInEx.Configuration;

// ReSharper disable once CheckNamespace  — intentionally unnamespaced so it
// matches the type ConfigurationManager expects.
// ReSharper disable UnusedMember.Global
#pragma warning disable CS1591
public sealed class ConfigurationManagerAttributes
{
    /// <summary>Show this setting in the config manager? Defaults to true.</summary>
    public bool? Browsable;

    /// <summary>Category the setting is shown under. Overrides the section name.</summary>
    public string? Category;

    /// <summary>Additional text shown as a tooltip or secondary description.</summary>
    public string? Description;

    /// <summary>Display name override shown in place of the config key.</summary>
    public string? DispName;

    /// <summary>
    /// Order within the section. Higher values appear first.
    /// Entries without an order are sorted alphabetically below ordered entries.
    /// </summary>
    public int? Order;

    /// <summary>Hide the setting name label (useful when using a custom drawer).</summary>
    public bool? HideSettingName;

    /// <summary>Hide the "Reset to default" button.</summary>
    public bool? HideDefaultButton;

    /// <summary>Show only in the "Advanced" view.</summary>
    public bool? IsAdvanced;

    /// <summary>
    /// Custom IMGUI drawer called instead of the default widget.
    /// Signature: <c>void Draw(ConfigEntryBase entry)</c>
    /// </summary>
    public Action<ConfigEntryBase>? CustomDrawer;

    /// <summary>Prevent the user from editing the value.</summary>
    public bool? ReadOnly;
}
#pragma warning restore CS1591
