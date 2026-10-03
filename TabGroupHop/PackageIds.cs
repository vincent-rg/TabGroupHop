using System;

namespace TabGroupHop
{
    internal static class PackageGuids
    {
        public const string PackageString = "dfd18762-e92a-4c5c-9443-8a0264f3c867";
        public const string CommandSetString = "188b3ead-ccd9-43c0-bd7d-a337cd2ad2ed";

        public static readonly Guid CommandSet = new Guid(CommandSetString);
    }

    internal static class PackageIds
    {
        public const int MoveToNextCommand = 0x0100;
        public const int FocusNextCommand = 0x0101;
        public const int DiagnosticsCommand = 0x0102;
        public const int ToggleTraceCommand = 0x0105;
    }
}
