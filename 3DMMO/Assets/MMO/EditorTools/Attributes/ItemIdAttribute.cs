using UnityEngine;

/// <summary>
/// Marks a string field to be drawn as an ItemDef picker popup in the inspector.
/// </summary>
public class ItemIdAttribute : PropertyAttribute
{
    public readonly bool showNone;
    public ItemIdAttribute(bool showNone = true) { this.showNone = showNone; }
}
