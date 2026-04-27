using System.Runtime.InteropServices;
using Zring.Win32.NativeConstants;

namespace Zring.Win32.NativeInterfaces;

[ComImport]
[ClassInterface(ClassInterfaceType.None)]
[Guid(Win32Consts.CLSID_AutomaticDestinationList)]
internal class CAutomaticDestinationList { }