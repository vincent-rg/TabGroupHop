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
                pane.Activate();
        }
    }
}
