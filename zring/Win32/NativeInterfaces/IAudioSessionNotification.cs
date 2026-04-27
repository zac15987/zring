using System.Runtime.InteropServices;
using Zring.Win32.NativeConstants;
using Zring.Win32.NativeStructs;
// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo
// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming


namespace Zring.Win32.NativeInterfaces;

/// <summary>
/// The IAudioSessionNotification interface provides notification when an audio session is created.
/// </summary>
[ComImport]
[Guid(Win32Consts.IID_IAudioSessionNotification)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    /// <summary>
    /// The OnSessionCreated method notifies the registered processes that the audio session has been created.
    /// </summary>
    /// <param name="NewSession">Pointer to the IAudioSessionControl interface of the audio session that was created.</param>
    /// <returns>If the method succeeds, it returns S_OK.</returns>
    [PreserveSig] HRESULT OnSessionCreated([MarshalAs(UnmanagedType.Interface)] IAudioSessionControl NewSession);
}