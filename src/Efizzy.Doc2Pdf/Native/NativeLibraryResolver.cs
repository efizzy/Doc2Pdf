using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Efizzy.Doc2Pdf.Native;

internal static class NativeLibraryResolver
{
    private static bool _initialized;
    private static readonly object _lock = new();
    private static IntPtr _loadedHandle = IntPtr.Zero;

    public static void Initialize()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

#if NETCOREAPP || NET5_0_OR_GREATER || NET8_0_OR_GREATER
            NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveDllImport);
#else
            // Fallback for .NET Standard 2.0 / .NET Framework: manually pre-load the native library into process space
            PreloadLibraryNetStandard();
#endif
            _initialized = true;
        }
    }

#if NETCOREAPP || NET5_0_OR_GREATER || NET8_0_OR_GREATER
    private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != NativeMethods.LibraryName)
            return IntPtr.Zero;

        string baseDir = AppContext.BaseDirectory;
        string fileName = GetPlatformLibraryFileName();
        var candidateRids = GetCandidateRuntimeIds();

        // 1. Check directly in application base directory
        string directPath = Path.Combine(baseDir, fileName);
        if (File.Exists(directPath) && NativeLibrary.TryLoad(directPath, out IntPtr handle))
            return handle;

        // 2. Check candidate NuGet runtimes: runtimes/{rid}/native/{fileName}
        foreach (var rid in candidateRids)
        {
            string nugetPath = Path.Combine(baseDir, "runtimes", rid, "native", fileName);
            if (File.Exists(nugetPath) && NativeLibrary.TryLoad(nugetPath, out handle))
                return handle;
        }

        // 3. Search upward from base directory for local workspace / dev build artifacts
        var currentDir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && currentDir != null; i++)
        {
            foreach (var rid in candidateRids)
            {
                string packageCandidate = Path.Combine(currentDir.FullName, "src", "Efizzy.Doc2Pdf", "runtimes", rid, "native", fileName);
                if (File.Exists(packageCandidate) && NativeLibrary.TryLoad(packageCandidate, out handle))
                    return handle;
            }

            // Check cargo target release directory
            string targetRelease = Path.Combine(currentDir.FullName, "src", "native", "core", "target", "release", fileName);
            if (File.Exists(targetRelease) && NativeLibrary.TryLoad(targetRelease, out handle))
                return handle;

            // Check cargo target debug directory
            string targetDebug = Path.Combine(currentDir.FullName, "src", "native", "core", "target", "debug", fileName);
            if (File.Exists(targetDebug) && NativeLibrary.TryLoad(targetDebug, out handle))
                return handle;

            currentDir = currentDir.Parent;
        }

        // 4. Fallback to OS default search path
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out handle))
            return handle;

        return IntPtr.Zero;
    }
#else
    private static void PreloadLibraryNetStandard()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
        string fileName = GetPlatformLibraryFileName();
        var candidateRids = GetCandidateRuntimeIds();

        var candidates = new List<string>();

        // Application base directory
        candidates.Add(Path.Combine(baseDir, fileName));

        // Runtimes candidate paths
        foreach (var rid in candidateRids)
        {
            candidates.Add(Path.Combine(baseDir, "runtimes", rid, "native", fileName));
        }

        // Search parent directories
        var currentDir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && currentDir != null; i++)
        {
            foreach (var rid in candidateRids)
            {
                candidates.Add(Path.Combine(currentDir.FullName, "src", "Efizzy.Doc2Pdf", "runtimes", rid, "native", fileName));
            }
            candidates.Add(Path.Combine(currentDir.FullName, "src", "native", "core", "target", "release", fileName));
            candidates.Add(Path.Combine(currentDir.FullName, "src", "native", "core", "target", "debug", fileName));
            currentDir = currentDir.Parent;
        }

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                _loadedHandle = LoadNativeLibraryInternal(path);
                if (_loadedHandle != IntPtr.Zero)
                    return;
            }
        }
    }

    private static IntPtr LoadNativeLibraryInternal(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Win32.LoadLibraryExW(path, IntPtr.Zero, Win32.LOAD_WITH_ALTERED_SEARCH_PATH);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // RTLD_NOW = 2, RTLD_GLOBAL = 0x100
                return Posix.dlopen(path, Posix.RTLD_NOW | Posix.RTLD_GLOBAL);
            }
        }
        catch
        {
            // Silently fall through to standard CLR DllImport resolution
        }
        return IntPtr.Zero;
    }

    private static class Win32
    {
        public const int LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, int dwFlags);
    }

    private static class Posix
    {
        public const int RTLD_NOW = 2;
        public const int RTLD_GLOBAL = 0x100;

        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen_linux(string filename, int flag);

        [DllImport("libc.so", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen_libc(string filename, int flag);

        [DllImport("libSystem.dylib", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen_osx(string filename, int flag);

        public static IntPtr dlopen(string filename, int flag)
        {
            try { return dlopen_linux(filename, flag); } catch { }
            try { return dlopen_libc(filename, flag); } catch { }
            try { return dlopen_osx(filename, flag); } catch { }
            return IntPtr.Zero;
        }
    }
#endif

    public static string GetPlatformLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "doc2pdf_native.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libdoc2pdf_native.dylib";
        return "libdoc2pdf_native.so";
    }

    public static List<string> GetCandidateRuntimeIds()
    {
        var rids = new List<string>();
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            rids.Add($"win-{arch}");
            if (arch == "arm64")
                rids.Add("win-x64"); // Windows 11 ARM64 can run x64 emulation
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            bool isMusl = IsMuslEnvironment();
            if (isMusl)
            {
                rids.Add($"linux-musl-{arch}");
                rids.Add($"linux-{arch}");
            }
            else
            {
                rids.Add($"linux-{arch}");
                rids.Add($"linux-musl-{arch}");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            rids.Add($"osx-{arch}");
            if (arch == "arm64")
                rids.Add("osx-x64"); // Rosetta 2 compatibility
        }

        return rids;
    }

    private static bool IsMuslEnvironment()
    {
        try
        {
            if (File.Exists("/etc/alpine-release"))
                return true;

            if (Directory.Exists("/lib"))
            {
                foreach (var file in Directory.GetFiles("/lib", "ld-musl*"))
                    return true;
            }
        }
        catch
        {
            // Ignore filesystem probe errors on non-Linux
        }
        return false;
    }
}
