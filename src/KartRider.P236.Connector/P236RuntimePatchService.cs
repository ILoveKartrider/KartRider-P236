using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KartRider.P236.Connector;

internal sealed class P236RuntimePatchService
{
    private static readonly byte[] InitSignature =
    {
        0x55, 0x8B, 0xEC, 0x81, 0xEC, 0xD4, 0x01, 0x00,
        0x00, 0x89, 0x8D, 0x54, 0xFE, 0xFF, 0xFF, 0x51
    };

    private static readonly byte[] ClearSignature =
    {
        0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08, 0x89, 0x4D,
        0xF8, 0x8B, 0x45, 0xF8, 0x05, 0xE4, 0x04, 0x00
    };

    private static readonly byte[] UpdateSignature =
    {
        0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x80, 0x02, 0x00,
        0x00, 0x89, 0x8D, 0xA0, 0xFD, 0xFF, 0xFF, 0xC7
    };

    private static readonly byte[] Challenge3BaseUpdateCallSignature =
    {
        0xE8, 0xFC, 0xCF, 0xF9, 0xFF
    };

    private static readonly byte[] DestructorSignature =
    {
        0x55, 0x8B, 0xEC, 0x51, 0x89, 0x4D, 0xFC, 0x8B,
        0x4D, 0xFC, 0xE8, 0xB1, 0xC1, 0xFF, 0xFF
    };

    private static readonly byte[] MissionIdCopySignature =
    {
        0x8A, 0x42, 0x48, 0x88, 0x81, 0x98, 0x04, 0x00, 0x00
    };

    private static readonly byte[] DevilConstructorSignature =
    {
        0x55, 0x8B, 0xEC, 0x51, 0x89, 0x4D, 0xFC, 0x8B,
        0x4D, 0xFC, 0xE8, 0x01, 0x01, 0xFF, 0xFF, 0x8B
    };

    private static readonly byte[] ProgressIndexSignature =
    {
        0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x0C, 0x89, 0x4D,
        0xF4, 0x8D, 0x45, 0x08, 0x50, 0x8D, 0x4D, 0xF8
    };

    private static readonly byte[] VisibilitySetterSignature =
    {
        0x55, 0x8B, 0xEC, 0x51, 0x89, 0x4D, 0xFC, 0x8B,
        0x45, 0xFC, 0x8A, 0x4D, 0x08, 0x88, 0x88, 0x90
    };

    private static readonly byte[] ObstacleKey = Encoding.Unicode.GetBytes("obstacle\0");
    private static readonly byte[] DummyKey = Encoding.Unicode.GetBytes("dummy\0");

    private const uint Challenge3DestructorRva = 0x166070;
    private const uint Challenge3InitRva = 0x162350;
    private const uint Challenge3ClearRva = 0x1635C0;
    private const uint Challenge3UpdateRva = 0x164FB0;
    private const uint Challenge3BaseUpdateCallRva = 0x16509F;
    private const uint DevilConstructorRva = 0x0B7DB0;
    private const uint ProgressIndexRva = 0x087770;
    private const uint VisibilitySetterRva = 0x08F560;
    private const uint MissionIdCopyRva = 0x1623A1;
    private const uint ObstacleKeyRva = 0x39FCBC;
    private const uint DummyKeyRva = 0x3A0268;
    private const uint VtableDestructorRva = 0x3A274C;
    private const uint VtableUpdateRva = 0x3A2770;
    private const uint VtableInitRva = 0x3A2780;
    private const uint VtableClearRva = 0x3A2784;
    private const int VtablePatchLength = 0x3C;

    internal void Apply(
        Process process,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        cancellationToken.ThrowIfCancellationRequested();

        uint moduleBase = GetModuleBase(process);
        using SafeProcessHandle processHandle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation |
            NativeMethods.ProcessVmOperation |
            NativeMethods.ProcessVmRead |
            NativeMethods.ProcessVmWrite,
            inheritHandle: false,
            process.Id);
        if (processHandle.IsInvalid)
        {
            throw NativeError("P236 런타임 패치용 프로세스 핸들을 열지 못했습니다");
        }

        progress?.Report("L1 호환 훅: P236 보호 해제 및 코드 초기화를 기다리는 중...");
        WaitForValidatedImage(
            process,
            processHandle,
            moduleBase,
            cancellationToken,
            timeout: TimeSpan.FromSeconds(25));

        IntPtr allocation = NativeMethods.VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            (nuint)P236L1StageShimAssembler.AllocationSize,
            NativeMethods.MemCommit | NativeMethods.MemReserve,
            NativeMethods.PageReadWrite);
        if (allocation == IntPtr.Zero)
        {
            throw NativeError("P236 L1 호환 훅 메모리를 할당하지 못했습니다");
        }

        bool runtimeReferencesAllocation = false;
        try
        {
            uint codeBase = ToUInt32Address(allocation, "원격 코드 메모리");
            uint dataBase = checked(codeBase + P236L1StageShimAssembler.CodePageSize);
            P236L1StageShimImage image = P236L1StageShimAssembler.Build(codeBase, dataBase, moduleBase);

            WriteExact(processHandle, dataBase, image.Data, "L1 호환 훅 상태 데이터");
            WriteExact(processHandle, codeBase, image.Code, "L1 호환 훅 코드");
            if (!NativeMethods.VirtualProtectEx(
                    processHandle,
                    Address(codeBase),
                    (nuint)P236L1StageShimAssembler.CodePageSize,
                    NativeMethods.PageExecuteRead,
                    out _))
            {
                throw NativeError("P236 L1 호환 훅 코드 페이지를 실행 가능하게 만들지 못했습니다");
            }

            if (!NativeMethods.FlushInstructionCache(
                    processHandle,
                    Address(codeBase),
                    (nuint)image.Code.Length))
            {
                throw NativeError("P236 L1 호환 훅 명령 캐시를 갱신하지 못했습니다");
            }

            PatchRuntimeHooks(
                processHandle,
                moduleBase,
                codeBase,
                image,
                progress,
                ref runtimeReferencesAllocation);

            // Some protectors finish restoring section permissions/data shortly
            // after unpacking. Confirm twice that our slots survive that window.
            for (int check = 0; check < 2; check++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(400);
                if (!IsInstalledRuntimeHooks(processHandle, moduleBase, codeBase, image))
                {
                    throw new InvalidOperationException(
                        "P236 보호기 초기화 중 Challenge3 vtable 패치가 되돌아가 적용을 중단했습니다.");
                }
            }
        }
        finally
        {
            // Once a vtable points into the allocation it must remain alive for
            // the client lifetime. Before that point every failure is reversible.
            if (!runtimeReferencesAllocation)
            {
                _ = NativeMethods.VirtualFreeEx(
                    processHandle,
                    allocation,
                    0,
                    NativeMethods.MemRelease);
            }
        }

        progress?.Report(
            "L1 호환 훅 적용 완료: 0x41 대마왕, 0x44 화살표, 0x45 Factory 기믹 활성화");
    }

    private static uint GetModuleBase(Process process)
    {
        process.Refresh();
        if (process.HasExited)
        {
            throw new InvalidOperationException("L1 호환 훅을 적용하기 전에 KartRider.exe가 종료되었습니다.");
        }

        ProcessModule? module = process.MainModule;
        if (module == null)
        {
            throw new InvalidOperationException("KartRider.exe의 메인 모듈을 찾지 못했습니다.");
        }

        return ToUInt32Address(module.BaseAddress, "KartRider.exe 이미지 베이스");
    }

    private static void WaitForValidatedImage(
        Process process,
        SafeProcessHandle processHandle,
        uint moduleBase,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int consecutiveMatches = 0;
        string lastMismatch = "코드가 아직 준비되지 않았습니다.";

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"L1 호환 훅 대기 중 KartRider.exe가 종료되었습니다 (exit={process.ExitCode}).");
            }

            if (IsValidatedImageReady(processHandle, moduleBase, out lastMismatch))
            {
                consecutiveMatches++;
                if (consecutiveMatches >= 8)
                {
                    return;
                }
            }
            else
            {
                consecutiveMatches = 0;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            "알려진 P236 코드가 제한 시간 안에 준비되지 않아 L1 호환 훅을 적용하지 않았습니다. " +
            lastMismatch);
    }

    private static bool IsValidatedImageReady(
        SafeProcessHandle processHandle,
        uint moduleBase,
        out string mismatch)
    {
        if (!Matches(processHandle, checked(moduleBase + Challenge3InitRva), InitSignature))
        {
            mismatch = "Challenge3 init 시그니처 불일치";
            return false;
        }

        if (!Matches(processHandle, checked(moduleBase + Challenge3ClearRva), ClearSignature))
        {
            mismatch = "Challenge3 clear 시그니처 불일치";
            return false;
        }

        if (!Matches(processHandle, checked(moduleBase + Challenge3UpdateRva), UpdateSignature))
        {
            mismatch = "Challenge3 update 시그니처 불일치";
            return false;
        }

        if (!Matches(
                processHandle,
                checked(moduleBase + Challenge3BaseUpdateCallRva),
                Challenge3BaseUpdateCallSignature))
        {
            mismatch = "Challenge3 base-update callsite 시그니처 불일치";
            return false;
        }

        if (!Matches(processHandle, checked(moduleBase + Challenge3DestructorRva), DestructorSignature))
        {
            mismatch = "Challenge3 destructor 시그니처 불일치";
            return false;
        }

        if (!Matches(processHandle, checked(moduleBase + MissionIdCopyRva), MissionIdCopySignature))
        {
            mismatch = "Challenge3 mission-id 복사 시그니처 불일치";
            return false;
        }

        if (!Matches(processHandle, checked(moduleBase + DevilConstructorRva), DevilConstructorSignature) ||
            !Matches(processHandle, checked(moduleBase + ProgressIndexRva), ProgressIndexSignature) ||
            !Matches(processHandle, checked(moduleBase + VisibilitySetterRva), VisibilitySetterSignature))
        {
            mismatch = "L1 대마왕/체크포인트 보조 코드 시그니처 불일치";
            return false;
        }

        if (!Matches(processHandle, checked(moduleBase + ObstacleKeyRva), ObstacleKey) ||
            !Matches(processHandle, checked(moduleBase + DummyKeyRva), DummyKey))
        {
            mismatch = "Factory 리소스 키 불일치";
            return false;
        }

        if (!TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableDestructorRva),
                out uint destructorPointer) ||
            destructorPointer != checked(moduleBase + Challenge3DestructorRva))
        {
            mismatch = "Challenge3 destructor vtable 불일치";
            return false;
        }

        if (!TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableUpdateRva),
                out uint updatePointer) ||
            updatePointer != checked(moduleBase + Challenge3UpdateRva))
        {
            mismatch = "Challenge3 update vtable 불일치";
            return false;
        }

        if (!TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableInitRva),
                out uint initPointer) ||
            initPointer != checked(moduleBase + Challenge3InitRva))
        {
            mismatch = "Challenge3 init vtable 불일치";
            return false;
        }

        if (!TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableClearRva),
                out uint clearPointer) ||
            clearPointer != checked(moduleBase + Challenge3ClearRva))
        {
            mismatch = "Challenge3 clear vtable 불일치";
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    private static void PatchRuntimeHooks(
        SafeProcessHandle processHandle,
        uint moduleBase,
        uint codeBase,
        P236L1StageShimImage image,
        IProgress<string>? progress,
        ref bool mayReferenceAllocation)
    {
        uint tableAddress = checked(moduleBase + VtableDestructorRva);
        byte[] originalTable = ReadExact(
            processHandle,
            tableAddress,
            VtablePatchLength,
            "Challenge3 vtable");
        ValidateVtableSnapshot(originalTable, moduleBase);

        uint callAddress = checked(moduleBase + Challenge3BaseUpdateCallRva);
        byte[] originalCall = ReadExact(
            processHandle,
            callAddress,
            Challenge3BaseUpdateCallSignature.Length,
            "Challenge3 base-update callsite");
        if (!originalCall.AsSpan().SequenceEqual(Challenge3BaseUpdateCallSignature))
        {
            throw new InvalidOperationException(
                "Challenge3 base-update callsite가 검증 후 변경되어 L1 호환 훅을 적용하지 않았습니다.");
        }

        byte[] patchedCall = BuildRelativeCallInstruction(
            callAddress,
            checked(codeBase + (uint)image.DevilBaseUpdateThunkOffset));

        (uint Address, uint Original, uint Patched)[] slots =
        {
            (
                checked(moduleBase + VtableDestructorRva),
                ReadUInt32(originalTable, 0),
                checked(codeBase + (uint)image.DestructorOffset)),
            (
                checked(moduleBase + VtableUpdateRva),
                ReadUInt32(originalTable, checked((int)(VtableUpdateRva - VtableDestructorRva))),
                checked(codeBase + (uint)image.UpdateOffset)),
            (
                checked(moduleBase + VtableInitRva),
                ReadUInt32(originalTable, checked((int)(VtableInitRva - VtableDestructorRva))),
                checked(codeBase + (uint)image.InitOffset)),
            (
                checked(moduleBase + VtableClearRva),
                ReadUInt32(originalTable, checked((int)(VtableClearRva - VtableDestructorRva))),
                checked(codeBase + (uint)image.ClearOffset))
        };

        bool tableProtectionChanged = false;
        bool callProtectionChanged = false;
        uint oldTableProtection = 0;
        uint oldCallProtection = 0;
        bool patchedWritten = false;
        try
        {
            if (!NativeMethods.VirtualProtectEx(
                    processHandle,
                    Address(tableAddress),
                    (nuint)VtablePatchLength,
                    NativeMethods.PageReadWrite,
                    out oldTableProtection))
            {
                throw NativeError("Challenge3 vtable 쓰기 보호를 해제하지 못했습니다");
            }

            tableProtectionChanged = true;
            if (!NativeMethods.VirtualProtectEx(
                    processHandle,
                    Address(callAddress),
                    (nuint)patchedCall.Length,
                    NativeMethods.PageExecuteReadWrite,
                    out oldCallProtection))
            {
                throw NativeError("Challenge3 base-update callsite 쓰기 보호를 해제하지 못했습니다");
            }

            callProtectionChanged = true;
            // Change only the four owned entries. Rewriting the entire vtable
            // would risk clobbering unrelated entries changed by the protector.
            mayReferenceAllocation = true;
            foreach (var slot in slots)
            {
                WriteExact(
                    processHandle,
                    slot.Address,
                    UInt32Bytes(slot.Patched),
                    $"Challenge3 vtable 슬롯 0x{slot.Address:X8} 패치");
            }

            WriteExact(
                processHandle,
                callAddress,
                patchedCall,
                "Challenge3 base-update callsite 패치");
            if (!NativeMethods.FlushInstructionCache(
                    processHandle,
                    Address(callAddress),
                    (nuint)patchedCall.Length))
            {
                throw NativeError("Challenge3 base-update callsite 명령 캐시를 갱신하지 못했습니다");
            }

            if (!IsInstalledRuntimeHooks(processHandle, moduleBase, codeBase, image))
            {
                throw new InvalidOperationException(
                    "Challenge3 runtime hook 패치 직후 검증에 실패했습니다.");
            }

            patchedWritten = true;
        }
        catch (Exception patchError)
        {
            if (!mayReferenceAllocation)
            {
                throw;
            }

            try
            {
                WriteExact(
                    processHandle,
                    callAddress,
                    originalCall,
                    "Challenge3 base-update callsite 롤백");
                foreach (var slot in slots.Reverse())
                {
                    WriteExact(
                        processHandle,
                        slot.Address,
                        UInt32Bytes(slot.Original),
                        $"Challenge3 vtable 슬롯 0x{slot.Address:X8} 롤백");
                }

                if (!NativeMethods.FlushInstructionCache(
                        processHandle,
                        Address(callAddress),
                        (nuint)originalCall.Length))
                {
                    throw NativeError("Challenge3 base-update callsite 롤백 캐시를 갱신하지 못했습니다");
                }

                foreach (var slot in slots)
                {
                    if (!TryReadUInt32(processHandle, slot.Address, out uint restored) ||
                        restored != slot.Original)
                    {
                        throw new InvalidOperationException(
                            $"Challenge3 vtable 슬롯 0x{slot.Address:X8} 롤백 검증에 실패했습니다.");
                    }
                }

                if (!Matches(processHandle, callAddress, originalCall))
                {
                    throw new InvalidOperationException(
                        "Challenge3 base-update callsite 롤백 검증에 실패했습니다.");
                }

                mayReferenceAllocation = false;
            }
            catch (Exception rollbackError)
            {
                // A partial vtable/callsite write may already reference the allocation.
                // Keep it alive rather than turning a recoverable error into a
                // guaranteed dangling function pointer.
                throw new AggregateException(
                    "Challenge3 runtime hook 패치와 롤백이 모두 실패했습니다.",
                    patchError,
                    rollbackError);
            }

            throw;
        }
        finally
        {
            if (callProtectionChanged && !NativeMethods.VirtualProtectEx(
                    processHandle,
                    Address(callAddress),
                    (nuint)patchedCall.Length,
                    oldCallProtection,
                    out _))
            {
                progress?.Report(
                    "경고: Challenge3 base-update callsite의 원래 메모리 보호를 복원하지 못했습니다. " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            if (tableProtectionChanged && !NativeMethods.VirtualProtectEx(
                    processHandle,
                    Address(tableAddress),
                    (nuint)VtablePatchLength,
                    oldTableProtection,
                    out _))
            {
                progress?.Report(
                    "경고: Challenge3 vtable의 원래 메모리 보호를 복원하지 못했습니다. " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }

        if (!patchedWritten)
        {
            throw new InvalidOperationException("Challenge3 runtime hook 패치가 완료되지 않았습니다.");
        }
    }

    private static void ValidateVtableSnapshot(byte[] table, uint moduleBase)
    {
        if (ReadUInt32(table, 0) != checked(moduleBase + Challenge3DestructorRva) ||
            ReadUInt32(table, checked((int)(VtableUpdateRva - VtableDestructorRva))) !=
                checked(moduleBase + Challenge3UpdateRva) ||
            ReadUInt32(table, checked((int)(VtableInitRva - VtableDestructorRva))) !=
                checked(moduleBase + Challenge3InitRva) ||
            ReadUInt32(table, checked((int)(VtableClearRva - VtableDestructorRva))) !=
                checked(moduleBase + Challenge3ClearRva))
        {
            throw new InvalidOperationException(
                "Challenge3 vtable이 검증 후 변경되어 L1 호환 훅을 적용하지 않았습니다.");
        }
    }

    private static bool IsInstalledRuntimeHooks(
        SafeProcessHandle processHandle,
        uint moduleBase,
        uint codeBase,
        P236L1StageShimImage image)
    {
        return TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableDestructorRva),
                out uint destructorPointer) &&
            destructorPointer == checked(codeBase + (uint)image.DestructorOffset) &&
            TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableUpdateRva),
                out uint updatePointer) &&
            updatePointer == checked(codeBase + (uint)image.UpdateOffset) &&
            TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableInitRva),
                out uint initPointer) &&
            initPointer == checked(codeBase + (uint)image.InitOffset) &&
            TryReadUInt32(
                processHandle,
                checked(moduleBase + VtableClearRva),
                out uint clearPointer) &&
            clearPointer == checked(codeBase + (uint)image.ClearOffset) &&
            Matches(
                processHandle,
                checked(moduleBase + Challenge3BaseUpdateCallRva),
                BuildRelativeCallInstruction(
                    checked(moduleBase + Challenge3BaseUpdateCallRva),
                    checked(codeBase + (uint)image.DevilBaseUpdateThunkOffset)));
    }

    internal static byte[] BuildRelativeCallInstruction(uint instructionAddress, uint targetAddress)
    {
        uint nextInstruction = unchecked(instructionAddress + 5);
        int displacement = unchecked((int)(targetAddress - nextInstruction));
        byte[] result = new byte[5];
        result[0] = 0xE8;
        BitConverter.GetBytes(displacement).CopyTo(result, 1);
        return result;
    }

    private static bool Matches(SafeProcessHandle processHandle, uint address, byte[] expected)
    {
        byte[] actual = new byte[expected.Length];
        return NativeMethods.ReadProcessMemory(
                processHandle,
                Address(address),
                actual,
                (nuint)actual.Length,
                out nuint bytesRead) &&
            bytesRead == (nuint)actual.Length &&
            actual.AsSpan().SequenceEqual(expected);
    }

    private static bool TryReadUInt32(
        SafeProcessHandle processHandle,
        uint address,
        out uint value)
    {
        byte[] bytes = new byte[sizeof(uint)];
        if (!NativeMethods.ReadProcessMemory(
                processHandle,
                Address(address),
                bytes,
                (nuint)bytes.Length,
                out nuint bytesRead) ||
            bytesRead != (nuint)bytes.Length)
        {
            value = 0;
            return false;
        }

        value = ReadUInt32(bytes, 0);
        return true;
    }

    private static byte[] ReadExact(
        SafeProcessHandle processHandle,
        uint address,
        int length,
        string description)
    {
        byte[] bytes = new byte[length];
        if (!NativeMethods.ReadProcessMemory(
                processHandle,
                Address(address),
                bytes,
                (nuint)bytes.Length,
                out nuint bytesRead) ||
            bytesRead != (nuint)bytes.Length)
        {
            throw NativeError($"{description}을(를) 읽지 못했습니다");
        }

        return bytes;
    }

    private static void WriteExact(
        SafeProcessHandle processHandle,
        uint address,
        byte[] bytes,
        string description)
    {
        if (!NativeMethods.WriteProcessMemory(
                processHandle,
                Address(address),
                bytes,
                (nuint)bytes.Length,
                out nuint bytesWritten) ||
            bytesWritten != (nuint)bytes.Length)
        {
            throw NativeError($"{description}을(를) 쓰지 못했습니다");
        }
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] |
            (bytes[offset + 1] << 8) |
            (bytes[offset + 2] << 16) |
            (bytes[offset + 3] << 24));

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static byte[] UInt32Bytes(uint value)
    {
        byte[] bytes = new byte[sizeof(uint)];
        WriteUInt32(bytes, 0, value);
        return bytes;
    }

    private static uint ToUInt32Address(IntPtr address, string description)
    {
        long value = address.ToInt64();
        if (value < 0 || (ulong)value > uint.MaxValue)
        {
            throw new InvalidOperationException(
                $"{description} 0x{unchecked((ulong)value):X}은(는) 32비트 P236 주소 범위를 벗어납니다.");
        }

        return (uint)value;
    }

    private static IntPtr Address(uint address) => new IntPtr(unchecked((long)address));

    private static Win32Exception NativeError(string operation) =>
        new Win32Exception(Marshal.GetLastWin32Error(), operation);

    private static class NativeMethods
    {
        internal const uint ProcessVmOperation = 0x0008;
        internal const uint ProcessVmRead = 0x0010;
        internal const uint ProcessVmWrite = 0x0020;
        internal const uint ProcessQueryInformation = 0x0400;

        internal const uint MemCommit = 0x1000;
        internal const uint MemReserve = 0x2000;
        internal const uint MemRelease = 0x8000;

        internal const uint PageReadWrite = 0x04;
        internal const uint PageExecuteRead = 0x20;
        internal const uint PageExecuteReadWrite = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadProcessMemory(
            SafeProcessHandle process,
            IntPtr baseAddress,
            [Out] byte[] buffer,
            nuint size,
            out nuint numberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteProcessMemory(
            SafeProcessHandle process,
            IntPtr baseAddress,
            byte[] buffer,
            nuint size,
            out nuint numberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualAllocEx(
            SafeProcessHandle process,
            IntPtr address,
            nuint size,
            uint allocationType,
            uint protection);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VirtualFreeEx(
            SafeProcessHandle process,
            IntPtr address,
            nuint size,
            uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VirtualProtectEx(
            SafeProcessHandle process,
            IntPtr address,
            nuint size,
            uint newProtection,
            out uint oldProtection);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlushInstructionCache(
            SafeProcessHandle process,
            IntPtr baseAddress,
            nuint size);
    }
}
