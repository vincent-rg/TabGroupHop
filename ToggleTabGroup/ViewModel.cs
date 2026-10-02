using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ToggleTabGroup
{
    /// <summary>
    /// Reflection access to the internal WPF window-management model
    /// (Microsoft.VisualStudio.Shell.ViewManager.dll), which is the only place tab groups exist.
    /// </summary>
    internal static class ViewModel
    {
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>WindowFrame.FrameView: the PlatformUI.Shell.View (the tab) of a frame.</summary>
        public static object GetView(IVsWindowFrame frame) => GetProperty(frame, "FrameView");

        public static bool IsVisible(object element) => GetProperty(element, "IsVisible") as bool? != false;

        public static object GetRoot(object element)
        {
            object node = element;
            while (GetProperty(node, "Parent") is object parent)
                node = parent;
            return node;
        }

        public static bool IsOfType(object obj, string typeName)
        {
            for (Type t = obj?.GetType(); t != null; t = t.BaseType)
            {
                if (t.Name == typeName)
                    return true;
            }
            return false;
        }

        public static IEnumerable<object> Enumerate(object collection)
        {
            return collection is IEnumerable enumerable ? enumerable.Cast<object>() : Enumerable.Empty<object>();
        }

        public static object GetProperty(object obj, string name)
        {
            if (obj == null)
                return null;

            // Walk the hierarchy so that a "new" redefinition doesn't cause AmbiguousMatchException.
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo prop = t.GetProperty(name, InstanceFlags | BindingFlags.DeclaredOnly);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(obj);
            }
            return null;
        }
    }

    /// <summary>
    /// Where a document frame sits: its View, its DocumentGroup, and the visible sibling groups.
    /// </summary>
    internal sealed class DocumentGroupLocation
    {
        public object View { get; private set; }
        public object Group { get; private set; }
        public object Container { get; private set; }
        public IReadOnlyList<object> Groups { get; private set; }
        public int Index { get; private set; }

        /// <summary>False when the document is in a floating window (FloatSite).</summary>
        public bool IsInMainWindow { get; private set; }

        /// <summary>Returns null if the frame is not hosted in a DocumentGroup.</summary>
        public static DocumentGroupLocation Find(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            object view = ViewModel.GetView(frame);
            object group = ViewModel.GetProperty(view, "Parent");
            if (!ViewModel.IsOfType(group, "DocumentGroup"))
                return null;

            object container = ViewModel.GetProperty(group, "Parent");
            List<object> groups = ViewModel.Enumerate(ViewModel.GetProperty(container, "VisibleChildren"))
                .Where(g => ViewModel.IsOfType(g, "DocumentGroup"))
                .ToList();

            return new DocumentGroupLocation
            {
                View = view,
                Group = group,
                Container = container,
                Groups = groups,
                Index = groups.IndexOf(group),
                IsInMainWindow = ViewModel.IsOfType(ViewModel.GetRoot(group), "MainSite"),
            };
        }

        /// <summary>The tab to focus in a group: its selected tab, or else its first visible one.</summary>
        public static object GetActiveTab(object group)
        {
            object selected = ViewModel.GetProperty(group, "SelectedElement");
            if (ViewModel.IsOfType(selected, "View") && ViewModel.IsVisible(selected))
                return selected;

            return ViewModel.Enumerate(ViewModel.GetProperty(group, "Children"))
                .FirstOrDefault(v => ViewModel.IsOfType(v, "View") && ViewModel.IsVisible(v));
        }
    }
}
