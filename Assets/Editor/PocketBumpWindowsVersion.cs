using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PocketBump.Editor
{
    // Windows PlayerSettings do not stamp the prebuilt launcher (Unity UUM-87133).
    // Called only for a newly built, unsigned Release launcher, before acceptance.
    // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-beginupdateresourcew
    public static class PocketBumpWindowsVersion
    {
        const string Description = "射球大战 / PocketBump";
        const uint LoadAsDataFile = 2;
        static readonly IntPtr VersionType = new IntPtr(16), VersionName = new IntPtr(1);

        public static void StampRelease(string executablePath, string expectedUnityVersion, string releaseVersion)
        {
            if (releaseVersion != "1.0" && releaseVersion != "1.1" && releaseVersion != "1.2")
                throw new ArgumentException("Only reviewed PocketBump release versions 1.0, 1.1 and 1.2 are supported.", nameof(releaseVersion));
            int minor = releaseVersion == "1.2" ? 2 : releaseVersion == "1.1" ? 1 : 0;
            string fileVersion = releaseVersion + ".0.0";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Windows release identity must be authored on Windows.");
            string path = Path.GetFullPath(executablePath);
            if (!string.Equals(Path.GetFileName(path), "PocketBump.exe", StringComparison.Ordinal))
                throw new ArgumentException("Version stamping is restricted to the new PocketBump.exe launcher.", nameof(executablePath));
            if (string.IsNullOrWhiteSpace(expectedUnityVersion)) throw new ArgumentException("Expected Unity version is required.");
            RejectSignedOrUnexpectedPe(path);
            FileVersionInfo original = FileVersionInfo.GetVersionInfo(path);
            if (original.ProductVersion == null || !original.ProductVersion.StartsWith(expectedUnityVersion + " (", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(original.CompanyName) || !string.IsNullOrEmpty(original.ProductName) ||
                !string.IsNullOrEmpty(original.FileDescription) || original.IsDebug ||
                string.IsNullOrEmpty(original.LegalCopyright) || original.LegalCopyright.IndexOf("Unity", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Refusing to stamp an unexpected or already-stamped Unity launcher.");

            byte[] fixedInfo = ReadFixedVersionInfo(path);
            string preservedResources = NonVersionResourceHash(path);
            ushort language = ReadVersionLanguage(path);
            if (language != 0x0409) throw new InvalidOperationException("Unexpected launcher version-resource language.");
            // Preserve all fixed flags/OS fields and original Unity copyright.
            PutUInt32(fixedInfo, 8, 0x00010000 | (uint)minor); PutUInt32(fixedInfo, 12, 0);
            PutUInt32(fixedInfo, 16, 0x00010000 | (uint)minor); PutUInt32(fixedInfo, 20, 0);
            byte[] resource = Block("VS_VERSION_INFO", 0, fixedInfo,
                Block("StringFileInfo", 1, null, Block("040904b0", 1, null,
                    Text("CompanyName", "Fnega"), Text("ProductName", "PocketBump"),
                    Text("FileDescription", Description), Text("FileVersion", fileVersion),
                    Text("ProductVersion", releaseVersion), Text("OriginalFilename", "PocketBump.exe"),
                    Text("InternalName", "PocketBump"), Text("LegalCopyright", original.LegalCopyright))),
                Block("VarFileInfo", 1, null, Block("Translation", 0, new byte[] { 0x09, 0x04, 0xb0, 0x04 })));
            IntPtr update = BeginUpdateResourceW(path, false); // Never delete icons/manifest/other resources.
            if (update == IntPtr.Zero) throw Error("BeginUpdateResource");
            try
            {
                if (!UpdateResourceW(update, VersionType, VersionName, language, resource, (uint)resource.Length))
                    throw Error("UpdateResource RT_VERSION");
                IntPtr committing = update;
                update = IntPtr.Zero; // Do not reuse a handle after an EndUpdateResource attempt.
                if (!EndUpdateResourceW(committing, false)) throw Error("EndUpdateResource commit");
            }
            finally { if (update != IntPtr.Zero) EndUpdateResourceW(update, true); }
            FileVersionInfo stamped = FileVersionInfo.GetVersionInfo(path);
            if (stamped.CompanyName != "Fnega" || stamped.ProductName != "PocketBump" ||
                stamped.FileDescription != Description || stamped.FileVersion != fileVersion || stamped.ProductVersion != releaseVersion ||
                stamped.OriginalFilename != "PocketBump.exe" || stamped.LegalCopyright != original.LegalCopyright ||
                stamped.FileMajorPart != 1 || stamped.FileMinorPart != minor || stamped.FileBuildPart != 0 || stamped.FilePrivatePart != 0 ||
                stamped.ProductMajorPart != 1 || stamped.ProductMinorPart != minor || stamped.ProductBuildPart != 0 || stamped.ProductPrivatePart != 0 ||
                NonVersionResourceHash(path) != preservedResources)
                throw new InvalidOperationException("Windows launcher identity or preserved resources failed verification.");
        }

        static void RejectSignedOrUnexpectedPe(string path)
        {
            using (var reader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                if (reader.BaseStream.Length < 256 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Not a PE launcher.");
                reader.BaseStream.Position = 0x3c;
                int pe = reader.ReadInt32();
                if (pe < 64 || pe > reader.BaseStream.Length - 264) throw new InvalidDataException("Invalid PE header.");
                reader.BaseStream.Position = pe;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Expected a Windows x64 launcher.");
                reader.BaseStream.Position = pe + 24;
                if (reader.ReadUInt16() != 0x20b) throw new InvalidDataException("Expected a PE32+ launcher.");
                reader.BaseStream.Position = pe + 24 + 108;
                if (reader.ReadUInt32() < 5) throw new InvalidDataException("Missing PE data directories.");
                reader.BaseStream.Position = pe + 24 + 112 + 4 * 8;
                if (reader.ReadUInt32() != 0 || reader.ReadUInt32() != 0)
                    throw new InvalidOperationException("Signed or certificate-bearing executables cannot be stamped by this build step.");
            }
        }

        static byte[] ReadFixedVersionInfo(string path)
        {
            uint ignored;
            uint size = GetFileVersionInfoSizeW(path, out ignored);
            if (size == 0) throw Error("GetFileVersionInfoSize");
            var data = new byte[size];
            if (!GetFileVersionInfoW(path, 0, size, data)) throw Error("GetFileVersionInfo");
            GCHandle pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr value; uint length;
                if (!VerQueryValueW(pinned.AddrOfPinnedObject(), "\\", out value, out length) || length != 52)
                    throw new InvalidDataException("Unexpected VS_FIXEDFILEINFO.");
                var fixedInfo = new byte[length]; Marshal.Copy(value, fixedInfo, 0, fixedInfo.Length);
                if (BitConverter.ToUInt32(fixedInfo, 0) != 0xfeef04bd) throw new InvalidDataException("Invalid fixed version signature.");
                return fixedInfo;
            }
            finally { pinned.Free(); }
        }

        static ushort ReadVersionLanguage(string path)
        {
            IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadAsDataFile);
            if (module == IntPtr.Zero) throw Error("LoadLibraryEx version");
            try
            {
                var languages = new List<ushort>();
                EnumLanguages callback = (m, t, n, lang, p) => { languages.Add(lang); return true; };
                if (!EnumResourceLanguagesW(module, VersionType, VersionName, callback, IntPtr.Zero)) throw Error("EnumResourceLanguages version");
                if (languages.Count != 1) throw new InvalidDataException("Expected exactly one version-resource language.");
                return languages[0];
            }
            finally { FreeLibrary(module); }
        }

        // Also used by the isolated fixture test to prove icon/manifest resources survive.
        public static string NonVersionResourceHash(string path)
        {
            IntPtr module = LoadLibraryExW(Path.GetFullPath(path), IntPtr.Zero, LoadAsDataFile);
            if (module == IntPtr.Zero) throw Error("LoadLibraryEx resources");
            try
            {
                var hashes = new List<string>(); Exception failure = null;
                EnumTypes types = (m, type, p) =>
                {
                    if (type == VersionType) return true;
                    EnumNames names = (mn, tn, name, pn) =>
                    {
                        EnumLanguages languages = (ml, tl, nl, lang, pl) =>
                        {
                            try
                            {
                                IntPtr resource = FindResourceExW(ml, tl, nl, lang);
                                if (resource == IntPtr.Zero) throw Error("FindResourceEx");
                                uint size = SizeofResource(ml, resource);
                                IntPtr handle = LoadResource(ml, resource);
                                IntPtr value = LockResource(handle);
                                if (value == IntPtr.Zero && size != 0) throw Error("LockResource");
                                var data = new byte[size]; if (size != 0) Marshal.Copy(value, data, 0, data.Length);
                                using (var sha = SHA256.Create())
                                    hashes.Add(ResourceName(tl) + "/" + ResourceName(nl) + "/" + lang.ToString("x4") + ":" + BitConverter.ToString(sha.ComputeHash(data)));
                                return true;
                            }
                            catch (Exception error) { failure = error; return false; }
                        };
                        if (!EnumResourceLanguagesW(mn, tn, name, languages, IntPtr.Zero))
                        { if (failure == null) failure = Error("EnumResourceLanguages"); return false; }
                        return true;
                    };
                    if (!EnumResourceNamesW(m, type, names, IntPtr.Zero))
                    { if (failure == null) failure = Error("EnumResourceNames"); return false; }
                    return true;
                };
                if (!EnumResourceTypesW(module, types, IntPtr.Zero) && failure == null) failure = Error("EnumResourceTypes");
                if (failure != null) throw failure;
                hashes.Sort(StringComparer.Ordinal);
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", hashes))));
            }
            finally { FreeLibrary(module); }
        }

        static string ResourceName(IntPtr value) => ((ulong)value.ToInt64() >> 16) == 0 ? "#" + value.ToInt64() : Marshal.PtrToStringUni(value);
        static byte[] Text(string key, string text) => Block(key, 1, Encoding.Unicode.GetBytes(text + '\0'));
        static byte[] Block(string key, ushort type, byte[] value, params byte[][] children)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                int size = value == null ? 0 : value.Length;
                writer.Write((ushort)0); writer.Write(checked((ushort)(type == 1 ? size / 2 : size))); writer.Write(type);
                writer.Write(Encoding.Unicode.GetBytes(key + '\0')); Align(writer);
                if (value != null) writer.Write(value);
                foreach (byte[] child in children) { Align(writer); writer.Write(child); }
                long length = stream.Length;
                stream.Position = 0; writer.Write(checked((ushort)length)); writer.Flush();
                return stream.ToArray();
            }
        }
        static void Align(BinaryWriter writer) { while (writer.BaseStream.Position % 4 != 0) writer.Write((byte)0); }
        static void PutUInt32(byte[] bytes, int offset, uint value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
        static Win32Exception Error(string operation) => new Win32Exception(Marshal.GetLastWin32Error(), operation);

        delegate bool EnumTypes(IntPtr module, IntPtr type, IntPtr parameter);
        delegate bool EnumNames(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
        delegate bool EnumLanguages(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr BeginUpdateResourceW(string path, bool deleteExisting);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool UpdateResourceW(IntPtr handle, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EndUpdateResourceW(IntPtr handle, bool discard);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EnumResourceTypesW(IntPtr module, EnumTypes callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumNames callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EnumResourceLanguagesW(IntPtr module, IntPtr type, IntPtr name, EnumLanguages callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindResourceExW(IntPtr module, IntPtr type, IntPtr name, ushort language);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr LockResource(IntPtr resource);
        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFileVersionInfoSizeW(string path, out uint ignored);
        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetFileVersionInfoW(string path, uint ignored, uint size, byte[] data);
        [DllImport("version.dll", CharSet = CharSet.Unicode)] static extern bool VerQueryValueW(IntPtr data, string query, out IntPtr value, out uint length);
    }
}
