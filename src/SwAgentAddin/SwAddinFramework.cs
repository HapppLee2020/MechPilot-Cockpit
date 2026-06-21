using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwAgentAddin
{
    /// <summary>
    /// ISwAddin — SolidWorks Add-in 必须实现的 COM 接口
    /// GUID: 28F606F1-DBB6-4a36-ABB2-8B5066B1FE16 (SW 官方固定值)
    /// </summary>
    [ComImport]
    [Guid("28F606F1-DBB6-4a36-ABB2-8B5066B1FE16")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface ISwAddin
    {
        [DispId(1)]
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool ConnectToSW([In, MarshalAs(UnmanagedType.IDispatch)] object ThisSW, int cookie);

        [DispId(2)]
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool DisconnectFromSW();
    }
}
