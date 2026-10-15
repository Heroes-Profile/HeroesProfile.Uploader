using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Heroesprofile.Uploader.Desktop.Platform
{
    /// <summary>
    /// A generic password in the user's default macOS keychain, through the Security framework's
    /// SecKeychain*GenericPassword C API. Deprecated in favour of SecItem*, but still supported, and it
    /// takes plain byte buffers rather than CoreFoundation dictionaries. Called directly rather than
    /// through the `security` command, which would put the secret on its command line where other
    /// users can read it with `ps`.
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal static class MacKeychain
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private const int ErrSecSuccess = 0;
        private const int ErrSecItemNotFound = -25300;

        [DllImport(Security)]
        private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, IntPtr itemRef);

        [DllImport(Security)]
        private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

        [DllImport(Security)]
        private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

        [DllImport(Security)]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport(Security)]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);

        /// <summary>Stores <paramref name="secret"/> under service/account, replacing any existing value. Throws on failure.</summary>
        public static void Set(string service, string account, string secret)
        {
            var s = Encoding.UTF8.GetBytes(service);
            var a = Encoding.UTF8.GetBytes(account);
            var data = Encoding.UTF8.GetBytes(secret);

            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out _, out _, out var item);
            if (status == ErrSecSuccess) {
                try {
                    Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)data.Length, data), "update");
                }
                finally {
                    CFRelease(item);
                }
                return;
            }
            if (status != ErrSecItemNotFound) {
                Check(status, "look up");
            }
            Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, (uint)data.Length, data, IntPtr.Zero), "add");
        }

        /// <summary>The stored value, or null if there isn't one. Throws if the keychain can't be read.</summary>
        public static string Get(string service, string account)
        {
            var s = Encoding.UTF8.GetBytes(service);
            var a = Encoding.UTF8.GetBytes(account);

            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out var length, out var data, out var item);
            if (status == ErrSecItemNotFound) {
                return null;
            }
            Check(status, "read");
            try {
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, (int)length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                CFRelease(item);
            }
        }

        /// <summary>Removes the stored value, if there is one.</summary>
        public static void Delete(string service, string account)
        {
            var s = Encoding.UTF8.GetBytes(service);
            var a = Encoding.UTF8.GetBytes(account);

            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out _, out _, out var item);
            if (status == ErrSecItemNotFound) {
                return;
            }
            Check(status, "look up");
            try {
                Check(SecKeychainItemDelete(item), "delete");
            }
            finally {
                CFRelease(item);
            }
        }

        private static void Check(int status, string action)
        {
            if (status != ErrSecSuccess) {
                throw new InvalidOperationException($"Keychain {action} failed (OSStatus {status}).");
            }
        }
    }
}
