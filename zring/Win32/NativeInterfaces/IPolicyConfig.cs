using System.Runtime.InteropServices;
using Zring.Win32.NativeConstants;
using Zring.Win32.NativeEnums;
using Zring.Win32.NativeStructs;

namespace Zring.Win32.NativeInterfaces
{
    /// <summary>
    /// Undocumented Audio endpoint interface
    /// https://github.com/File-New-Project/EarTrumpet
    /// https://github.com/SomeProgrammerGuy/Powershell-Default-Audio-Device-Changer/blob/master/Set-DefaultAudioDevice.ps1
    /// </summary>
    [Guid(Win32Consts.IID_IPolicyConfig), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig]
        HRESULT GetMixFormat();

        [PreserveSig]
        HRESULT GetDeviceFormat();

        [PreserveSig]
        HRESULT ResetDeviceFormat();

        [PreserveSig]
        HRESULT SetDeviceFormat();

        [PreserveSig]
        HRESULT GetProcessingPeriod();

        [PreserveSig]
        HRESULT SetProcessingPeriod();

        [PreserveSig]
        HRESULT GetShareMode();

        [PreserveSig]
        HRESULT SetShareMode();

        [PreserveSig]
        HRESULT GetPropertyValue();

        [PreserveSig]
        HRESULT SetPropertyValue();

        [PreserveSig]
        HRESULT SetDefaultEndpoint([In,MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, [In,MarshalAs(UnmanagedType.U4)] ERole role);

        [PreserveSig]
        HRESULT SetEndpointVisibility();
    }
   
}
