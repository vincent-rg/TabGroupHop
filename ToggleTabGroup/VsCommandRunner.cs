using System;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ToggleTabGroup
{
    /// <summary>
    /// Runs a native VS command synchronously, by canonical (non-localized) name.
    /// </summary>
    internal static class VsCommandRunner
    {
        public const string NewVerticalTabGroup = "Window.NewVerticalTabGroup";
        public const string MoveToNextTabGroup = "Window.MoveToNextTabGroup";
        public const string MoveToPreviousTabGroup = "Window.MoveToPreviousTabGroup";

        /// <summary>Returns false if the command doesn't exist or is currently disabled.</summary>
        public static bool TryExecute(string canonicalName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE)Package.GetGlobalService(typeof(SDTE));
            var dispatcher = (IOleCommandTarget)Package.GetGlobalService(typeof(SUIHostCommandDispatcher));
            if (dte == null || dispatcher == null)
                return false;

            Command command;
            try
            {
                command = dte.Commands.Item(canonicalName);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var group = new Guid(command.Guid);
            uint id = (uint)command.ID;

            var status = new[] { new OLECMD { cmdID = id } };
            if (ErrorHandler.Failed(dispatcher.QueryStatus(ref group, 1, status, IntPtr.Zero))
                || (status[0].cmdf & (uint)OLECMDF.OLECMDF_ENABLED) == 0)
            {
                return false;
            }

            return ErrorHandler.Succeeded(dispatcher.Exec(ref group, id, 0, IntPtr.Zero, IntPtr.Zero));
        }
    }
}
