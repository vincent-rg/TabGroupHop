using System;
using System.ComponentModel.Design;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace ToggleTabGroup
{
    /// <summary>
    /// TabGroup.MoveToNext, TabGroup.FocusNext and TabGroup.Diagnostics.
    /// Only document groups of the main window are handled (no floating windows).
    /// </summary>
    internal sealed class TabGroupCommands
    {
        private static readonly Guid OutputPaneGuid = new Guid("bdf75cb3-d073-4d2f-b5bb-d9285e78b361");

        private readonly AsyncPackage _package;

        private TabGroupCommands(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;

            commandService.AddCommand(new MenuCommand(MoveToNext, new CommandID(PackageGuids.CommandSet, PackageIds.MoveToNextCommand)));
            commandService.AddCommand(new MenuCommand(FocusNext, new CommandID(PackageGuids.CommandSet, PackageIds.FocusNextCommand)));
            commandService.AddCommand(new MenuCommand(Diagnostics, new CommandID(PackageGuids.CommandSet, PackageIds.DiagnosticsCommand)));
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            Assumes.Present(commandService);
            _ = new TabGroupCommands(package, commandService);
        }

        /// <summary>
        /// Moves the active document to the next group, wrapping from the last group to the first,
        /// and keeps the focus on it. With a single group, creates a new vertical group.
        /// </summary>
        private void MoveToNext(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!TryGetActiveDocument(out IVsWindowFrame frame, out DocumentGroupLocation location))
                return;

            frame.Show();

            bool ok;
            if (location.Groups.Count == 1)
            {
                ok = VsCommandRunner.TryExecute(VsCommandRunner.NewVerticalTabGroup);
                if (!ok)
                    SetStatusBarText("Toggle Tab Group: a new group needs at least two documents in the current group.");
            }
            else if (location.Index < location.Groups.Count - 1)
            {
                ok = VsCommandRunner.TryExecute(VsCommandRunner.MoveToNextTabGroup);
            }
            else
            {
                // Last group: wrap around by moving back step by step to the first group
                // (the native command doesn't wrap). Groups emptied on the way are removed by VS.
                ok = true;
                for (int guard = location.Groups.Count; ok && guard > 0; guard--)
                {
                    var current = DocumentGroupLocation.Find(frame);
                    if (current == null || current.Index <= 0)
                        break;

                    frame.Show();
                    ok = VsCommandRunner.TryExecute(VsCommandRunner.MoveToPreviousTabGroup);
                }
            }

            frame.Show();

            if (!ok && location.Groups.Count > 1)
                SetStatusBarText("Toggle Tab Group: the document could not be moved.");
        }

        /// <summary>
        /// Focuses the active tab of the next group, wrapping from the last group to the first.
        /// Does nothing with a single group.
        /// </summary>
        private void FocusNext(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!TryGetActiveDocument(out _, out DocumentGroupLocation location))
                return;

            if (location.Groups.Count < 2)
                return;

            object nextGroup = location.Groups[(location.Index + 1) % location.Groups.Count];
            object tab = DocumentGroupLocation.GetActiveTab(nextGroup);
            IVsWindowFrame target = FindDocumentFrame(tab);
            if (target == null)
            {
                SetStatusBarText("Toggle Tab Group: no document found in the next group.");
                return;
            }

            target.Show();
        }

        private void Diagnostics(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string message;
            string details = null;
            try
            {
                var frame = GetActiveDocumentFrame();
                if (frame == null)
                {
                    message = "No active document.";
                }
                else
                {
                    message = DocumentGroupInspector.Describe(frame);
                    details = DocumentGroupInspector.DescribeAllViews(frame);
                }
            }
            catch (Exception ex)
            {
                message = "Error while inspecting the document group:\n\n" + ex;
            }

            WriteToOutputPane(message + Environment.NewLine + "--- All views (including hidden) ---" + Environment.NewLine + details);

            VsShellUtilities.ShowMessageBox(
                _package,
                message,
                "Toggle Tab Group — diagnostics",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private static bool TryGetActiveDocument(out IVsWindowFrame frame, out DocumentGroupLocation location)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            location = null;
            frame = GetActiveDocumentFrame();
            if (frame == null)
                return false;

            location = DocumentGroupLocation.Find(frame);
            if (location == null || location.Index < 0)
            {
                SetStatusBarText("Toggle Tab Group: the active document is not in a tab group.");
                return false;
            }
            if (!location.IsInMainWindow)
            {
                SetStatusBarText("Toggle Tab Group: floating windows are not supported.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// The active document frame, even when focus is in a tool window or a menu.
        /// </summary>
        private static IVsWindowFrame GetActiveDocumentFrame()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var monitorSelection = (IVsMonitorSelection)Package.GetGlobalService(typeof(SVsShellMonitorSelection));
            if (monitorSelection == null)
                return null;

            monitorSelection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object value);
            return value as IVsWindowFrame;
        }

        /// <summary>The document frame whose FrameView is the given View.</summary>
        private static IVsWindowFrame FindDocumentFrame(object view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (view == null || !(Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell))
                return null;

            if (ErrorHandler.Failed(shell.GetDocumentWindowEnum(out IEnumWindowFrames frames)) || frames == null)
                return null;

            var buffer = new IVsWindowFrame[1];
            while (frames.Next(1, buffer, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                if (ReferenceEquals(ViewModel.GetView(buffer[0]), view))
                    return buffer[0];
            }
            return null;
        }

        private static void SetStatusBarText(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                statusBar.SetText(text);
        }

        /// <summary>
        /// Writes to a "Toggle Tab Group" pane of the Output window (copyable, unlike the message box).
        /// </summary>
        private static void WriteToOutputPane(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow))
                return;

            Guid paneGuid = OutputPaneGuid;
            if (outputWindow.GetPane(ref paneGuid, out IVsOutputWindowPane pane) != VSConstants.S_OK || pane == null)
            {
                outputWindow.CreatePane(ref paneGuid, "Toggle Tab Group", fInitVisible: 1, fClearWithSolution: 0);
                outputWindow.GetPane(ref paneGuid, out pane);
            }
            if (pane == null)
                return;

            pane.OutputStringThreadSafe($"===== {DateTime.Now:HH:mm:ss} ====={Environment.NewLine}{text}{Environment.NewLine}");
            pane.Activate();
        }
    }
}
