using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CoopSpectator.DedicatedHelper
{
    // Directory links only. Never invokes a shell or recursively removes a tree.
    internal static class WindowsDirectoryJunction
    {
        private const uint OpenReparsePoint = 0x00200000;
        private const uint BackupSemantics = 0x02000000;
        private const uint MountPointTag = 0xA0000003;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code,
            byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,
            StringBuilder path, uint length, uint flags);

        internal static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("An installation path is empty.");
            string input = path.Trim().Trim('"');
            if (input.Length < 3 || !char.IsLetter(input[0]) || input[1] != ':' || (input[2] != '\\' && input[2] != '/'))
                throw new IOException("Shared modules require an absolute local drive path: " + path);
            string full = Path.GetFullPath(input).TrimEnd('\\', '/');
            return full;
        }

        internal static bool SamePath(string left, string right) =>
            string.Equals(FullPath(left), FullPath(right), StringComparison.OrdinalIgnoreCase);

        internal static string ResolveDirectory(string path)
        {
            path = FullPath(path);
            using (var handle = Open(path, false, 0))
            {
                var buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity) throw NativeError("Resolve directory", path);
                string resolved = buffer.ToString();
                if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved.Substring(4);
                return FullPath(resolved);
            }
        }

        internal static bool Exists(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal static bool IsReparsePoint(string path) =>
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        internal static string ReadTarget(string path)
        {
            using (var handle = Open(path, true, 0))
            {
                byte[] data = new byte[16384];
                int count;
                if (!DeviceIoControl(handle, 0x000900A8, null, 0, data, data.Length, out count, IntPtr.Zero))
                    throw NativeError("Read directory junction", path);
                if (count < 16 || BitConverter.ToUInt32(data, 0) != MountPointTag)
                    throw new IOException("Not a supported directory junction: " + path);
                int offset = BitConverter.ToUInt16(data, 8), length = BitConverter.ToUInt16(data, 10);
                if ((offset & 1) != 0 || (length & 1) != 0 || length == 0 || 16 + offset + length > count)
                    throw new IOException("Invalid directory junction data: " + path);
                string target = Encoding.Unicode.GetString(data, 16 + offset, length);
                if (!target.StartsWith(@"\??\", StringComparison.Ordinal))
                    throw new IOException("Not an absolute local junction target: " + path);
                return FullPath(target.Substring(4));
            }
        }

        internal static void Create(string path, string target)
        {
            path = FullPath(path);
            target = ResolveDirectory(target);
            if (Exists(path)) throw new IOException("Junction destination already exists: " + path);
            byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
            byte[] print = Encoding.Unicode.GetBytes(target);
            byte[] data = new byte[16 + substitute.Length + 2 + print.Length + 2];
            if (data.Length > 16384) throw new IOException("Directory junction target is too long.");
            Buffer.BlockCopy(BitConverter.GetBytes(MountPointTag), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(data.Length - 8)), 0, data, 4, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)substitute.Length), 0, data, 10, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(substitute.Length + 2)), 0, data, 12, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)print.Length), 0, data, 14, 2);
            Buffer.BlockCopy(substitute, 0, data, 16, substitute.Length);
            Buffer.BlockCopy(print, 0, data, 18 + substitute.Length, print.Length);
            Directory.CreateDirectory(path);
            try
            {
                using (var handle = Open(path, true, 0x40000000))
                {
                    int returned;
                    if (!DeviceIoControl(handle, 0x000900A4, data, data.Length, null, 0, out returned, IntPtr.Zero))
                        throw NativeError("Create directory junction", path);
                }
            }
            catch
            {
                // Only the newly created empty directory is eligible for removal.
                if (Exists(path) && !IsReparsePoint(path)) Directory.Delete(path, false);
                throw;
            }
        }

        internal static void RemoveExact(string path, string expectedTarget)
        {
            if (!IsReparsePoint(path) || !SamePath(ReadTarget(path), expectedTarget))
                throw new IOException("Refusing to remove a changed directory link: " + path);
            Directory.Delete(path, false);
        }

        private static SafeFileHandle Open(string path, bool reparse, uint access)
        {
            var handle = CreateFile(path, access, 7, IntPtr.Zero, 3,
                BackupSemantics | (reparse ? OpenReparsePoint : 0), IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("Cannot open directory: " + path, new Win32Exception(error));
        }

        private static IOException NativeError(string operation, string path) =>
            new IOException(operation + " failed: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
    }
}
