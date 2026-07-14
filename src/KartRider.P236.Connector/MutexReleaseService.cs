using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using KartRider2005MutexRelease;

namespace KartRider.P236.Connector;

internal readonly record struct MutexReleaseSummary(
    int EligibleProcessCount,
    int MatchedCount,
    int ReleasedCount);

internal sealed class MutexReleaseService
{
    private const string TargetImageName = "KartRider.exe";
    private const string TargetObjectSuffix = @"\CR-KartRider";

    internal MutexReleaseSummary ReleaseExactMutexes(
        IReadOnlySet<string> allowedImagePaths,
        string selectedImagePath,
        IProgress<string>? progress)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("mutex 해제는 Windows에서만 지원됩니다.");
        }

        HashSet<string> normalizedAllowedPaths = new HashSet<string>(
            allowedImagePaths.Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);
        string normalizedSelectedPath = NormalizePath(selectedImagePath);
        if (!normalizedAllowedPaths.Contains(normalizedSelectedPath))
        {
            throw new InvalidOperationException("선택한 EXE가 검증된 준비 클라이언트 집합에 없습니다.");
        }

        SecurityIdentifier currentUserSid = GetCurrentUserSid();
        ushort mutantTypeIndex = DiscoverMutantTypeIndex();
        List<TargetProcess> targets = new List<TargetProcess>();
        List<int> accessDeniedPids = new List<int>();
        List<string> unapprovedProcesses = new List<string>();

        try
        {
            foreach (int processId in FindKartRiderProcessIds())
            {
                if (TargetProcess.TryOpen(
                        processId,
                        currentUserSid,
                        normalizedAllowedPaths,
                        out TargetProcess? target,
                        out string diagnostic,
                        out bool accessDenied,
                        out bool unapprovedCurrentUser))
                {
                    targets.Add(target!);
                    progress?.Report($"mutex 검사 대상: PID {processId}, {target!.ImagePath}");
                }
                else if (accessDenied)
                {
                    accessDeniedPids.Add(processId);
                    progress?.Report($"PID {processId} 검사 거부: {diagnostic}");
                }
                else if (unapprovedCurrentUser)
                {
                    unapprovedProcesses.Add($"PID {processId}: {diagnostic}");
                    progress?.Report($"PID {processId} 미승인 경로: {diagnostic}");
                }
            }

            // Failing closed preserves the same-instance guarantee when an elevated
            // KartRider cannot be distinguished safely from the selected instance.
            if (accessDeniedPids.Count > 0)
            {
                throw new UnauthorizedAccessException(
                    "실행 중인 KartRider 프로세스를 안전하게 검사할 수 없습니다 (PID " +
                    string.Join(", ", accessDeniedPids) +
                    "). 게임과 같은 권한으로 접속기를 다시 실행하세요.");
            }

            // Never close a handle in a process outside the verified allow-list.
            // It may still own the global CR-KartRider mutex, so launching and
            // pretending it does not exist would be misleading. Fail closed and
            // tell the user which process must be closed or selected explicitly.
            if (unapprovedProcesses.Count > 0)
            {
                throw new InvalidOperationException(
                    "현재 사용자의 다른 KartRider.exe가 준비 클라이언트 경로 밖에서 실행 중입니다. " +
                    "안전을 위해 해당 프로세스의 mutex는 건드리지 않았습니다. 먼저 종료하세요: " +
                    string.Join("; ", unapprovedProcesses));
            }

            TargetProcess? duplicateInstance = targets.FirstOrDefault(target =>
                string.Equals(target.ImagePath, normalizedSelectedPath, StringComparison.OrdinalIgnoreCase));
            if (duplicateInstance != null)
            {
                throw new InvalidOperationException(
                    $"선택한 인스턴스가 이미 실행 중입니다 (PID {duplicateInstance.ProcessId}).");
            }

            if (targets.Count == 0)
            {
                return new MutexReleaseSummary(0, 0, 0);
            }

            int matchedCount = 0;
            int releasedCount = 0;
            int failedCount = 0;
            using SystemHandleSnapshot snapshot = SystemHandleSnapshot.Capture();
            foreach (TargetProcess target in targets)
            {
                SystemHandleTableEntryInfoEx[] mutantEntries = snapshot
                    .EntriesFor(target.ProcessId, mutantTypeIndex)
                    .ToArray();

                foreach (SystemHandleTableEntryInfoEx entry in mutantEntries)
                {
                    IntPtr sourceHandle = NativeMethods.HandleFromValue(entry.NumericHandle);
                    if (!NativeMethods.DuplicateHandle(
                            target.Handle,
                            sourceHandle,
                            NativeMethods.GetCurrentProcess(),
                            out IntPtr queryHandle,
                            0,
                            false,
                            NativeMethods.DuplicateSameAccess))
                    {
                        continue;
                    }

                    try
                    {
                        if (!ObjectNameReader.TryRead(queryHandle, out string? objectName, out _))
                        {
                            continue;
                        }

                        if (objectName == null ||
                            !objectName.EndsWith(TargetObjectSuffix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        matchedCount++;
                        progress?.Report(
                            $"정확한 mutex 발견: PID {target.ProcessId}, " +
                            $"handle 0x{entry.NumericHandle:X}, {objectName}");

                        // The system handle table is racy. Immediately before the
                        // destructive call, require the same PID, numeric handle,
                        // Mutant type, and kernel object address in a fresh snapshot.
                        using SystemHandleSnapshot verificationSnapshot = SystemHandleSnapshot.Capture();
                        if (!verificationSnapshot.ContainsSameHandle(
                                target.ProcessId,
                                entry.NumericHandle,
                                mutantTypeIndex,
                                entry.Object))
                        {
                            failedCount++;
                            progress?.Report(
                                $"mutex 해제 중단: PID {target.ProcessId} handle이 재사용되었습니다.");
                            continue;
                        }

                        if (!NativeMethods.DuplicateHandle(
                                target.Handle,
                                sourceHandle,
                                NativeMethods.GetCurrentProcess(),
                                out IntPtr releaseHandle,
                                0,
                                false,
                                NativeMethods.DuplicateCloseSource | NativeMethods.DuplicateSameAccess))
                        {
                            failedCount++;
                            int errorCode = Marshal.GetLastWin32Error();
                            progress?.Report(
                                $"mutex 해제 실패: PID {target.ProcessId}, " +
                                new Win32Exception(errorCode).Message);
                            continue;
                        }

                        try
                        {
                            if (!ObjectNameReader.TryRead(releaseHandle, out string? releasedName, out _) ||
                                releasedName == null ||
                                !releasedName.EndsWith(TargetObjectSuffix, StringComparison.Ordinal))
                            {
                                failedCount++;
                                progress?.Report(
                                    $"mutex 사후 이름 검증 실패: PID {target.ProcessId}.");
                                continue;
                            }

                            releasedCount++;
                            progress?.Report(
                                $"mutex 해제 완료: PID {target.ProcessId}, {releasedName}");
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(releaseHandle);
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(queryHandle);
                    }
                }
            }

            if (failedCount > 0 || releasedCount != matchedCount)
            {
                throw new InvalidOperationException(
                    $"정확한 mutex {matchedCount}개 중 {releasedCount}개만 해제했습니다. " +
                    "안전을 위해 새 클라이언트를 실행하지 않습니다.");
            }

            return new MutexReleaseSummary(targets.Count, matchedCount, releasedCount);
        }
        finally
        {
            foreach (TargetProcess target in targets)
            {
                target.Dispose();
            }
        }
    }

    private static SecurityIdentifier GetCurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User
            ?? throw new InvalidOperationException("현재 Windows 사용자 SID를 확인할 수 없습니다.");
    }

    private static ushort DiscoverMutantTypeIndex()
    {
        string probeName =
            $@"Local\KartRider2005Launcher.Probe.{Environment.ProcessId}.{Guid.NewGuid():N}";
        IntPtr probeHandle = NativeMethods.CreateMutexW(IntPtr.Zero, false, probeName);
        if (probeHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateMutexW probe failed.");
        }

        try
        {
            using SystemHandleSnapshot snapshot = SystemHandleSnapshot.Capture();
            ulong numericHandle = NativeMethods.HandleValue(probeHandle);
            if (!snapshot.TryFind(Environment.ProcessId, numericHandle, out SystemHandleTableEntryInfoEx entry))
            {
                throw new InvalidOperationException(
                    "probe mutex가 system handle table에 없어 Mutant 타입을 안전하게 확인할 수 없습니다.");
            }

            return entry.ObjectTypeIndex;
        }
        finally
        {
            NativeMethods.CloseHandle(probeHandle);
        }
    }

    private static IReadOnlyList<int> FindKartRiderProcessIds()
    {
        List<int> result = new List<int>();
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(TargetImageName)))
        {
            using (process)
            {
                try
                {
                    result.Add(process.Id);
                }
                catch (InvalidOperationException)
                {
                    // The process exited during enumeration.
                }
            }
        }

        result.Sort();
        return result.Distinct().ToArray();
    }

    private static string NormalizePath(string path)
    {
        string normalized = path;
        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"\\" + normalized[8..];
        }
        else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        return Path.GetFullPath(normalized);
    }

    private sealed class TargetProcess : IDisposable
    {
        private TargetProcess(int processId, IntPtr handle, string imagePath)
        {
            ProcessId = processId;
            Handle = handle;
            ImagePath = imagePath;
        }

        internal int ProcessId { get; }
        internal IntPtr Handle { get; private set; }
        internal string ImagePath { get; }

        internal static bool TryOpen(
            int processId,
            SecurityIdentifier currentUserSid,
            IReadOnlySet<string> allowedImagePaths,
            out TargetProcess? target,
            out string diagnostic,
            out bool accessDenied,
            out bool unapprovedCurrentUser)
        {
            target = null;
            accessDenied = false;
            unapprovedCurrentUser = false;
            IntPtr processHandle = NativeMethods.OpenProcess(
                NativeMethods.ProcessDuplicateHandle | NativeMethods.ProcessQueryLimitedInformation,
                false,
                processId);
            if (processHandle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                accessDenied = errorCode == NativeMethods.ErrorAccessDenied;
                diagnostic = "OpenProcess: " + new Win32Exception(errorCode).Message;
                return false;
            }

            try
            {
                if (!TryReadImagePath(processHandle, out string? imagePath, out diagnostic))
                {
                    return false;
                }

                string normalizedImagePath = NormalizePath(imagePath!);
                if (!string.Equals(
                        Path.GetFileName(normalizedImagePath),
                        TargetImageName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostic = "이미지 basename이 KartRider.exe가 아닙니다.";
                    return false;
                }

                if (!TryReadUserSid(
                        processHandle,
                        out SecurityIdentifier? processUserSid,
                        out diagnostic,
                        out accessDenied))
                {
                    return false;
                }

                if (!allowedImagePaths.Contains(normalizedImagePath))
                {
                    unapprovedCurrentUser = true;
                    diagnostic = normalizedImagePath;
                    return false;
                }

                if (!string.Equals(
                        processUserSid!.Value,
                        currentUserSid.Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostic = "프로세스 소유자가 현재 사용자와 다릅니다.";
                    return false;
                }

                target = new TargetProcess(processId, processHandle, normalizedImagePath);
                processHandle = IntPtr.Zero;
                diagnostic = string.Empty;
                return true;
            }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(processHandle);
                }
            }
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(Handle);
                Handle = IntPtr.Zero;
            }
        }

        private static bool TryReadImagePath(
            IntPtr processHandle,
            out string? imagePath,
            out string diagnostic)
        {
            for (int capacity = 1024; capacity <= 32768; capacity *= 2)
            {
                StringBuilder builder = new StringBuilder(capacity);
                uint size = (uint)builder.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(processHandle, 0, builder, ref size))
                {
                    imagePath = builder.ToString();
                    diagnostic = string.Empty;
                    return true;
                }

                int errorCode = Marshal.GetLastWin32Error();
                if (errorCode != NativeMethods.ErrorInsufficientBuffer)
                {
                    imagePath = null;
                    diagnostic = "QueryFullProcessImageNameW: " +
                                 new Win32Exception(errorCode).Message;
                    return false;
                }
            }

            imagePath = null;
            diagnostic = "프로세스 이미지 경로가 32,768자를 초과합니다.";
            return false;
        }

        private static bool TryReadUserSid(
            IntPtr processHandle,
            out SecurityIdentifier? userSid,
            out string diagnostic,
            out bool accessDenied)
        {
            userSid = null;
            accessDenied = false;
            if (!NativeMethods.OpenProcessToken(
                    processHandle,
                    NativeMethods.TokenQuery,
                    out IntPtr tokenHandle))
            {
                int errorCode = Marshal.GetLastWin32Error();
                accessDenied = errorCode == NativeMethods.ErrorAccessDenied;
                diagnostic = "OpenProcessToken: " + new Win32Exception(errorCode).Message;
                return false;
            }

            try
            {
                NativeMethods.GetTokenInformation(tokenHandle, 1, IntPtr.Zero, 0, out int requiredSize);
                int errorCode = Marshal.GetLastWin32Error();
                if (requiredSize <= 0 || errorCode != NativeMethods.ErrorInsufficientBuffer)
                {
                    accessDenied = errorCode == NativeMethods.ErrorAccessDenied;
                    diagnostic = "GetTokenInformation(size): " +
                                 new Win32Exception(errorCode).Message;
                    return false;
                }

                IntPtr buffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    if (!NativeMethods.GetTokenInformation(
                            tokenHandle,
                            1,
                            buffer,
                            requiredSize,
                            out _))
                    {
                        errorCode = Marshal.GetLastWin32Error();
                        accessDenied = errorCode == NativeMethods.ErrorAccessDenied;
                        diagnostic = "GetTokenInformation(TokenUser): " +
                                     new Win32Exception(errorCode).Message;
                        return false;
                    }

                    TokenUser tokenUser = Marshal.PtrToStructure<TokenUser>(buffer);
                    userSid = new SecurityIdentifier(tokenUser.User.Sid);
                    diagnostic = string.Empty;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(tokenHandle);
            }
        }
    }

    private static class ObjectNameReader
    {
        private const int InitialBufferSize = 512;
        private const int MaximumBufferSize = 1024 * 1024;

        internal static bool TryRead(IntPtr handle, out string? objectName, out string error)
        {
            int bufferSize = InitialBufferSize;
            while (bufferSize <= MaximumBufferSize)
            {
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    int status = NativeMethods.NtQueryObject(
                        handle,
                        NativeMethods.ObjectNameInformation,
                        buffer,
                        bufferSize,
                        out int requiredSize);
                    if (status >= 0)
                    {
                        UnicodeString name = Marshal.PtrToStructure<UnicodeString>(buffer);
                        if (name.Length == 0 || name.Buffer == IntPtr.Zero)
                        {
                            objectName = null;
                            error = string.Empty;
                            return true;
                        }

                        if ((name.Length & 1) != 0 ||
                            !PointsInside(buffer, bufferSize, name.Buffer, name.Length))
                        {
                            objectName = null;
                            error = "NtQueryObject가 잘못된 UNICODE_STRING을 반환했습니다.";
                            return false;
                        }

                        objectName = Marshal.PtrToStringUni(name.Buffer, name.Length / 2);
                        error = string.Empty;
                        return true;
                    }

                    if (!NativeMethods.IsResizeStatus(status))
                    {
                        objectName = null;
                        error = NativeMethods.NtStatusException(status, "NtQueryObject").Message;
                        return false;
                    }

                    int doubled = checked(bufferSize * 2);
                    bufferSize = Math.Max(doubled, requiredSize > 0 ? requiredSize : 0);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            objectName = null;
            error = $"NtQueryObject가 {MaximumBufferSize:N0}바이트보다 큰 버퍼를 요청했습니다.";
            return false;
        }

        private static bool PointsInside(
            IntPtr allocation,
            int allocationSize,
            IntPtr value,
            int valueSize)
        {
            long start = allocation.ToInt64();
            long end = checked(start + allocationSize);
            long pointer = value.ToInt64();
            return pointer >= start && pointer <= end - valueSize;
        }
    }
}
