using System;
using System.ComponentModel.Design;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace TabGroupHop
{
    /// <summary>
    /// TabGroup.MoveToNext, TabGroup.FocusNext, TabGroup.Diagnostics and TabGroup.ToggleTrace.
    /// Only document groups of the main window are handled (no floating windows).
    /// </summary>
    internal sealed class TabGroupCommands
    {
        private readonly FocusGuard _focusGuard = new FocusGuard();

        private TabGroupCommands(OleMenuCommandService commandService, IVsUIShell7 shell)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            shell?.AdviseWindowFrameEvents(_focusGuard);

            commandService.AddCommand(new MenuCommand(MoveToNext, new CommandID(PackageGuids.CommandSet, PackageIds.MoveToNextCommand)));
            commandService.AddCommand(new MenuCommand(FocusNext, new CommandID(PackageGuids.CommandSet, PackageIds.FocusNextCommand)));
            commandService.AddCommand(new MenuCommand(Diagnostics, new CommandID(PackageGuids.CommandSet, PackageIds.DiagnosticsCommand)));

            var traceCommand = new OleMenuCommand(ToggleTrace, new CommandID(PackageGuids.CommandSet, PackageIds.ToggleTraceCommand));
            traceCommand.BeforeQueryStatus += (s, e) => ((OleMenuCommand)s).Checked = Tracer.Enabled;
            commandService.AddCommand(traceCommand);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            Assumes.Present(commandService);
            var shell = await package.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell7;
            _ = new TabGroupCommands(commandService, shell);
        }

        /// <summary>
        /// Moves the active document to the next group, wrapping from the last group to the first,
        /// and keeps the focus on it. With a single group, creates a new vertical group.
        /// </summary>
        private void MoveToNext(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Tracer.Log("=== MoveToNext ===");
            _focusGuard.Disarm();
            if (!TryGetActiveDocument(out IVsWindowFrame frame, out DocumentGroupLocation location))
                return;

            Tracer.LogState("start", frame);
            frame.Show();
            Tracer.LogState("after initial Show", frame);

            bool ok;
            if (location.Groups.Count == 1)
            {
                ok = VsCommandRunner.TryExecute(VsCommandRunner.NewVerticalTabGroup);
                Tracer.LogState($"NewVerticalTabGroup ok={ok}", frame);
                if (!ok)
                    SetStatusBarText("Tab Group Hop: a new group needs at least two documents in the current group.");
            }
            else if (ViewModel.TryDockInto(location.Groups[(location.Index + 1) % location.Groups.Count], location.View))
            {
                // Dock the tab straight into the next group (first position in its tab strip),
                // wrapping from the last group to the first without crossing the groups in between.
                ok = true;
                Tracer.LogState("DockOperations.Dock into next group", frame);
            }
            else if (location.Index < location.Groups.Count - 1)
            {
                // Fallback: native command (no wrap).
                ok = VsCommandRunner.TryExecute(VsCommandRunner.MoveToNextTabGroup);
                Tracer.LogState($"MoveToNextTabGroup ok={ok}", frame);
            }
            else
            {
                // Fallback for the last group: move back step by step to the first group.
                // Groups emptied on the way are removed by VS.
                ok = true;
                for (int guard = location.Groups.Count; ok && guard > 0; guard--)
                {
                    var current = DocumentGroupLocation.Find(frame);
                    if (current == null || current.Index <= 0)
                        break;

                    frame.Show();
                    ok = VsCommandRunner.TryExecute(VsCommandRunner.MoveToPreviousTabGroup);
                    Tracer.LogState($"MoveToPreviousTabGroup ok={ok}", frame);
                }
            }

            frame.Show();
            if (ok)
                _focusGuard.Arm(frame);
            Tracer.LogStateNowAndLater("after final Show", frame);

            if (!ok && location.Groups.Count > 1)
                SetStatusBarText("Tab Group Hop: the document could not be moved.");
        }

        /// <summary>
        /// Focuses the active tab of the next group, wrapping from the last group to the first.
        /// Does nothing with a single group.
        /// </summary>
        private void FocusNext(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Tracer.Log("=== FocusNext ===");
            _focusGuard.Disarm();
            if (!TryGetActiveDocument(out IVsWindowFrame frame, out DocumentGroupLocation location))
                return;

            Tracer.LogState("start", frame);
            if (location.Groups.Count < 2)
                return;

            object nextGroup = location.Groups[(location.Index + 1) % location.Groups.Count];
            object tab = DocumentGroupLocation.GetActiveTab(nextGroup);
            IVsWindowFrame target = FindFrame(tab);
            if (target == null)
            {
                SetStatusBarText("Tab Group Hop: no window found for the next group's tab.");
                return;
            }

            target.Show();
            Tracer.LogStateNowAndLater("after Show", target);
        }

        private void ToggleTrace(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Tracer.SetEnabled(!Tracer.Enabled);
        }

        private void Diagnostics(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string message;
            string details = null;
            try
            {
                var frame = GetActiveTabFrame();
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

            OutputPane.Write($"===== {DateTime.Now:HH:mm:ss} ====={Environment.NewLine}{message}{Environment.NewLine}" +
                             $"--- All views (including hidden) ---{Environment.NewLine}{details}", activate: true);
        }

        private static bool TryGetActiveDocument(out IVsWindowFrame frame, out DocumentGroupLocation location)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            location = null;
            frame = GetActiveTabFrame();
            if (frame == null)
                return false;

            location = DocumentGroupLocation.Find(frame);
            if (location == null || location.Index < 0)
            {
                SetStatusBarText("Tab Group Hop: the active document is not in a tab group.");
                return false;
            }
            if (!location.IsInMainWindow)
            {
                SetStatusBarText("Tab Group Hop: floating windows are not supported.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// The frame of the active tab in the document well:
        /// the active window if it is hosted in a tab group (a document, or a tool window
        /// docked as a tab, like Test Explorer), otherwise the last active document
        /// (e.g. when focus is in Solution Explorer).
        /// </summary>
        internal static IVsWindowFrame GetActiveTabFrame()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var monitorSelection = (IVsMonitorSelection)Package.GetGlobalService(typeof(SVsShellMonitorSelection));
            if (monitorSelection == null)
                return null;

            monitorSelection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out object window);
            if (window is IVsWindowFrame windowFrame && DocumentGroupLocation.Find(windowFrame) != null)
                return windowFrame;

            monitorSelection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object document);
            return document as IVsWindowFrame;
        }

        /// <summary>The frame (document or tool window) whose FrameView is the given View.</summary>
        private static IVsWindowFrame FindFrame(object view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (view == null || !(Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell))
                return null;

            if (ErrorHandler.Succeeded(shell.GetDocumentWindowEnum(out IEnumWindowFrames documents))
                && FindFrame(documents, view) is IVsWindowFrame document)
            {
                return document;
            }

            if (ErrorHandler.Succeeded(shell.GetToolWindowEnum(out IEnumWindowFrames toolWindows)))
                return FindFrame(toolWindows, view);

            return null;
        }

        private static IVsWindowFrame FindFrame(IEnumWindowFrames frames, object view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (frames == null)
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
    }
}
