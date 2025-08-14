#if UNITY_EDITOR
using System;
using UnityEditor;

namespace MMO.EditorTools
{
    /// <summary> Attribute to decorate modules with metadata. </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ToolkitModuleAttribute : Attribute
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Icon;
        public readonly int Order;

        public ToolkitModuleAttribute(string id, string name, int order = 0, string icon = "d_Project")
        {
            Id = id; Name = name; Order = order; Icon = icon;
        }
    }

    public interface IMMOToolkitModule
    {
        string Id { get; }
        string DisplayName { get; }
        string IconName { get; }
        int Order { get; }

        bool Enabled { get; set; } // persisted in EditorPrefs

        void ApplyMeta(ToolkitModuleAttribute attr); // window will call if present
        void OnEnable();  // called when selected
        void OnDisable(); // called when deselected
        void OnGUI();     // draw right panel UI
    }

    public abstract class MMOToolkitModuleBase : IMMOToolkitModule
    {
        public string Id { get; protected set; } = "module.unnamed";
        public string DisplayName { get; protected set; } = "Unnamed";
        public string IconName { get; protected set; } = "d_Project";
        public int Order { get; protected set; } = 0;

        string PrefKey => $"MMO.Toolkit.Module.{Id}.enabled";

        public bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public virtual void ApplyMeta(ToolkitModuleAttribute attr)
        {
            if (attr == null) return;
            Id = attr.Id;
            DisplayName = attr.Name;
            IconName = attr.Icon;
            Order = attr.Order;
        }

        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
        public abstract void OnGUI();
    }
}
#endif
