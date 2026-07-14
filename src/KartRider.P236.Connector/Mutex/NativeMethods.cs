using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace KartRider2005MutexRelease;

internal static class NativeMethods
{
    internal const int SystemExtendedHandleInformation = 64;
    internal const int ObjectNameInformation = 1;

    internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    internal const int StatusBufferOverflow = unchecked((int)0x80000005);
    internal const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    internal const uint ProcessDuplicateHandle = 0x0040;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint TokenQuery = 0x0008;

    internal const uint DuplicateCloseSource = 0x00000001;
    internal const uint DuplicateSameAccess = 0x00000002;

    internal const int ErrorAccessDenied = 5;
    internal const int ErrorInsufficientBuffer = 122;

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryObject(
        IntPtr handle,
        int objectInformationClass,
        IntPtr objectInformation,
        int objectInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll")]
    internal static extern uint RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateHandle(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        out IntPtr targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr processHandle,
        uint flags,
        StringBuilder exeName,
        ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateMutexW(
        IntPtr mutexAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool initialOwner,
        string name);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    internal static bool IsResizeStatus(int status) =>
        status == StatusInfoLengthMismatch ||
        status == StatusBufferOverflow ||
        status == StatusBufferTooSmall;

    internal static ulong HandleValue(IntPtr handle) =>
        IntPtr.Size == 8
            ? unchecked((ulong)handle.ToInt64())
            : unchecked((uint)handle.ToInt32());

    internal static IntPtr HandleFromValue(ulong handle) =>
        IntPtr.Size == 8
            ? new IntPtr(unchecked((long)handle))
            : new IntPtr(unchecked((int)handle));

    internal static Win32Exception NtStatusException(int status, string operation)
    {
        var win32Error = unchecked((int)RtlNtStatusToDosError(status));
        return new Win32Exception(
            win32Error,
            $"{operation} failed with NTSTATUS 0x{status:X8}: " +
            new Win32Exception(win32Error).Message);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SystemHandleTableEntryInfoEx
{
    internal IntPtr Object;
    internal UIntPtr UniqueProcessId;
    internal UIntPtr HandleValue;
    internal uint GrantedAccess;
    internal ushort CreatorBackTraceIndex;
    internal ushort ObjectTypeIndex;
    internal uint HandleAttributes;
    internal uint Reserved;

    internal readonly ulong ProcessId => UniqueProcessId.ToUInt64();
    internal readonly ulong NumericHandle => HandleValue.ToUInt64();
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    internal ushort Length;
    internal ushort MaximumLength;
    internal IntPtr Buffer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SidAndAttributes
{
    internal IntPtr Sid;
    internal uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TokenUser
{
    internal SidAndAttributes User;
}
