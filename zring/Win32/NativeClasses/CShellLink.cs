using System.Runtime.InteropServices;
using Zring.Win32.NativeConstants;

namespace Zring.Win32.NativeClasses
{
    [ComImport]
    [ClassInterface(ClassInterfaceType.None)]
    [Guid(Win32Consts.CLSID_ShellLink)]
    internal class CShellLink { }
}
