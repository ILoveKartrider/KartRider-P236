namespace KartRider.P236.Connector;

internal readonly record struct P236ShimCallSite(int OpcodeOffset, uint Target);

internal sealed record P236L1StageShimImage(
    byte[] Code,
    byte[] Data,
    int SetupOffset,
    int CleanupOffset,
    int DevilUpdateOffset,
    int DevilBaseUpdateThunkOffset,
    int ArrowUpdateOffset,
    int UpdateOffset,
    int InitOffset,
    int ClearOffset,
    int DestructorOffset,
    IReadOnlyList<P236ShimCallSite> CallSites);

/// <summary>
/// Builds the small x86 shim installed in a validated 2005-12-14 (P236) client.
/// The shim keeps Challenge3Stage's UI and completion logic while restoring
/// the P236-compatible pieces needed by selected L1 missions:
/// 0x41 Devil track markers, 0x44 native Challenge3 arrows, and 0x45 Factory GOs.
/// </summary>
internal static class P236L1StageShimAssembler
{
    internal const int CodePageSize = 0x1000;
    internal const int DataPageSize = 0x1000;
    internal const int AllocationSize = CodePageSize + DataPageSize;

    internal const int FactoryOwnerDataOffset = 0x00;
    internal const int MissionOwnerDataOffset = 0x04;
    internal const int MissionFlagsDataOffset = 0x08;
    internal const int Devil0StringDataOffset = 0x20;
    internal const int Devil1StringDataOffset = 0x40;
    internal const int DataImageLength = 0x60;

    internal const byte Devil0TriggeredFlag = 0x01;
    internal const byte Devil1TriggeredFlag = 0x02;

    internal static P236L1StageShimImage Build(uint codeBase, uint dataBase, uint moduleBase)
    {
        uint minimumDataBase = checked(codeBase + (uint)CodePageSize);
        if (dataBase < minimumDataBase)
        {
            throw new ArgumentOutOfRangeException(nameof(dataBase), "The writable state must follow the code page.");
        }

        uint factoryOwnerAddress = checked(dataBase + FactoryOwnerDataOffset);
        uint missionOwnerAddress = checked(dataBase + MissionOwnerDataOffset);
        uint missionFlagsAddress = checked(dataBase + MissionFlagsDataOffset);
        uint devil0StringAddress = checked(dataBase + Devil0StringDataOffset);
        uint devil1StringAddress = checked(dataBase + Devil1StringDataOffset);
        byte[] data = BuildDataImage();
        X86Emitter emitter = new X86Emitter(codeBase);

        int setupOffset = emitter.Position;
        EmitFactorySetup(emitter, moduleBase);
        emitter.Align(16);

        int cleanupOffset = emitter.Position;
        EmitLifecycleCleanup(
            emitter,
            moduleBase,
            factoryOwnerAddress,
            missionOwnerAddress,
            missionFlagsAddress);
        emitter.Align(16);

        int devilUpdateOffset = emitter.Position;
        EmitDevilUpdate(
            emitter,
            moduleBase,
            missionFlagsAddress,
            devil0StringAddress,
            devil1StringAddress);
        emitter.Align(16);

        int devilBaseUpdateThunkOffset = emitter.Position;
        EmitDevilBaseUpdateThunk(
            emitter,
            moduleBase,
            missionOwnerAddress,
            checked(codeBase + (uint)devilUpdateOffset));
        emitter.Align(16);

        int arrowUpdateOffset = emitter.Position;
        EmitArrowUpdate(emitter);
        emitter.Align(16);

        int updateOffset = emitter.Position;
        EmitUpdateWrapper(
            emitter,
            moduleBase,
            missionOwnerAddress,
            checked(codeBase + (uint)arrowUpdateOffset));
        emitter.Align(16);

        int initOffset = emitter.Position;
        EmitInitWrapper(
            emitter,
            moduleBase,
            factoryOwnerAddress,
            missionOwnerAddress,
            missionFlagsAddress,
            checked(codeBase + (uint)setupOffset));
        emitter.Align(16);

        int clearOffset = emitter.Position;
        EmitClearWrapper(
            emitter,
            moduleBase,
            checked(codeBase + (uint)cleanupOffset));
        emitter.Align(16);

        int destructorOffset = emitter.Position;
        EmitDestructorWrapper(
            emitter,
            moduleBase,
            checked(codeBase + (uint)cleanupOffset));

        if (emitter.Position > CodePageSize)
        {
            throw new InvalidOperationException(
                $"The P236 L1 shim is {emitter.Position} bytes and no longer fits in its code page.");
        }

        return new P236L1StageShimImage(
            emitter.ToArray(),
            data,
            setupOffset,
            cleanupOffset,
            devilUpdateOffset,
            devilBaseUpdateThunkOffset,
            arrowUpdateOffset,
            updateOffset,
            initOffset,
            clearOffset,
            destructorOffset,
            emitter.CallSites.ToArray());
    }

    private static byte[] BuildDataImage()
    {
        byte[] data = new byte[DataImageLength];
        System.Text.Encoding.Unicode.GetBytes("devil0\0")
            .CopyTo(data, Devil0StringDataOffset);
        System.Text.Encoding.Unicode.GetBytes("devil1\0")
            .CopyTo(data, Devil1StringDataOffset);
        return data;
    }

    private static void EmitFactorySetup(X86Emitter code, uint moduleBase)
    {
        // Use the original TimeAttack frame size and local offsets. This keeps
        // the game's non-trivial string/shared-pointer ABI byte-for-byte close
        // to the verified 0x56F7B3..0x56F923 sequence.
        code.Bytes(0x55, 0x8B, 0xEC);                         // push ebp; mov ebp,esp
        code.Bytes(0x81, 0xEC).UInt32(0x134);                // sub esp,134h

        code.LeaEcxFromEbp(-0x2C);
        code.Call(Rva(moduleBase, 0x1CA920));                // shared zero-ctor

        EmitResourceRegistration(
            code,
            moduleBase,
            keyRva: 0x39FCBC,                               // L"obstacle"
            firstStringOffset: -0xF8,
            firstTemporaryOffset: -0xFC,
            secondStringOffset: -0x104,
            secondTemporaryOffset: -0x108,
            managerRva: 0x0C4A90);

        EmitResourceRegistration(
            code,
            moduleBase,
            keyRva: 0x3A0268,                               // L"dummy"
            firstStringOffset: -0x110,
            firstTemporaryOffset: -0x114,
            secondStringOffset: -0x11C,
            secondTemporaryOffset: -0x120,
            managerRva: 0x0BA250);

        code.LeaEcxFromEbp(-0x2C);
        code.Call(Rva(moduleBase, 0x0AF810));                // shared dtor

        code.Bytes(0x6A, 0x01);                             // push 1
        code.Bytes(0x8B, 0x4D, 0x08);                       // mov ecx,[ebp+8] (stage)
        code.Call(Rva(moduleBase, 0x1009B0));                // instantiate track GOs

        code.Bytes(0x8B, 0xE5, 0x5D, 0xC2, 0x04, 0x00);     // leave; ret 4
    }

    private static void EmitResourceRegistration(
        X86Emitter code,
        uint moduleBase,
        uint keyRva,
        int firstStringOffset,
        int firstTemporaryOffset,
        int secondStringOffset,
        int secondTemporaryOffset,
        uint managerRva)
    {
        uint keyAddress = Rva(moduleBase, keyRva);
        uint registryAddress = Rva(moduleBase, 0x3FB6C6);

        code.Byte(0x68).UInt32(keyAddress);                  // push key
        code.LeaEcxFromEbp(firstStringOffset);
        code.Call(Rva(moduleBase, 0x020FE0));                // game string ctor

        code.Byte(0x68).UInt32(keyAddress);                  // push key
        code.LeaEcxFromEbp(secondStringOffset);
        code.Call(Rva(moduleBase, 0x020FE0));

        code.Bytes(0x6A, 0x00);                             // push 0
        code.LeaEcxFromEbp(firstStringOffset);
        code.Byte(0x51);                                    // push ecx
        code.LeaEdxFromEbp(firstTemporaryOffset);
        code.Byte(0x52);                                    // push edx
        code.Byte(0xB9).UInt32(registryAddress);             // mov ecx,registry
        code.Call(Rva(moduleBase, 0x02C4A0));
        code.Bytes(0x8B, 0xC8);                             // mov ecx,eax
        code.Call(Rva(moduleBase, 0x07EE40));
        code.Bytes(0x8B, 0x00, 0x50, 0x51, 0x8B, 0xCC);     // mov eax,[eax]; push eax/ecx; mov ecx,esp
        code.LeaEdxFromEbp(secondStringOffset);
        code.Bytes(0x52, 0x51);                             // push edx; push ecx
        code.Byte(0xB9).UInt32(registryAddress);
        code.Call(Rva(moduleBase, 0x02C4A0));
        code.Bytes(0x8B, 0xC8);
        code.Call(Rva(moduleBase, 0x07EB90));
        code.LeaEaxFromEbp(secondTemporaryOffset);
        code.Byte(0x50);                                    // push eax
        code.Call(Rva(moduleBase, 0x28EFF0));
        code.Bytes(0x83, 0xC4, 0x10, 0x50);                 // add esp,10h; push eax
        code.LeaEcxFromEbp(-0x2C);
        code.Call(Rva(moduleBase, 0x14E150));                // assign shared

        code.LeaEcxFromEbp(secondTemporaryOffset);
        code.Call(Rva(moduleBase, 0x0AF810));
        code.LeaEcxFromEbp(firstTemporaryOffset);
        code.Call(Rva(moduleBase, 0x0AF810));
        code.LeaEcxFromEbp(secondStringOffset);
        code.Call(Rva(moduleBase, 0x004F10));
        code.LeaEcxFromEbp(firstStringOffset);
        code.Call(Rva(moduleBase, 0x004F10));

        code.Byte(0x51);                                    // reserve by-value shared pointer
        code.Bytes(0x8B, 0xCC);                             // mov ecx,esp
        code.LeaEdxFromEbp(-0x2C);
        code.Byte(0x52);                                    // push edx
        code.Call(Rva(moduleBase, 0x1CA3A0));                // shared copy-ctor
        code.Call(Rva(moduleBase, managerRva));
        code.Bytes(0x83, 0xC4, 0x04);                       // add esp,4
    }

    private static void EmitLifecycleCleanup(
        X86Emitter code,
        uint moduleBase,
        uint factoryOwnerAddress,
        uint missionOwnerAddress,
        uint missionFlagsAddress)
    {
        code.Bytes(0x55, 0x8B, 0xEC);                       // push ebp; mov ebp,esp
        code.Byte(0xA1).UInt32(factoryOwnerAddress);         // mov eax,[factory owner]
        code.Bytes(0x3B, 0x45, 0x08);                       // cmp eax,[ebp+8]
        int notFactoryOwner = code.NearJne();

        code.Call(Rva(moduleBase, 0x0C4AC0));                // obstacle clear
        code.Call(Rva(moduleBase, 0x0BA280));                // dummy clear
        code.Bytes(0xC7, 0x05).UInt32(factoryOwnerAddress).UInt32(0);

        code.PatchRelativeToCurrent(notFactoryOwner);
        code.Byte(0xA1).UInt32(missionOwnerAddress);         // mov eax,[mission owner]
        code.Bytes(0x3B, 0x45, 0x08);                       // cmp eax,[ebp+8]
        int notMissionOwner = code.NearJne();
        code.Bytes(0xC7, 0x05).UInt32(missionOwnerAddress).UInt32(0);
        code.Bytes(0xC7, 0x05).UInt32(missionFlagsAddress).UInt32(0);

        code.PatchRelativeToCurrent(notMissionOwner);
        code.Bytes(0x5D, 0xC2, 0x04, 0x00);                 // pop ebp; ret 4
    }

    private static void EmitDevilUpdate(
        X86Emitter code,
        uint moduleBase,
        uint missionFlagsAddress,
        uint devil0StringAddress,
        uint devil1StringAddress)
    {
        // stdcall helper(stage, effectiveNow). The current named track event is copied
        // into a normal game string, compared with the two L1 marker names, and
        // released exactly once. Each marker can trigger only once per stage.
        code.Bytes(0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x18);

        code.Bytes(0x8B, 0x4D, 0x08, 0x81, 0xC1).UInt32(0x134);
        code.Call(Rva(moduleBase, 0x025B50));                // resolve local rider
        code.Bytes(0x85, 0xC0);                             // rider must exist
        int noRider = code.NearJe();
        code.Bytes(0x8B, 0x10, 0x8B, 0xC8, 0xFF, 0x52, 0x54); // rider->hasNamedEvent()
        code.Bytes(0x84, 0xC0);                             // test al,al
        int noNamedEvent = code.NearJe();

        code.Byte(0x68).UInt32(devil0StringAddress);        // expected raw UTF-16
        code.Bytes(0x8B, 0x4D, 0x08, 0x81, 0xC1).UInt32(0x134);
        code.Call(Rva(moduleBase, 0x025B50));
        code.LeaEdxFromEbp(-0x18);                          // output game string
        code.Byte(0x52);
        code.Bytes(0x8B, 0x10, 0x8B, 0xC8, 0xFF, 0x52, 0x5C);
        code.Bytes(0x89, 0x45, 0xF8, 0x50);                 // save/push returned string
        code.Call(Rva(moduleBase, 0x02AFE0));               // equals(raw, devil0)
        code.Bytes(0x83, 0xC4, 0x08, 0x84, 0xC0);
        int matchedDevil0 = code.NearJne();

        code.Byte(0x68).UInt32(devil1StringAddress);
        code.Bytes(0xFF, 0x75, 0xF8);
        code.Call(Rva(moduleBase, 0x02AFE0));               // equals(raw, devil1)
        code.Bytes(0x83, 0xC4, 0x08, 0x84, 0xC0);
        int noDevilMatch = code.NearJe();
        code.Bytes(0xC6, 0x45, 0xF4, Devil1TriggeredFlag);  // selected flag mask
        int haveFlagMask = code.NearJump();

        code.PatchRelativeToCurrent(matchedDevil0);
        code.Bytes(0xC6, 0x45, 0xF4, Devil0TriggeredFlag);

        code.PatchRelativeToCurrent(haveFlagMask);
        code.Bytes(0x8A, 0x45, 0xF4);                       // al=selected flag
        code.Bytes(0x84, 0x05).UInt32(missionFlagsAddress); // test [flags],al
        int alreadyTriggered = code.NearJne();

        code.Byte(0x68).UInt32(0x104);
        code.Call(Rva(moduleBase, 0x26ADDB));               // allocate GoItemDevil
        code.Bytes(0x83, 0xC4, 0x04, 0x85, 0xC0);
        int allocationFailed = code.NearJe();
        code.Bytes(0x8B, 0xC8);
        code.Call(Rva(moduleBase, 0x0B7DB0));               // GoItemDevil ctor

        code.Bytes(0x6A, 0x01, 0x50);
        code.LeaEcxFromEbp(-0x04);
        code.Call(Rva(moduleBase, 0x1CA690));               // local shared pointer

        code.Bytes(0x6A, 0x01);
        code.LeaEcxFromEbp(-0x04);
        code.Call(Rva(moduleBase, 0x025B50));
        code.Bytes(0x8B, 0xC8);
        code.Call(Rva(moduleBase, 0x245230));               // visible/active

        code.Bytes(0x8B, 0x4D, 0x08, 0x81, 0xC1).UInt32(0x134);
        code.Call(Rva(moduleBase, 0x025B50));
        code.Byte(0x50);                                    // target rider
        code.LeaEcxFromEbp(-0x04);
        code.Call(Rva(moduleBase, 0x025B50));
        code.Bytes(0x8B, 0xC8);
        code.Call(Rva(moduleBase, 0x0A8250));               // set target

        code.Bytes(0xFF, 0x75, 0x0C);                       // effective stage time
        code.Bytes(0x6A, 0x01);
        code.LeaEcxFromEbp(-0x04);
        code.Call(Rva(moduleBase, 0x025B50));
        code.Bytes(0x8B, 0xC8);
        code.Call(Rva(moduleBase, 0x0A83D0));               // state=1, start time

        code.Byte(0x51);                                    // by-value shared pointer
        code.Bytes(0x8B, 0xCC);
        code.LeaEdxFromEbp(-0x04);
        code.Byte(0x52);
        code.Call(Rva(moduleBase, 0x1CA3A0));
        code.Byte(0xB9).UInt32(Rva(moduleBase, 0x403318));
        code.Call(Rva(moduleBase, 0x0AAFA0));
        code.Bytes(0x8B, 0xC8);
        code.Call(Rva(moduleBase, 0x08F940));               // object manager register

        code.Bytes(0x8A, 0x45, 0xF4);
        code.Bytes(0x08, 0x05).UInt32(missionFlagsAddress); // or [flags],al
        code.LeaEcxFromEbp(-0x04);
        code.Call(Rva(moduleBase, 0x0AF810));               // release local shared ptr

        code.PatchRelativeToCurrent(allocationFailed);
        code.PatchRelativeToCurrent(alreadyTriggered);
        code.PatchRelativeToCurrent(noDevilMatch);
        code.LeaEcxFromEbp(-0x18);
        code.Call(Rva(moduleBase, 0x004F10));               // release copied event name

        code.PatchRelativeToCurrent(noNamedEvent);
        code.PatchRelativeToCurrent(noRider);
        code.Bytes(0x8B, 0xE5, 0x5D, 0xC2, 0x08, 0x00);     // leave; ret 8
    }

    private static void EmitDevilBaseUpdateThunk(
        X86Emitter code,
        uint moduleBase,
        uint missionOwnerAddress,
        uint devilUpdateAddress)
    {
        // Drop-in replacement for Challenge3Stage's call to GameStage::Update.
        // The collision event is a one-frame GoPlayKart field, so inspect it
        // immediately after the native base update and before Challenge3Stage
        // runs the rest of its comparatively long update routine.
        code.Bytes(0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08);
        code.Bytes(0x89, 0x4D, 0xFC);                       // [ebp-4]=stage
        code.Bytes(0xFF, 0x75, 0x08);
        code.Bytes(0x8B, 0x4D, 0xFC);
        code.Call(Rva(moduleBase, 0x1020A0));               // original GameStage::Update
        code.Bytes(0x89, 0x45, 0xF8);                       // preserve original EAX

        code.Bytes(0x8B, 0x4D, 0xFC);
        code.Bytes(0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x41);
        int notDevilMission = code.NearJne();
        code.Byte(0xA1).UInt32(missionOwnerAddress);
        code.Bytes(0x3B, 0x45, 0xFC);                       // initialized owner == stage?
        int notMissionOwner = code.NearJne();
        code.Bytes(0xFF, 0x75, 0x08, 0x51);                 // effectiveNow, stage
        code.Call(devilUpdateAddress);

        code.PatchRelativeToCurrent(notMissionOwner);
        code.PatchRelativeToCurrent(notDevilMission);
        code.Bytes(0x8B, 0x45, 0xF8);
        code.Bytes(0x8B, 0xE5, 0x5D, 0xC2, 0x04, 0x00);     // leave; ret 4
    }

    private static void EmitArrowUpdate(X86Emitter code)
    {
        // stdcall helper(stage). P236 exposes an absolute route/segment value,
        // not Challenge4's later-client disappearArrowIdx counter. Leave the
        // native Challenge3 state machine in sole control of arrow visibility.
        code.Bytes(0x55, 0x8B, 0xEC, 0x5D, 0xC2, 0x04, 0x00);
    }

    private static void EmitUpdateWrapper(
        X86Emitter code,
        uint moduleBase,
        uint missionOwnerAddress,
        uint arrowUpdateAddress)
    {
        code.Bytes(0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08);
        code.Bytes(0x89, 0x4D, 0xFC);                       // [ebp-4]=self
        code.Bytes(0xFF, 0x75, 0x08);
        code.Bytes(0x8B, 0x4D, 0xFC);
        code.Call(Rva(moduleBase, 0x164FB0));               // original update first
        code.Bytes(0x89, 0x45, 0xF8);                       // preserve original EAX

        code.Byte(0xA1).UInt32(missionOwnerAddress);
        code.Bytes(0x3B, 0x45, 0xFC);                       // owner == self?
        int notMissionOwner = code.NearJne();

        code.Bytes(0x8B, 0x4D, 0xFC);
        code.Bytes(0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x44);
        int notArrowMission = code.NearJne();
        code.Byte(0x51);
        code.Call(arrowUpdateAddress);

        code.PatchRelativeToCurrent(notArrowMission);
        code.PatchRelativeToCurrent(notMissionOwner);
        code.Bytes(0x8B, 0x45, 0xF8);
        code.Bytes(0x8B, 0xE5, 0x5D, 0xC2, 0x04, 0x00);     // leave; ret 4
    }

    private static void EmitInitWrapper(
        X86Emitter code,
        uint moduleBase,
        uint factoryOwnerAddress,
        uint missionOwnerAddress,
        uint missionFlagsAddress,
        uint setupAddress)
    {
        code.Bytes(0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08);     // frame + two locals
        code.Bytes(0x89, 0x4D, 0xFC);                       // [ebp-4]=self
        code.Bytes(0xFF, 0x75, 0x08);                       // push raw a2 word once
        code.Bytes(0x8B, 0x4D, 0xFC);                       // ecx=self
        code.Call(Rva(moduleBase, 0x162350));                // original Challenge3 init
        code.Bytes(0x89, 0x45, 0xF8);                       // save return value

        code.Bytes(0x8B, 0x4D, 0xFC);
        code.Bytes(0x89, 0x0D).UInt32(missionOwnerAddress); // state owner=self
        code.Bytes(0xC7, 0x05).UInt32(missionFlagsAddress).UInt32(0);

        code.Bytes(0x8B, 0x4D, 0xFC);                       // ecx=self
        code.Bytes(0x80, 0xB9, 0x98, 0x04, 0x00, 0x00, 0x45); // mission id == 45h?
        int notFactoryMission = code.NearJne();
        code.Bytes(0x83, 0x3D).UInt32(factoryOwnerAddress).Byte(0x00); // owner must be null
        int alreadyOwned = code.NearJne();

        code.Byte(0x51);                                    // push self
        code.Call(setupAddress);
        code.Bytes(0x8B, 0x4D, 0xFC);                       // ecx=self
        code.Bytes(0x89, 0x0D).UInt32(factoryOwnerAddress); // owner=self

        code.PatchRelativeToCurrent(alreadyOwned);
        code.PatchRelativeToCurrent(notFactoryMission);
        code.Bytes(0x8B, 0x45, 0xF8);                       // restore original return
        code.Bytes(0x8B, 0xE5, 0x5D, 0xC2, 0x04, 0x00);     // leave; ret 4
    }

    private static void EmitClearWrapper(X86Emitter code, uint moduleBase, uint cleanupAddress)
    {
        code.Bytes(0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08);
        code.Bytes(0x89, 0x4D, 0xFC);                       // [ebp-4]=self
        code.Bytes(0x8B, 0x4D, 0xFC);
        code.Call(Rva(moduleBase, 0x1635C0));                // original clear first
        code.Bytes(0x89, 0x45, 0xF8);
        code.Bytes(0xFF, 0x75, 0xFC);                       // push self
        code.Call(cleanupAddress);
        code.Bytes(0x8B, 0x45, 0xF8);
        code.Bytes(0x8B, 0xE5, 0x5D, 0xC3);                 // leave; ret
    }

    private static void EmitDestructorWrapper(X86Emitter code, uint moduleBase, uint cleanupAddress)
    {
        code.Bytes(0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x04);
        code.Bytes(0x89, 0x4D, 0xFC);                       // [ebp-4]=self
        code.Bytes(0xFF, 0x75, 0xFC);                       // cleanup before dtor
        code.Call(cleanupAddress);
        code.Bytes(0xFF, 0x75, 0x08);                       // push dtor flags
        code.Bytes(0x8B, 0x4D, 0xFC);
        code.Call(Rva(moduleBase, 0x166070));                // original dtor
        code.Bytes(0x8B, 0xE5, 0x5D, 0xC2, 0x04, 0x00);     // leave; ret 4
    }

    private static uint Rva(uint moduleBase, uint rva) => checked(moduleBase + rva);

    private sealed class X86Emitter
    {
        private readonly uint _baseAddress;
        private readonly List<byte> _bytes = new List<byte>();
        private readonly List<P236ShimCallSite> _callSites = new List<P236ShimCallSite>();

        internal X86Emitter(uint baseAddress)
        {
            _baseAddress = baseAddress;
        }

        internal int Position => _bytes.Count;
        internal IReadOnlyList<P236ShimCallSite> CallSites => _callSites;

        internal X86Emitter Byte(byte value)
        {
            _bytes.Add(value);
            return this;
        }

        internal X86Emitter Bytes(params byte[] values)
        {
            _bytes.AddRange(values);
            return this;
        }

        internal X86Emitter UInt32(uint value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 24));
            return this;
        }

        internal void Call(uint target)
        {
            int opcodeOffset = Position;
            Byte(0xE8);
            long nextInstruction = (long)_baseAddress + Position + sizeof(int);
            long displacement = (long)target - nextInstruction;
            if (displacement is < int.MinValue or > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"x86 call from 0x{nextInstruction - 5:X8} to 0x{target:X8} is out of range.");
            }

            UInt32(unchecked((uint)(int)displacement));
            _callSites.Add(new P236ShimCallSite(opcodeOffset, target));
        }

        internal int NearJne()
        {
            Bytes(0x0F, 0x85);
            int displacementOffset = Position;
            UInt32(0);
            return displacementOffset;
        }

        internal int NearJe()
        {
            Bytes(0x0F, 0x84);
            int displacementOffset = Position;
            UInt32(0);
            return displacementOffset;
        }

        internal int NearJb()
        {
            Bytes(0x0F, 0x82);
            int displacementOffset = Position;
            UInt32(0);
            return displacementOffset;
        }

        internal int NearJump()
        {
            Byte(0xE9);
            int displacementOffset = Position;
            UInt32(0);
            return displacementOffset;
        }

        internal void PatchRelativeToCurrent(int displacementOffset)
        {
            int displacement = checked(Position - (displacementOffset + sizeof(int)));
            WriteInt32(displacementOffset, displacement);
        }

        internal void LeaEaxFromEbp(int offset) => LeaFromEbp(0x85, offset);
        internal void LeaEcxFromEbp(int offset) => LeaFromEbp(0x8D, offset);
        internal void LeaEdxFromEbp(int offset) => LeaFromEbp(0x95, offset);

        internal void Align(int alignment)
        {
            while ((Position % alignment) != 0)
            {
                Byte(0x90);
            }
        }

        internal byte[] ToArray() => _bytes.ToArray();

        private void LeaFromEbp(byte modRm, int offset)
        {
            Bytes(0x8D, modRm);
            UInt32(unchecked((uint)offset));
        }

        private void WriteInt32(int offset, int value)
        {
            _bytes[offset] = (byte)value;
            _bytes[offset + 1] = (byte)(value >> 8);
            _bytes[offset + 2] = (byte)(value >> 16);
            _bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
