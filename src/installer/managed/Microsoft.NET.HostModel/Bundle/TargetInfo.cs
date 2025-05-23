// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.NET.HostModel.MachO;
using Microsoft.NET.HostModel.AppHost;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.NET.HostModel.Bundle
{
    /// <summary>
    /// TargetInfo: Information about the target for which the single-file bundle is built.
    ///
    /// Currently the TargetInfo only tracks:
    ///   - the target operating system
    ///   - the target architecture
    ///   - the target framework
    ///   - the default options for this target
    ///   - the assembly alignment for this target
    /// </summary>

    public class TargetInfo
    {
        public readonly OSPlatform OS;
        public readonly Architecture Arch;
        public readonly Version FrameworkVersion;
        public readonly uint BundleMajorVersion;
        public readonly BundleOptions DefaultOptions;
        public readonly int AssemblyAlignment;

        public TargetInfo(OSPlatform? os, Architecture? arch, Version targetFrameworkVersion)
        {
            OS = os ?? HostOS;
            Arch = arch ?? RuntimeInformation.OSArchitecture;
            FrameworkVersion = targetFrameworkVersion ?? Environment.Version;

            if (FrameworkVersion.Major >= 6)
            {
                BundleMajorVersion = 6u;
                DefaultOptions = BundleOptions.None;
            }
            else if (FrameworkVersion.Major == 5)
            {
                BundleMajorVersion = 2u;
                DefaultOptions = BundleOptions.None;
            }
            else if (FrameworkVersion.Major == 3)
            {
                BundleMajorVersion = 1u;
                DefaultOptions = BundleOptions.BundleAllContent;
            }
            else
            {
                throw new ArgumentException($"Invalid input: Unsupported Target Framework Version {targetFrameworkVersion}");
            }

            if (IsWindows)
            {
                // We align assemblies in the bundle at 4K - per requirements of memory mapping API (MapViewOfFile3, et al).
                // This is only necessary for R2R assemblies, but we do it for all assemblies for simplicity.
                AssemblyAlignment = 4096;
            }
            else if (Arch == Architecture.LoongArch64)
            {
                // We align assemblies in the bundle at 16K so that we can use mmap on Unix without changing the page alignment of LOONGARCH64 R2R code.
                // This is only necessary for R2R assemblies, but we do it for all assemblies for simplicity.
                AssemblyAlignment = 16384;
            }
            else if (Arch == Architecture.Arm64)
            {
                // We align assemblies in the bundle at 4K so that we can use mmap on Unix without changing the page alignment of ARM64 R2R code.
                // This is only necessary for R2R assemblies, but we do it for all assemblies for simplicity.
                // See https://github.com/dotnet/runtime/issues/41832.
                AssemblyAlignment = 4096;
            }
            else
            {
                // Otherwise, assemblies are 64 bytes aligned, so that their sections can be memory-mapped cache aligned.
                AssemblyAlignment = 64;
            }
        }

        public bool IsNativeBinary(string filePath)
        {
            return IsWindows ? PEUtils.IsPEImage(filePath) : IsOSX ? MachObjectFile.IsMachOImage(filePath) : ElfUtils.IsElfImage(filePath);
        }

        public string GetAssemblyName(string hostName)
        {
            // This logic to calculate assembly name from hostName should be removed (and probably moved to test helpers)
            // once the SDK in the correct assembly name.
            return (IsWindows ? Path.GetFileNameWithoutExtension(hostName) : hostName);
        }

        public override string ToString()
        {
            string os = IsWindows ? "win" : OS.ToString().ToLowerInvariant();
            string arch = Arch.ToString().ToLowerInvariant();
            return $"OS: {os} Arch: {arch} FrameworkVersion: {FrameworkVersion}";
        }

        private static OSPlatform? _hostOS;
        private static OSPlatform HostOS => _hostOS ??= RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
            RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) ? OSPlatform.FreeBSD :
            RuntimeInformation.IsOSPlatform(OSPlatform.Illumos) ? OSPlatform.Illumos : OSPlatform.Windows;

        public bool IsOSX => OS.Equals(OSPlatform.OSX);
        public bool IsWindows => OS.Equals(OSPlatform.Windows);

        // The .net core 3 apphost doesn't care about semantics of FileType -- all files are extracted at startup.
        // However, the apphost checks that the FileType value is within expected bounds, so set it to the first enumeration.
        public FileType TargetSpecificFileType(FileType fileType) => (BundleMajorVersion == 1) ? FileType.Unknown : fileType;

        // In .net core 3.x, bundle processing happens within the AppHost.
        // Therefore HostFxr and HostPolicy can be bundled within the single-file app.
        // In .net 5, bundle processing happens in HostFxr and HostPolicy libraries.
        // Therefore, these libraries themselves cannot be bundled into the single-file app.
        // This problem is mitigated by statically linking these host components with the AppHost.
        // https://github.com/dotnet/runtime/issues/32823
        public bool ShouldExclude(string relativePath) =>
            (FrameworkVersion.Major != 3) && (relativePath.Equals(HostFxr) || relativePath.Equals(HostPolicy));

        private string HostFxr => IsWindows ? "hostfxr.dll" : IsOSX ? "libhostfxr.dylib" : "libhostfxr.so";
        private string HostPolicy => IsWindows ? "hostpolicy.dll" : IsOSX ? "libhostpolicy.dylib" : "libhostpolicy.so";
    }

    file static class PlatformExtensions
    {
        extension(OSPlatform)
        {
#if NETFRAMEWORK
            public static OSPlatform FreeBSD => OSPlatform.Create("FREEBSD");
#endif
            public static OSPlatform Illumos => OSPlatform.Create("ILLUMOS");
        }

#if NETFRAMEWORK
        extension(Architecture)
        {
            public static Architecture LoongArch64 => (Architecture)6;
        }
#endif
    }
}
