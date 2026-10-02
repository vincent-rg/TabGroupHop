using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ToggleTabGroup
{
    /// <summary>
    /// Diagnostic dump of the document group hosting a frame (TabGroup.Diagnostics command).
    /// </summary>
    internal static class DocumentGroupInspector
    {
        public static string Describe(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var sb = new StringBuilder();

            frame.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out object caption);
            sb.AppendLine($"Active document: {caption}");
            sb.AppendLine($"Frame type: {frame.GetType().FullName}");

            object view = ViewModel.GetView(frame);
            if (view == null)
            {
                sb.AppendLine();
                sb.AppendLine("FAILED: WindowFrame.FrameView not found.");
                return sb.ToString();
            }

            sb.AppendLine($"View: {view.GetType().Name} \"{ViewModel.GetProperty(view, "Title")}\"");
            sb.AppendLine();

            var location = DocumentGroupLocation.Find(frame);
            if (location == null)
            {
                sb.AppendLine($"FAILED: view parent is {ViewModel.GetProperty(view, "Parent")?.GetType().FullName ?? "null"}, expected DocumentGroup.");
                AppendParentChain(sb, view);
                return sb.ToString();
            }

            object group = location.Group;
            IReadOnlyList<object> groups = location.Groups;

            sb.AppendLine("=== Document group of the active document ===");
            sb.AppendLine($"Group identity (object hash): 0x{RuntimeHelpers.GetHashCode(group):X8}");
            sb.AppendLine($"DocumentGroup.Index property: {ViewModel.GetProperty(group, "Index")}");
            sb.AppendLine($"Position among visible groups: {location.Index + 1} / {groups.Count}");
            sb.AppendLine($"Container orientation: {ViewModel.GetProperty(location.Container, "Orientation")}");
            sb.AppendLine($"Hosted in: {ViewModel.GetRoot(group).GetType().Name}");
            sb.AppendLine();

            for (int i = 0; i < groups.Count; i++)
            {
                List<object> tabs = ViewModel.Enumerate(ViewModel.GetProperty(groups[i], "Children")).ToList();
                List<object> visibleTabs = tabs.Where(ViewModel.IsVisible).ToList();
                string marker = ReferenceEquals(groups[i], group) ? "  <== active" : "";
                sb.AppendLine($"Group {i + 1} ({visibleTabs.Count} tab(s), {tabs.Count - visibleTabs.Count} hidden){marker}");
                foreach (object tab in visibleTabs)
                {
                    string tabMarker = ReferenceEquals(tab, view) ? "*" : " ";
                    sb.AppendLine($"   {tabMarker} {GetTabTitle(tab)}");
                }
            }

            sb.AppendLine();
            AppendParentChain(sb, view);
            return sb.ToString();
        }

        /// <summary>
        /// Raw dump of every view in every group (including hidden ones), for the Output window.
        /// </summary>
        public static string DescribeAllViews(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            object view = ViewModel.GetView(frame);
            object container = ViewModel.GetProperty(ViewModel.GetProperty(view, "Parent"), "Parent");
            if (container == null)
                return "No DocumentGroupContainer found.";

            var sb = new StringBuilder();
            int groupNumber = 0;
            foreach (object group in ViewModel.Enumerate(ViewModel.GetProperty(container, "Children")))
            {
                groupNumber++;
                sb.AppendLine($"Group {groupNumber}: {group.GetType().Name} IsVisible={ViewModel.GetProperty(group, "IsVisible")} " +
                              $"Index={ViewModel.GetProperty(group, "Index")} hash=0x{RuntimeHelpers.GetHashCode(group):X8}");

                foreach (object tab in ViewModel.Enumerate(ViewModel.GetProperty(group, "Children")))
                {
                    object title = ViewModel.GetProperty(tab, "Title");
                    object content = ViewModel.GetProperty(tab, "Content");
                    sb.AppendLine($"   {(ReferenceEquals(tab, view) ? "*" : " ")} " +
                                  $"IsVisible={ViewModel.GetProperty(tab, "IsVisible")} " +
                                  $"IsSelected={ViewModel.GetProperty(tab, "IsSelected")} " +
                                  $"IsPinned={ViewModel.GetProperty(tab, "IsPinned")} " +
                                  $"Name=\"{ViewModel.GetProperty(tab, "Name")}\" " +
                                  $"Title=[{title?.GetType().Name ?? "null"}] \"{title}\" " +
                                  $"Content=[{content?.GetType().Name ?? "null"}]");
                }
            }
            return sb.ToString();
        }

        private static string GetTabTitle(object tab)
        {
            string title = ViewModel.GetProperty(tab, "Title")?.ToString();
            return string.IsNullOrWhiteSpace(title) ? $"(no title, Name=\"{ViewModel.GetProperty(tab, "Name")}\")" : title;
        }

        private static void AppendParentChain(StringBuilder sb, object element)
        {
            sb.AppendLine("Parent chain:");
            for (object node = element; node != null; node = ViewModel.GetProperty(node, "Parent"))
                sb.AppendLine($"   {node.GetType().FullName}");
        }
    }
}
