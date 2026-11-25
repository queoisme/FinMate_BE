namespace FinMate.Application.DataExport.Package;

/// <summary>Gói vượt <c>dataExport.maxArchiveMb</c> (BR-EXP-09). Ném ngay khi ghi quá, không chờ dựng xong.</summary>
public sealed class ArchiveTooLargeException(long limitBytes) : Exception($"Gói vượt giới hạn {limitBytes} byte.")
{
    public long LimitBytes { get; } = limitBytes;
}

/// <summary>Luồng ghi chỉ đếm byte và dừng khi vượt giới hạn. Không seek được, để <see cref="System.IO.Compression.ZipArchive"/> ghi tuần tự.</summary>
internal sealed class LimitedWriteStream(Stream inner, long limitBytes) : Stream
{
    public long Written { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => Written;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Count(count);
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Count(buffer.Length);
        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Count(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Count(int bytes)
    {
        Written += bytes;
        if (Written > limitBytes)
        {
            throw new ArchiveTooLargeException(limitBytes);
        }
    }
}
