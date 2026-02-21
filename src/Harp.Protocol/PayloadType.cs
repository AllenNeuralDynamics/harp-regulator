using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Harp.Protocol;

public readonly struct PayloadType
{
    public readonly byte RawValue;

    public bool HasTimestamp => (RawValue & (1 << 4)) != 0;

    public bool IsSigned => (RawValue & (1 << 7)) != 0;
    public bool IsFloat => (RawValue & (1 << 6)) != 0;
    public int NumBits => (RawValue & 0b1111) * 8;

    public PayloadType(bool hasTimestamp, bool isSigned, bool isFloat, int numBits)
    {
        RawValue = 0;
        if (hasTimestamp)
            RawValue |= 1 << 4;
        if (isSigned)
            RawValue |= 1 << 7;
        if (isFloat)
            RawValue |= 1 << 6;

        switch (numBits)
        {
            case 8:
            case 16:
            case 32:
            case 64:
                RawValue |= (byte)((numBits / 8) & 0b1111);
                break;
            default:
                throw new ArgumentException("Invalid number of bits specified.", nameof(numBits));
        }
    }

    public Type Type => TryGetType(out Type? result, throwIfInvalid: true) ? result : throw new UnreachableException();
    public bool IsValid => TryGetType(out _, throwIfInvalid: false);

    public bool TryGetType([NotNullWhen(true)] out Type? type)
        => TryGetType(out type, throwIfInvalid: false);

    private bool TryGetType([NotNullWhen(true)] out Type? type, bool throwIfInvalid)
    {
        if (IsSigned && IsFloat)
        {
            if (throwIfInvalid)
                throw new NotSupportedException($"{nameof(IsFloat)} and {nameof(IsSigned)} must not both be set.");

            type = null;
            return false;
        }

        type = (IsSigned, IsFloat, NumBits) switch
        {
            (false, true, 8) => throwIfInvalid ? throw new NotSupportedException("8-bit floats are not supported") : null,
            (false, true, 16) => typeof(Half),
            (false, true, 32) => typeof(float),
            (false, true, 64) => typeof(double),
            (true, false, 8) => typeof(sbyte),
            (true, false, 16) => typeof(short),
            (true, false, 32) => typeof(int),
            (true, false, 64) => typeof(long),
            (false, false, 8) => typeof(byte),
            (false, false, 16) => typeof(ushort),
            (false, false, 32) => typeof(uint),
            (false, false, 64) => typeof(ulong),
            (_, _, 0) => throwIfInvalid ? throw new NotSupportedException("0-bit type is not supported") : null,
            (_, _, _) => throwIfInvalid
                ? throw new NotSupportedException($"Unsupported payload type 0x{RawValue:X2} ({NumBits}-bit, signed={IsSigned}, float={IsFloat}).")
                : null,
        };
        return type is not null;
    }

    public override string ToString()
        => TryGetType(out Type? type) ? type.Name : $"<invalid 0x{RawValue:X}>";

    public static PayloadType GetType<T>(bool hasTimestamp = false)
        where T : unmanaged
    {
        if (typeof(T) == typeof(Half))
            return new PayloadType(hasTimestamp, false, true, 16);
        else if (typeof(T) == typeof(float))
            return new PayloadType(hasTimestamp, false, true, 32);
        else if (typeof(T) == typeof(double))
            return new PayloadType(hasTimestamp, false, true, 64);
        else if (typeof(T) == typeof(sbyte))
            return new PayloadType(hasTimestamp, true, false, 8);
        else if (typeof(T) == typeof(short))
            return new PayloadType(hasTimestamp, true, false, 16);
        else if (typeof(T) == typeof(int))
            return new PayloadType(hasTimestamp, true, false, 32);
        else if (typeof(T) == typeof(long))
            return new PayloadType(hasTimestamp, true, false, 64);
        else if (typeof(T) == typeof(byte))
            return new PayloadType(hasTimestamp, false, false, 8);
        else if (typeof(T) == typeof(ushort))
            return new PayloadType(hasTimestamp, false, false, 16);
        else if (typeof(T) == typeof(uint))
            return new PayloadType(hasTimestamp, false, false, 32);
        else if (typeof(T) == typeof(ulong))
            return new PayloadType(hasTimestamp, false, false, 64);
        else
            throw new NotSupportedException();
    }
}
