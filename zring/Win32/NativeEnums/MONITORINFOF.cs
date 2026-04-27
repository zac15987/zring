using System;
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo
// ReSharper disable UnusedMember.Global
namespace Zring.Win32.NativeEnums;

/// <summary>
/// A set of flags that represent attributes of the display monitor.
/// </summary>
[Flags]
internal enum MONITORINFOF
{
    /// <summary>
    /// This is the primary display monitor.
    /// </summary>
    PRIMARY = 0x1
}