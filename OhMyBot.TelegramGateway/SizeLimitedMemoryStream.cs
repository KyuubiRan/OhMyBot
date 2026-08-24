namespace OhMyBot.TelegramGateway;

internal sealed class SizeLimitedMemoryStream(long maxLength) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(buffer.Length);
        base.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureCapacity(count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureCapacity(buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        EnsureCapacity(1);
        base.WriteByte(value);
    }

    private void EnsureCapacity(int additionalBytes)
    {
        if (additionalBytes < 0 || Position > maxLength - additionalBytes)
        {
            throw new InvalidDataException($"Command media cannot exceed {maxLength} bytes.");
        }
    }
}
