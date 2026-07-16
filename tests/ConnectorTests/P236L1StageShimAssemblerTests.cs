using Xunit;

namespace KartRider.P236.Connector.Tests;

public sealed class P236L1StageShimAssemblerTests
{
    private const uint CodeBase = 0x12000000;
    private const uint DataBase = CodeBase + P236L1StageShimAssembler.CodePageSize;
    private const uint ModuleBase = 0x00400000;

    [Fact]
    public void BuildProducesRelocatableOnePageShimWithValidatedEntries()
    {
        P236L1StageShimImage image = P236L1StageShimAssembler.Build(
            CodeBase,
            DataBase,
            ModuleBase);

        Assert.NotEmpty(image.Code);
        Assert.Equal(P236L1StageShimAssembler.DataImageLength, image.Data.Length);
        Assert.True(image.Code.Length <= P236L1StageShimAssembler.CodePageSize);
        Assert.Equal(0, image.SetupOffset);
        Assert.Equal(0, image.CleanupOffset % 16);
        Assert.Equal(0, image.DevilUpdateOffset % 16);
        Assert.Equal(0, image.DevilBaseUpdateThunkOffset % 16);
        Assert.Equal(0, image.ArrowUpdateOffset % 16);
        Assert.Equal(0, image.UpdateOffset % 16);
        Assert.Equal(0, image.InitOffset % 16);
        Assert.Equal(0, image.ClearOffset % 16);
        Assert.Equal(0, image.DestructorOffset % 16);
        Assert.Equal(0x55, image.Code[image.SetupOffset]);
        Assert.Equal(0x55, image.Code[image.CleanupOffset]);
        Assert.Equal(0x55, image.Code[image.DevilUpdateOffset]);
        Assert.Equal(0x55, image.Code[image.DevilBaseUpdateThunkOffset]);
        Assert.Equal(0x55, image.Code[image.ArrowUpdateOffset]);
        Assert.Equal(0x55, image.Code[image.UpdateOffset]);
        Assert.Equal(0x55, image.Code[image.InitOffset]);
        Assert.Equal(0x55, image.Code[image.ClearOffset]);
        Assert.Equal(0x55, image.Code[image.DestructorOffset]);

        foreach (P236ShimCallSite callSite in image.CallSites)
        {
            Assert.Equal(0xE8, image.Code[callSite.OpcodeOffset]);
            int displacement = BitConverter.ToInt32(image.Code, callSite.OpcodeOffset + 1);
            long nextInstruction = (long)CodeBase + callSite.OpcodeOffset + 5;
            uint decodedTarget = checked((uint)(nextInstruction + displacement));
            Assert.Equal(callSite.Target, decodedTarget);
        }
    }

    [Fact]
    public void BuildContainsL1MissionGuardsAndExpectedLifecycleCalls()
    {
        P236L1StageShimImage image = P236L1StageShimAssembler.Build(
            CodeBase,
            DataBase,
            ModuleBase);
        uint setupAddress = CodeBase + (uint)image.SetupOffset;
        uint cleanupAddress = CodeBase + (uint)image.CleanupOffset;
        uint devilUpdateAddress = CodeBase + (uint)image.DevilUpdateOffset;
        uint devilBaseUpdateThunkAddress = CodeBase + (uint)image.DevilBaseUpdateThunkOffset;
        uint arrowUpdateAddress = CodeBase + (uint)image.ArrowUpdateOffset;
        uint[] calls = image.CallSites.Select(call => call.Target).ToArray();

        Assert.True(Contains(
            image.Code,
            new byte[] { 0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x41 }));
        Assert.True(Contains(
            image.Code,
            new byte[] { 0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x44 }));
        Assert.True(Contains(
            image.Code,
            new byte[] { 0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x45 }));
        byte[] activeRaceGuard = { 0x83, 0xB9, 0xBC, 0x04, 0x00, 0x00, 0x02 };
        byte[] devilMissionGuard = { 0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x41 };
        byte[] arrowMissionGuard = { 0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x44 };
        P236ShimCallSite baseUpdateCall = Assert.Single(
            image.CallSites.Where(call => call.Target == ModuleBase + 0x1020A0));
        P236ShimCallSite originalUpdateCall = Assert.Single(
            image.CallSites.Where(call => call.Target == ModuleBase + 0x164FB0));
        P236ShimCallSite earlyDevilDispatch = Assert.Single(
            image.CallSites.Where(call =>
                call.Target == devilUpdateAddress &&
                call.OpcodeOffset >= image.DevilBaseUpdateThunkOffset &&
                call.OpcodeOffset < image.ArrowUpdateOffset));
        int earlyDevilGuardOffset = IndexOf(
            image.Code,
            devilMissionGuard,
            image.DevilBaseUpdateThunkOffset);
        int lateArrowGuardOffset = IndexOf(
            image.Code,
            arrowMissionGuard,
            image.UpdateOffset);

        Assert.InRange(baseUpdateCall.OpcodeOffset,
            image.DevilBaseUpdateThunkOffset,
            image.ArrowUpdateOffset - 1);
        Assert.True(baseUpdateCall.OpcodeOffset < earlyDevilGuardOffset);
        Assert.True(earlyDevilGuardOffset < earlyDevilDispatch.OpcodeOffset);
        Assert.InRange(originalUpdateCall.OpcodeOffset, image.UpdateOffset, image.InitOffset - 1);
        Assert.InRange(lateArrowGuardOffset, image.UpdateOffset, image.InitOffset - 1);
        Assert.False(Contains(
            image.Code[image.UpdateOffset..image.InitOffset],
            devilMissionGuard));
        Assert.False(Contains(
            image.Code[image.UpdateOffset..image.InitOffset],
            activeRaceGuard));
        Assert.True(Contains(
            image.Code[image.DevilUpdateOffset..image.DevilBaseUpdateThunkOffset],
            new byte[] { 0x85, 0xC0, 0x0F, 0x84 }));         // resolved rider null guard

        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x0C4A90));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x0BA250));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x1009B0));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x0C4AC0));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x0BA280));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x162350));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x1635C0));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x166070));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x164FB0));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x1020A0));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x0B7DB0));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x0A8250));
        Assert.Equal(1, calls.Count(target => target == ModuleBase + 0x0A83D0));
        Assert.DoesNotContain(calls, target => target == ModuleBase + 0x087770);
        Assert.DoesNotContain(calls, target => target == ModuleBase + 0x08F560);
        Assert.Equal(1, calls.Count(target => target == setupAddress));
        Assert.Equal(1, calls.Count(target => target == devilUpdateAddress));
        Assert.DoesNotContain(calls, target => target == devilBaseUpdateThunkAddress);
        Assert.Equal(1, calls.Count(target => target == arrowUpdateAddress));
        Assert.Equal(2, calls.Count(target => target == cleanupAddress));

        Assert.Equal(
            "devil0\0",
            System.Text.Encoding.Unicode.GetString(
                image.Data,
                P236L1StageShimAssembler.Devil0StringDataOffset,
                "devil0\0".Length * sizeof(char)));
        Assert.Equal(
            "devil1\0",
            System.Text.Encoding.Unicode.GetString(
                image.Data,
                P236L1StageShimAssembler.Devil1StringDataOffset,
                "devil1\0".Length * sizeof(char)));
    }

    [Fact]
    public void ArrowCompatibilityThunkLeavesNativeVisibilityUntouched()
    {
        P236L1StageShimImage image = P236L1StageShimAssembler.Build(
            CodeBase,
            DataBase,
            ModuleBase);
        byte[] arrowCode = image.Code[image.ArrowUpdateOffset..image.UpdateOffset];

        Assert.Equal(
            new byte[] { 0x55, 0x8B, 0xEC, 0x5D, 0xC2, 0x04, 0x00 },
            arrowCode[..7]);
        Assert.All(arrowCode[7..], value => Assert.Equal(0x90, value));
        Assert.DoesNotContain(
            image.CallSites,
            call => call.OpcodeOffset >= image.ArrowUpdateOffset &&
                call.OpcodeOffset < image.UpdateOffset);
    }

    [Fact]
    public void BuildRelocatesCallsAndWritableOwnerState()
    {
        P236L1StageShimImage first = P236L1StageShimAssembler.Build(
            CodeBase,
            DataBase,
            ModuleBase);
        const uint relocatedCode = 0x22000000;
        uint relocatedData = relocatedCode + P236L1StageShimAssembler.CodePageSize;
        P236L1StageShimImage second = P236L1StageShimAssembler.Build(
            relocatedCode,
            relocatedData,
            ModuleBase);

        Assert.Equal(first.Code.Length, second.Code.Length);
        Assert.Equal(
            first.CallSites.Where(call => call.Target < CodeBase).Select(call => call.Target),
            second.CallSites.Where(call => call.Target < relocatedCode).Select(call => call.Target));
        Assert.Contains(first.CallSites, call => call.Target == CodeBase + (uint)first.SetupOffset);
        Assert.Contains(second.CallSites, call => call.Target == relocatedCode + (uint)second.SetupOffset);
        Assert.Contains(first.CallSites, call => call.Target == CodeBase + (uint)first.DevilUpdateOffset);
        Assert.Contains(second.CallSites, call => call.Target == relocatedCode + (uint)second.DevilUpdateOffset);
        Assert.Equal(first.DevilBaseUpdateThunkOffset, second.DevilBaseUpdateThunkOffset);
        Assert.Contains(first.CallSites, call => call.Target == ModuleBase + 0x1020A0);
        Assert.Contains(second.CallSites, call => call.Target == ModuleBase + 0x1020A0);
        Assert.Contains(first.CallSites, call => call.Target == CodeBase + (uint)first.ArrowUpdateOffset);
        Assert.Contains(second.CallSites, call => call.Target == relocatedCode + (uint)second.ArrowUpdateOffset);
        Assert.Equal(
            2,
            second.CallSites.Count(call => call.Target == relocatedCode + (uint)second.CleanupOffset));
        Assert.Equal(first.Data, second.Data);
        Assert.False(first.Code.SequenceEqual(second.Code));
        Assert.True(Contains(first.Code, LittleEndian(DataBase)));
        Assert.True(Contains(second.Code, LittleEndian(relocatedData)));
    }

    [Fact]
    public void RuntimeCallPatchTargetsEarlyDevilBaseUpdateThunk()
    {
        P236L1StageShimImage image = P236L1StageShimAssembler.Build(
            CodeBase,
            DataBase,
            ModuleBase);
        uint callsite = ModuleBase + 0x16509F;

        Assert.Equal(
            new byte[] { 0xE8, 0xFC, 0xCF, 0xF9, 0xFF },
            P236RuntimePatchService.BuildRelativeCallInstruction(
                callsite,
                ModuleBase + 0x1020A0));

        byte[] patched = P236RuntimePatchService.BuildRelativeCallInstruction(
            callsite,
            CodeBase + (uint)image.DevilBaseUpdateThunkOffset);
        Assert.Equal(0xE8, patched[0]);
        int displacement = BitConverter.ToInt32(patched, 1);
        uint decodedTarget = unchecked(callsite + 5u + (uint)displacement);
        Assert.Equal(CodeBase + (uint)image.DevilBaseUpdateThunkOffset, decodedTarget);
    }

    [Fact]
    public void BuildRejectsStateInsideCodePage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            P236L1StageShimAssembler.Build(CodeBase, CodeBase + 0x800, ModuleBase));
        Assert.Throws<OverflowException>(() =>
            P236L1StageShimAssembler.Build(uint.MaxValue - 0x800, uint.MaxValue, ModuleBase));
    }

    private static byte[] LittleEndian(uint value) =>
        new[]
        {
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24)
        };

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        for (int index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int startIndex)
    {
        for (int index = startIndex; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }
        }

        return -1;
    }
}
