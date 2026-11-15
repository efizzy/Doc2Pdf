using System;
using System.IO;
using System.Text;

namespace Efizzy.Doc2Pdf.Security;

/// <summary>
/// Lightweight ASN.1 DER (Distinguished Encoding Rules) writer for cryptographic structures.
/// </summary>
internal sealed class DerWriter
{
    private readonly MemoryStream _stream = new();

    public byte[] ToArray() => _stream.ToArray();

    public void WriteByte(byte b) => _stream.WriteByte(b);

    public void WriteBytes(byte[] bytes) => _stream.Write(bytes, 0, bytes.Length);

    public void WriteLength(int length)
    {
        if (length < 128)
        {
            _stream.WriteByte((byte)length);
        }
        else if (length <= 0xFF)
        {
            _stream.WriteByte(0x81);
            _stream.WriteByte((byte)length);
        }
        else if (length <= 0xFFFF)
        {
            _stream.WriteByte(0x82);
            _stream.WriteByte((byte)(length >> 8));
            _stream.WriteByte((byte)(length & 0xFF));
        }
        else if (length <= 0xFFFFFF)
        {
            _stream.WriteByte(0x83);
            _stream.WriteByte((byte)(length >> 16));
            _stream.WriteByte((byte)((length >> 8) & 0xFF));
            _stream.WriteByte((byte)(length & 0xFF));
        }
        else
        {
            _stream.WriteByte(0x84);
            _stream.WriteByte((byte)((length >> 24) & 0xFF));
            _stream.WriteByte((byte)((length >> 16) & 0xFF));
            _stream.WriteByte((byte)((length >> 8) & 0xFF));
            _stream.WriteByte((byte)(length & 0xFF));
        }
    }

    public void WriteTagged(byte tag, byte[] content)
    {
        _stream.WriteByte(tag);
        WriteLength(content.Length);
        _stream.Write(content, 0, content.Length);
    }

    public void WriteInteger(long value)
    {
        if (value == 0)
        {
            WriteTagged(0x02, new byte[] { 0x00 });
            return;
        }

        byte[] raw = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(raw);

        int start = 0;
        while (start < raw.Length - 1 && raw[start] == 0)
            start++;

        int len = raw.Length - start;
        bool needZero = (raw[start] & 0x80) != 0;
        byte[] payload = new byte[needZero ? len + 1 : len];
        if (needZero)
        {
            payload[0] = 0x00;
            Buffer.BlockCopy(raw, start, payload, 1, len);
        }
        else
        {
            Buffer.BlockCopy(raw, start, payload, 0, len);
        }

        WriteTagged(0x02, payload);
    }

    public void WriteBigInteger(byte[] bigEndianBytes)
    {
        int start = 0;
        while (start < bigEndianBytes.Length - 1 && bigEndianBytes[start] == 0)
            start++;

        int len = bigEndianBytes.Length - start;
        bool needZero = (bigEndianBytes[start] & 0x80) != 0;
        byte[] payload = new byte[needZero ? len + 1 : len];
        if (needZero)
        {
            payload[0] = 0x00;
            Buffer.BlockCopy(bigEndianBytes, start, payload, 1, len);
        }
        else
        {
            Buffer.BlockCopy(bigEndianBytes, start, payload, 0, len);
        }

        WriteTagged(0x02, payload);
    }

    public void WriteOctetString(byte[] content)
    {
        WriteTagged(0x04, content);
    }

    public void WriteNull()
    {
        _stream.WriteByte(0x05);
        _stream.WriteByte(0x00);
    }

    public void WriteOid(string oid)
    {
        string[] parts = oid.Split('.');
        if (parts.Length < 2)
            throw new ArgumentException("Invalid OID format.", nameof(oid));

        int first = int.Parse(parts[0]);
        int second = int.Parse(parts[1]);
        using var ms = new MemoryStream();
        ms.WriteByte((byte)(first * 40 + second));

        for (int i = 2; i < parts.Length; i++)
        {
            long val = long.Parse(parts[i]);
            WriteVarInt(ms, val);
        }

        WriteTagged(0x06, ms.ToArray());
    }

    private static void WriteVarInt(MemoryStream ms, long value)
    {
        if (value == 0)
        {
            ms.WriteByte(0);
            return;
        }

        byte[] buf = new byte[10];
        int idx = 0;
        buf[idx++] = (byte)(value & 0x7F);
        value >>= 7;

        while (value > 0)
        {
            buf[idx++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        for (int i = idx - 1; i >= 0; i--)
        {
            ms.WriteByte(buf[i]);
        }
    }

    public void WriteUtcTime(DateTimeOffset time)
    {
        // YYMMDDhhmmssZ
        string s = time.ToUniversalTime().ToString("yyMMddHHmmss'Z'");
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        WriteTagged(0x17, bytes);
    }

    public void WriteSequence(byte[] content) => WriteTagged(0x30, content);

    public void WriteSet(byte[] content) => WriteTagged(0x31, content);

    public void WriteContextSpecific(int tagNumber, byte[] content, bool constructed = true)
    {
        byte tag = (byte)((constructed ? 0xA0 : 0x80) | (tagNumber & 0x1F));
        WriteTagged(tag, content);
    }
}
