using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TabGroupHop
{
    /// <summary>
    /// The "Tab Group Hop" pane of the Output window (copyable, unlike a message box).
    /// </summary>
    internal static class OutputPane
    {
        private static readonly Guid PaneGuid = new Guid("bdf75cb3-d073-4d2f-b5bb-d9285e78b361");

        public static void Write(string text, bool activate)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow))
                return;

            Guid paneGuid = PaneGuid;
            if (outputWindow.GetPane(ref paneGuid, out IVsOutputWindowPane pane) != VSConstants.S_OK || pane == null)
            {
                outputWindow.CreatePane(ref paneGuid, "Tab Group Hop", fInitVisible: 1, fClearWithSolution: 0);
                outputWindow.GetPane(ref paneGuid, out pane);
            }
            if (pane == null)
                return;

            pane.OutputStringThreadSafe(text + Environment.NewLine);
            if (activate)
            {
                ShowOutputWindow();
                pane.Activate();
            }
        }

        /// <summary>
        /// Reports an exception that would otherwise be lost (fire-and-forget work, timer callbacks).
        /// </summary>
        public static void WriteError(string context, Exception ex)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Write($"{DateTime.Now:HH:mm:ss.fff}  Tab Group Hop error ({context}): {ex}", activate: false);
        }

        /// <summary>Opens the Output tool window if it is closed (Activate only selects the pane).</summary>
        private static void ShowOutputWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell))
                return;

            Guid outputWindowGuid = new Guid(ToolWindowGuids80.Outputwindow);
            if (ErrorHandler.Succeeded(shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref outputWindowGuid, out IVsWindowFrame frame)))
                frame?.ShowNoActivate();
        }
    }
}
