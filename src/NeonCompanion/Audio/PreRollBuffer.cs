namespace NeonCompanion.Audio;

/// <summary>
/// A ring buffer holding the most recent <c>N</c> milliseconds of audio, so that when a wake word
/// is recognised the audio <em>before</em> the recognition (the word itself, and whatever was
/// said right after it) can be seeded into the utterance.
///
/// <para>Writes overwrite the oldest bytes once the buffer is full; <see cref="Flush"/> returns
/// everything held, oldest first, and empties it. Thread-safe. <c>WakeListener</c> owns one and
/// trims it with <see cref="DiscardOldest"/> before the flush; push-to-talk starts the utterance
/// at the key press and needs no pre-roll.</para>
/// </summary>
public sealed class PreRollBuffer
{
    private readonly byte[] _buffer;
    private readonly object _gate = new();
    private long _writePosition;
    private int _byteCount;

    /// <param name="captureMilliseconds">How much audio to keep.</param>
    /// <param name="format">The stream's format, which fixes the byte capacity.</param>
    public PreRollBuffer(int captureMilliseconds, PcmFormat format)
    {
        int capacity = format.BytesFor(captureMilliseconds);
        if (captureMilliseconds <= 0 || capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(captureMilliseconds), captureMilliseconds, "The pre-roll must hold a positive number of bytes.");
        }

        CaptureMilliseconds = captureMilliseconds;
        Format = format;
        _buffer = new byte[capacity];
    }

    public int CaptureMilliseconds { get; }

    public PcmFormat Format { get; }

    /// <summary>The most bytes the buffer holds.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>How many bytes are held now.</summary>
    public int ByteCount
    {
        get
        {
            lock (_gate)
            {
                return _byteCount;
            }
        }
    }

    /// <summary>Appends <paramref name="count"/> bytes of <paramref name="data"/> from <paramref name="offset"/>, dropping the oldest when full. Returns the bytes written.</summary>
    public int Write(byte[] data, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (offset < 0 || count < 0 || offset + count > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The offset and count must describe a slice of the array.");
        }

        lock (_gate)
        {
            int len = _buffer.Length;
            for (int i = 0; i < count; i++)
            {
                _buffer[(int)(_writePosition % len)] = data[offset + i];
                _writePosition++;
                if (_byteCount < len)
                {
                    _byteCount++;
                }
            }

            return count;
        }
    }

    /// <summary>Everything held, oldest first; the buffer is empty afterwards.</summary>
    public byte[] Flush()
    {
        lock (_gate)
        {
            if (_byteCount == 0)
            {
                return Array.Empty<byte>();
            }

            var result = new byte[_byteCount];
            int len = _buffer.Length;
            int readPos = (int)(((_writePosition - _byteCount) % len + len) % len);
            int remaining = _byteCount;
            int dst = 0;
            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, len - readPos);
                Array.Copy(_buffer, readPos, result, dst, chunk);
                readPos = (readPos + chunk) % len;
                dst += chunk;
                remaining -= chunk;
            }

            _byteCount = 0;
            _writePosition = 0;
            return result;
        }
    }

    /// <summary>Drops everything.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _byteCount = 0;
            _writePosition = 0;
        }
    }

    /// <summary>Drops the oldest <paramref name="count"/> bytes; a count at or over <see cref="ByteCount"/> empties the buffer, a negative one does nothing.</summary>
    public void DiscardOldest(int count)
    {
        if (count <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (count >= _byteCount)
            {
                _byteCount = 0;
                _writePosition = 0;
            }
            else
            {
                _byteCount -= count;
            }
        }
    }
}
