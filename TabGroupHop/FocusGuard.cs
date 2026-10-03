using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace TabGroupHop
{
    /// <summary>
    /// Gives the focus back to a moved tab when another group steals it right after the move.
    ///
    /// After a move, VS sometimes gives the keyboard focus back asynchronously (after our command
    /// has returned) to the selected tab of a group: the group the tab left, or even the group it
    /// arrived in. Seen with documents and with tool windows docked as tabs (e.g. Test Explorer).
    /// Armed for a short time after each move, one-shot.
    /// </summary>
    internal sealed class FocusGuard : IVsWindowFrameEvents
    {
        private static readonly TimeSpan ArmDuration = TimeSpan.FromMilliseconds(500);

        private IVsWindowFrame _frame;
        private DateTime _deadline;

        public void Arm(IVsWindowFrame frame)
        {
            _frame = frame;
            _deadline = DateTime.UtcNow + ArmDuration;
        }

        public void Disarm() => _frame = null;

        public void OnActiveFrameChanged(IVsWindowFrame oldFrame, IVsWindowFrame newFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsWindowFrame frame = _frame;
            if (frame == null || newFrame == null || ReferenceEquals(newFrame, frame))
                return;

            if (DateTime.UtcNow > _deadline)
            {
                _frame = null;
                return;
            }

            // Only react to a tab of the document well taking the focus (in any group, including
            // the moved tab's own), not to e.g. Solution Explorer or the Output window.
            if (DocumentGroupLocation.Find(newFrame) == null)
                return;

            _frame = null;
            Tracer.Log($"focus stolen by \"{Tracer.Caption(newFrame)}\", restoring \"{Tracer.Caption(frame)}\"");

            // Don't re-activate from inside the activation notification itself.
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Task.Yield();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                frame.Show();
                Tracer.LogState("focus restored", frame);
            });
        }

        public void OnFrameCreated(IVsWindowFrame frame) { }

        public void OnFrameDestroyed(IVsWindowFrame frame)
        {
            if (ReferenceEquals(frame, _frame))
                _frame = null;
        }

        public void OnFrameIsVisibleChanged(IVsWindowFrame frame, bool newIsVisible) { }

        public void OnFrameIsOnScreenChanged(IVsWindowFrame frame, bool newIsOnScreen) { }
    }
}
