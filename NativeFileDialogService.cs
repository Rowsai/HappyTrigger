using System;
using System.Runtime.InteropServices;

namespace HappyTrigger;

public static class NativeFileDialogService
{
    private const int MaxPathChars = 32768;

    private const int OfnReadOnly = 0x00000001;
    private const int OfnOverwritePrompt = 0x00000002;
    private const int OfnHideReadOnly = 0x00000004;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnShowHelp = 0x00000010;
    private const int OfnEnableHook = 0x00000020;
    private const int OfnEnableTemplate = 0x00000040;
    private const int OfnEnableTemplateHandle = 0x00000080;
    private const int OfnNoValidate = 0x00000100;
    private const int OfnAllowMultiSelect = 0x00000200;
    private const int OfnExtensionDifferent = 0x00000400;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnCreatePrompt = 0x00002000;
    private const int OfnShareAware = 0x00004000;
    private const int OfnNoReadOnlyReturn = 0x00008000;
    private const int OfnNoTestFileCreate = 0x00010000;
    private const int OfnNoNetworkButton = 0x00020000;
    private const int OfnNoLongNames = 0x00040000;
    private const int OfnExplorer = 0x00080000;
    private const int OfnNoDereferenceLinks = 0x00100000;
    private const int OfnLongNames = 0x00200000;
    private const int OfnEnableIncludeNotify = 0x00400000;
    private const int OfnEnableSizing = 0x00800000;
    private const int OfnDontAddToRecent = 0x02000000;
    private const int OfnForceShowHidden = 0x10000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    public static bool TryOpenImageFile(out string filePath)
    {
        filePath = string.Empty;

        var fileBuffer = IntPtr.Zero;
        var filterBuffer = IntPtr.Zero;
        var titleBuffer = IntPtr.Zero;
        var defaultExtBuffer = IntPtr.Zero;

        try
        {
            fileBuffer = Marshal.AllocHGlobal(MaxPathChars * sizeof(char));
            ZeroMemory(fileBuffer, MaxPathChars * sizeof(char));

            filterBuffer = Marshal.StringToHGlobalUni(
                "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp\0" +
                "すべてのファイル (*.*)\0*.*\0\0");

            titleBuffer = Marshal.StringToHGlobalUni("画像を選択");
            defaultExtBuffer = Marshal.StringToHGlobalUni("png");

            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = IntPtr.Zero,
                hInstance = IntPtr.Zero,
                lpstrFilter = filterBuffer,
                lpstrFile = fileBuffer,
                nMaxFile = MaxPathChars,
                lpstrTitle = titleBuffer,
                lpstrDefExt = defaultExtBuffer,
                nFilterIndex = 1,
                Flags =
                    OfnExplorer |
                    OfnFileMustExist |
                    OfnPathMustExist |
                    OfnNoChangeDir |
                    OfnHideReadOnly |
                    OfnEnableSizing
            };

            if (!GetOpenFileName(ref ofn))
            {
                return false;
            }

            filePath = Marshal.PtrToStringUni(fileBuffer) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(filePath);
        }
        finally
        {
            if (fileBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileBuffer);
            }

            if (filterBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterBuffer);
            }

            if (titleBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(titleBuffer);
            }

            if (defaultExtBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(defaultExtBuffer);
            }
        }
    }

    private static unsafe void ZeroMemory(IntPtr ptr, int byteCount)
    {
        new Span<byte>(ptr.ToPointer(), byteCount).Clear();
    }
}
