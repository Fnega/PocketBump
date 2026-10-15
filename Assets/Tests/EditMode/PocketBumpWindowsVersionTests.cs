using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpWindowsVersionTests
    {
        string _source, _sourceHash, _tempRoot, _fixture, _launcher;

        [SetUp]
        public void CopyUnsignedLauncherToOwnedFixture()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                Assert.Ignore("Native Windows release identity test requires Windows.");
            _source = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data",
                "PlaybackEngines", "WindowsStandaloneSupport", "Variations",
                "win64_player_nondevelopment_mono", "WindowsPlayer.exe");
            if (!File.Exists(_source)) Assert.Ignore("Windows Mono build support is not installed.");
            _sourceHash = Hash(_source);
            _tempRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"));
            _fixture = Path.Combine(_tempRoot, "PocketBumpVersionTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_fixture);
            _launcher = Path.Combine(_fixture, "PocketBump.exe");
            File.Copy(_source, _launcher);
        }

        [TearDown]
        public void VerifyOriginalAndRemoveOnlyOwnedFixture()
        {
            try
            {
                if (_sourceHash != null) Assert.That(Hash(_source), Is.EqualTo(_sourceHash), "Original Unity launcher was modified.");
            }
            finally
            {
                if (_fixture != null && Directory.Exists(_fixture))
                {
                    string resolved = Path.GetFullPath(_fixture);
                    Assert.That(resolved.StartsWith(_tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
                    Assert.That(Path.GetFileName(resolved), Does.StartWith("PocketBumpVersionTest-"));
                    Directory.Delete(resolved, true);
                }
                _fixture = null;
                _sourceHash = null;
            }
        }

        [TestCase("1.0", 0)]
        [TestCase("1.1", 1)]
        [TestCase("1.2", 2)]
        public void ReleaseIdentityIsVisibleToWindowsAndPreservesCopyrightAndOtherResources(string version, int minor)
        {
            FileVersionInfo before = FileVersionInfo.GetVersionInfo(_launcher);
            string resources = (string)Invoke("NonVersionResourceHash", _launcher);
            Stamp(version);
            FileVersionInfo actual = FileVersionInfo.GetVersionInfo(_launcher);
            Assert.That(actual.CompanyName, Is.EqualTo("Fnega"));
            Assert.That(actual.ProductName, Is.EqualTo("PocketBump"));
            Assert.That(actual.FileDescription, Is.EqualTo("射球大战 / PocketBump"));
            Assert.That(actual.FileVersion, Is.EqualTo(version + ".0.0"));
            Assert.That(actual.ProductVersion, Is.EqualTo(version));
            Assert.That(actual.OriginalFilename, Is.EqualTo("PocketBump.exe"));
            Assert.That(actual.InternalName, Is.EqualTo("PocketBump"));
            Assert.That(actual.LegalCopyright, Is.EqualTo(before.LegalCopyright));
            Assert.That(new[] { actual.FileMajorPart, actual.FileMinorPart, actual.FileBuildPart, actual.FilePrivatePart }, Is.EqualTo(new[] { 1, minor, 0, 0 }));
            Assert.That(new[] { actual.ProductMajorPart, actual.ProductMinorPart, actual.ProductBuildPart, actual.ProductPrivatePart }, Is.EqualTo(new[] { 1, minor, 0, 0 }));
            Assert.That(actual.IsDebug, Is.EqualTo(before.IsDebug));
            Assert.That(actual.IsPreRelease, Is.EqualTo(before.IsPreRelease));
            Assert.That(Invoke("NonVersionResourceHash", _launcher), Is.EqualTo(resources), "Icon, manifest and every other non-version resource must survive byte-for-byte.");
        }

        [Test]
        public void UnexpectedOutputNameIsRejectedWithoutModification()
        {
            string other = Path.Combine(_fixture, "Other.exe");
            File.Move(_launcher, other);
            string before = Hash(other);
            Assert.Throws<ArgumentException>(() => Invoke("StampRelease", other, Application.unityVersion, "1.2"));
            Assert.That(Hash(other), Is.EqualTo(before));
        }

        [Test]
        public void UnexpectedUnityVersionIsRejectedWithoutModification()
        {
            string before = Hash(_launcher);
            Assert.Throws<InvalidOperationException>(() => Invoke("StampRelease", _launcher, "0.0.0-not-this-editor", "1.2"));
            Assert.That(Hash(_launcher), Is.EqualTo(before));
        }

        [TestCase("1.0")]
        [TestCase("1.1")]
        [TestCase("1.2")]
        public void ExistingStampedReleaseIsRejectedWithoutModification(string originalVersion)
        {
            Stamp(originalVersion);
            string before = Hash(_launcher);
            foreach (string requestedVersion in new[] { "1.0", "1.1", "1.2" })
                Assert.Throws<InvalidOperationException>(() => Stamp(requestedVersion));
            Assert.That(Hash(_launcher), Is.EqualTo(before));
        }

        [Test]
        public void CertificateBearingExecutableIsRejectedWithoutModification()
        {
            // Mark the owned EXE copy as certificate-bearing. The helper must reject even
            // malformed/untrusted certificate tables instead of stripping or invalidating them.
            using (var stream = File.Open(_launcher, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var reader = new BinaryReader(stream))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 0x3c;
                int pe = reader.ReadInt32();
                stream.Position = pe + 24 + 112 + 4 * 8;
                writer.Write((uint)stream.Length);
                writer.Write((uint)8);
            }
            string before = Hash(_launcher);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Stamp());
            Assert.That(error.Message, Does.Contain("certificate-bearing"));
            Assert.That(Hash(_launcher), Is.EqualTo(before));
        }

        [Test]
        public void WriteLockedOutputFailsClosedWithoutModification()
        {
            string before = Hash(_launcher);
            using (File.Open(_launcher, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<Win32Exception>(() => Stamp());
            Assert.That(Hash(_launcher), Is.EqualTo(before));
        }

        [Test]
        public void NonExecutableIsRejectedWithoutModification()
        {
            File.WriteAllBytes(_launcher, new byte[] { 1, 2, 3 });
            string before = Hash(_launcher);
            Assert.Throws<InvalidDataException>(() => Stamp());
            Assert.That(Hash(_launcher), Is.EqualTo(before));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1.3")]
        [TestCase("1.1.0.0")]
        [TestCase("../1.1")]
        public void UnsupportedReleaseVersionIsRejectedWithoutModification(string version)
        {
            string before = Hash(_launcher);
            Assert.Throws<ArgumentException>(() => Stamp(version));
            Assert.That(Hash(_launcher), Is.EqualTo(before));
        }

        void Stamp(string version = "1.2") => Invoke("StampRelease", _launcher, Application.unityVersion, version);

        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream));
        }

        static object Invoke(string method, params object[] args)
        {
            Type utility = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("PocketBump.Editor.PocketBumpWindowsVersion", false)).FirstOrDefault(t => t != null);
            Assert.That(utility, Is.Not.Null, "Windows version helper must compile in the Editor assembly.");
            try { return utility.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
    }
}
