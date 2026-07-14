using System.Runtime.InteropServices;

namespace KartRider2005MutexRelease;

internal sealed class SystemHandleSnapshot : IDisposable
{
    private const int InitialBufferSize = 1 * 1024 * 1024;
    private const int MaximumBufferSize = 256 * 1024 * 1024;

    private static readonly int HeaderSize = IntPtr.Size * 2;
    private static readonly int EntrySize = Marshal.SizeOf<SystemHandleTableEntryInfoEx>();

    private IntPtr _buffer;
    private readonly int _entryCount;

    private SystemHandleSnapshot(IntPtr buffer, int entryCount)
    {
        _buffer = buffer;
        _entryCount = entryCount;
    }

    internal static SystemHandleSnapshot Capture()
    {
        var bufferSize = InitialBufferSize;

        while (true)
        {
            var buffer = Marshal.AllocHGlobal(bufferSize);
            var status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemExtendedHandleInformation,
                buffer,
                bufferSize,
                out var requiredSize);

            if (status >= 0)
            {
                try
                {
                    var count = ReadNativeUnsigned(buffer);
                    var maximumCount = (ulong)((bufferSize - HeaderSize) / EntrySize);
                    if (count > maximumCount || count > int.MaxValue)
                    {
                        throw new InvalidDataException(
                            $"System handle snapshot reported {count:N0} entries, " +
                            $"but the returned buffer can contain at most {maximumCount:N0}.");
                    }

                    return new SystemHandleSnapshot(buffer, (int)count);
                }
                catch
                {
                    Marshal.FreeHGlobal(buffer);
                    throw;
                }
            }

            Marshal.FreeHGlobal(buffer);

            if (!NativeMethods.IsResizeStatus(status))
            {
                throw NativeMethods.NtStatusException(status, "NtQuerySystemInformation");
            }

            var doubled = checked(bufferSize * 2);
            bufferSize = Math.Max(doubled, requiredSize > 0 ? requiredSize + 64 * 1024 : 0);
            if (bufferSize > MaximumBufferSize)
            {
                throw new InvalidOperationException(
                    $"System handle snapshot requires {bufferSize:N0} bytes, above the " +
                    $"{MaximumBufferSize:N0}-byte safety limit.");
            }
        }
    }

    internal IEnumerable<SystemHandleTableEntryInfoEx> EntriesFor(int processId, ushort objectTypeIndex)
    {
        ThrowIfDisposed();
        var processIdValue = unchecked((ulong)(uint)processId);

        for (var index = 0; index < _entryCount; index++)
        {
            var entry = ReadEntry(index);
            if (entry.ProcessId == processIdValue && entry.ObjectTypeIndex == objectTypeIndex)
            {
                yield return entry;
            }
        }
    }

    internal bool TryFind(int processId, ulong handleValue, out SystemHandleTableEntryInfoEx match)
    {
        ThrowIfDisposed();
        var processIdValue = unchecked((ulong)(uint)processId);

        for (var index = 0; index < _entryCount; index++)
        {
            var entry = ReadEntry(index);
            if (entry.ProcessId == processIdValue && entry.NumericHandle == handleValue)
            {
                match = entry;
                return true;
            }
        }

        match = default;
        return false;
    }

    internal bool ContainsSameHandle(
        int processId,
        ulong handleValue,
        ushort objectTypeIndex,
        IntPtr objectAddress)
    {
        return TryFind(processId, handleValue, out var current) &&
               current.ObjectTypeIndex == objectTypeIndex &&
               current.Object == objectAddress;
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    private SystemHandleTableEntryInfoEx ReadEntry(int index)
    {
        var offset = checked(HeaderSize + index * EntrySize);
        return Marshal.PtrToStructure<SystemHandleTableEntryInfoEx>(IntPtr.Add(_buffer, offset));
    }

    private static ulong ReadNativeUnsigned(IntPtr address) =>
        IntPtr.Size == 8
            ? unchecked((ulong)Marshal.ReadInt64(address))
            : unchecked((uint)Marshal.ReadInt32(address));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_buffer == IntPtr.Zero, this);
    }
}
