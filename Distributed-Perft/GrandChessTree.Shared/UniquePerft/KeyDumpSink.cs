using System.Buffers.Binary;

namespace GrandChessTree.Shared;

public sealed class KeyDumpSink : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _lock = new();
    private long _written;

    public long BytesWritten => Volatile.Read(ref _written);

    public KeyDumpSink(string path)
    {
        _stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 20,
            useAsync: false);
    }

    public void Record(ulong value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        lock (_lock)
        {
            _stream.Write(buf);
            _written += 8;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stream.Flush(flushToDisk: true);
            _stream.Dispose();
        }
    }
}
