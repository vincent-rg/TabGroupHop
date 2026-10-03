using System;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TabGroupHop
{
    /// <summary>
    /// Opt-in trace (TabGroup.ToggleTrace) to the Output pane, to debug focus issues:
    /// command steps, every active-frame change reported by the shell, and the state
    /// a little after the command returns (focus changes can happen asynchronously).
    /// </summary>
    internal sealed class Tracer : IVsWindowFrameEvents
    {
        private static Tracer _instance;
        private static uint _eventsCookie;

        public static bool Enabled => _instance != null;

        public static void SetEnabled(bool enabled)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (enabled == Enabled)
                return;

            var shell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell7;
            if (enabled)
            {
                _instance = new Tracer();
                if (shell != null)
                    _eventsCookie = shell.AdviseWindowFrameEvents(_instance);
                OutputPane.Write("----- Tab Group Hop trace ON -----", activate: true);
                OutputPane.Write(DocumentGroupInspector.DescribeGroups(TabGroupCommands.GetActiveTabFrame()), activate: false);
            }
            else
            {
                if (shell != null && _eventsCookie != 0)
                    shell.UnadviseWindowFrameEvents(_eventsCookie);
                _eventsCookie = 0;
                _instance = null;
                OutputPane.Write("----- Tab Group Hop trace OFF -----", activate: true);
            }
        }

        public static void Log(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Enabled)
                OutputPane.Write($"{DateTime.Now:HH:mm:ss.fff}  {message}", activate: false);
        }

        /// <summary>Logs the state now, then again once the dispatcher is idle and after 300 ms.</summary>
        public static void LogStateNowAndLater(string label, IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!Enabled)
                return;

            LogState(label, frame);

            _ = ThreadHelper.JoinableTaskFactory.StartOnIdle(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                LogState(label + " [idle]", frame);
            });

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                timer.Stop();
                LogState(label + " [+300ms]", frame);
            };
            timer.Start();
        }

        public static void LogState(string label, IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!Enabled)
                return;

            var location = frame != null ? DocumentGroupLocation.Find(frame) : null;
            string where = location != null ? $"group {location.Index + 1}/{location.Groups.Count}" : "no group";

            var monitorSelection = (IVsMonitorSelection)Package.GetGlobalService(typeof(SVsShellMonitorSelection));
            object active = null;
            monitorSelection?.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out active);
            var activeFrame = active as IVsWindowFrame;
            var activeLocation = activeFrame != null ? DocumentGroupLocation.Find(activeFrame) : null;
            string activeWhere = activeLocation != null ? $"group {activeLocation.Index + 1}" : "not in a group";

            Log($"{label}: frame \"{Caption(frame)}\" in {where} | active window \"{Caption(activeFrame)}\" ({activeWhere})" +
                $" | keyboard focus {Keyboard.FocusedElement?.GetType().Name ?? "null"}");
        }

        public static string Caption(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (frame == null)
                return "null";
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out object caption);
            return caption?.ToString() ?? "?";
        }

        public void OnActiveFrameChanged(IVsWindowFrame oldFrame, IVsWindowFrame newFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log($"  (shell) active frame: \"{Caption(oldFrame)}\" -> \"{Caption(newFrame)}\"");
        }

        public void OnFrameCreated(IVsWindowFrame frame) { }

        public void OnFrameDestroyed(IVsWindowFrame frame) { }

        public void OnFrameIsVisibleChanged(IVsWindowFrame frame, bool newIsVisible) { }

        public void OnFrameIsOnScreenChanged(IVsWindowFrame frame, bool newIsOnScreen) { }
    }
}
