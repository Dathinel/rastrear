using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace Rastrear.App;

/// <summary>
/// Diálogos nativos de Windows (IFileOpenDialog e IFileSaveDialog) vía vtable COM directa.
/// Totalmente compatibles con Native AOT y sin paquete MSIX.
/// </summary>
internal static unsafe class FileDialogs
{
    private const uint FosOverwritePrompt = 0x2;
    private const uint FosNoChangeDir = 0x8;
    private const uint FosPickFolders = 0x20;
    private const uint FosForceFileSystem = 0x40;
    private const uint FosPathMustExist = 0x800;
    private const uint FosFileMustExist = 0x1000;

    public static readonly (string Name, string Spec) VideoFilter =
        ("Archivos de video", "*.mp4;*.mov;*.webm;*.mkv;*.avi;*.ts");

    public static readonly (string Name, string Spec) CsvFilter =
        ("Archivo CSV", "*.csv");

    public static readonly (string Name, string Spec) JsonFilter =
        ("Archivo JSON", "*.json");

    public static readonly (string Name, string Spec) AllFilter =
        ("Todos los archivos", "*.*");

    public static string? OpenSingle(nint owner, string title, params (string Name, string Spec)[] filters)
    {
        IFileOpenDialog* dialog = null;
        IShellItem* item = null;
        try
        {
            if (CoCreateInstance(__uuidof<FileOpenDialog>(), null, (uint)CLSCTX.CLSCTX_INPROC_SERVER,
                __uuidof<IFileOpenDialog>(), (void**)&dialog).FAILED)
            {
                return null;
            }

            uint options;
            dialog->GetOptions(&options);
            options |= FosForceFileSystem | FosFileMustExist | FosPathMustExist | FosNoChangeDir;
            dialog->SetOptions(options);

            Configure((IFileDialog*)dialog, title, filters);

            if (dialog->Show((HWND)owner).FAILED)
            {
                return null;
            }

            if (dialog->GetResult(&item).FAILED)
            {
                return null;
            }

            return PathOf(item);
        }
        finally
        {
            if (item != null) item->Release();
            if (dialog != null) dialog->Release();
        }
    }

    public static string? SaveSingle(nint owner, string title, string defaultFileName, params (string Name, string Spec)[] filters)
    {
        IFileSaveDialog* dialog = null;
        IShellItem* item = null;
        try
        {
            if (CoCreateInstance(__uuidof<FileSaveDialog>(), null, (uint)CLSCTX.CLSCTX_INPROC_SERVER,
                __uuidof<IFileSaveDialog>(), (void**)&dialog).FAILED)
            {
                return null;
            }

            uint options;
            dialog->GetOptions(&options);
            options |= FosForceFileSystem | FosOverwritePrompt | FosPathMustExist | FosNoChangeDir;
            dialog->SetOptions(options);

            Configure((IFileDialog*)dialog, title, filters);

            fixed (char* pName = defaultFileName)
            {
                dialog->SetFileName(pName);
            }

            if (dialog->Show((HWND)owner).FAILED)
            {
                return null;
            }

            if (dialog->GetResult(&item).FAILED)
            {
                return null;
            }

            return PathOf(item);
        }
        finally
        {
            if (item != null) item->Release();
            if (dialog != null) dialog->Release();
        }
    }

    public static string? ChooseFolder(nint owner, string title)
    {
        IFileOpenDialog* dialog = null;
        IShellItem* item = null;
        try
        {
            if (CoCreateInstance(__uuidof<FileOpenDialog>(), null, (uint)CLSCTX.CLSCTX_INPROC_SERVER,
                __uuidof<IFileOpenDialog>(), (void**)&dialog).FAILED)
            {
                return null;
            }

            uint options;
            dialog->GetOptions(&options);
            options |= FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoChangeDir;
            dialog->SetOptions(options);

            fixed (char* pTitle = title)
            {
                dialog->SetTitle(pTitle);
            }

            if (dialog->Show((HWND)owner).FAILED)
            {
                return null;
            }

            if (dialog->GetResult(&item).FAILED)
            {
                return null;
            }

            return PathOf(item);
        }
        finally
        {
            if (item != null) item->Release();
            if (dialog != null) dialog->Release();
        }
    }

    private static void Configure(IFileDialog* dialog, string title, (string Name, string Spec)[] filters)
    {
        fixed (char* pTitle = title)
        {
            dialog->SetTitle(pTitle);
        }

        if (filters.Length == 0) return;

        List<GCHandle> pins = new(filters.Length * 2);
        try
        {
            COMDLG_FILTERSPEC* specs = stackalloc COMDLG_FILTERSPEC[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                GCHandle namePin = GCHandle.Alloc(filters[i].Name, GCHandleType.Pinned);
                GCHandle specPin = GCHandle.Alloc(filters[i].Spec, GCHandleType.Pinned);
                pins.Add(namePin);
                pins.Add(specPin);
                specs[i] = new COMDLG_FILTERSPEC
                {
                    pszName = (char*)namePin.AddrOfPinnedObject(),
                    pszSpec = (char*)specPin.AddrOfPinnedObject(),
                };
            }

            dialog->SetFileTypes((uint)filters.Length, specs);
            dialog->SetFileTypeIndex(1);
        }
        finally
        {
            foreach (GCHandle pin in pins)
            {
                pin.Free();
            }
        }
    }

    private static string? PathOf(IShellItem* item)
    {
        if (item == null) return null;
        char* path = null;
        try
        {
            return item->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &path).SUCCEEDED && path != null
                ? new string(path)
                : null;
        }
        finally
        {
            if (path != null)
            {
                CoTaskMemFree(path);
            }
        }
    }
}
