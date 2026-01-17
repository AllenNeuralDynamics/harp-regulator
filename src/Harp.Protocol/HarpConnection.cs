//#define PRINT_RECEIVED_MESSAGES
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.InteropServices;

namespace Harp.Protocol;

public sealed class HarpConnection : IDisposable
{
    private readonly SerialPort Port;

    private readonly byte[] ReceiveBuffer = new byte[1024];
    private int WriteHead = 0;
    private int ReadHead = 0;

    /// <summary>The standard baud rate for Harp devices (1 Mbps)</summary>
    public const int DefaultBaudRate = 1000000;

    public HarpConnection(string portName, int timeoutMilliseconds = SerialPort.InfiniteTimeout)
    {
        Port = new SerialPort(portName, DefaultBaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = timeoutMilliseconds,
            WriteTimeout = timeoutMilliseconds,
            Handshake = Handshake.RequestToSend,
            DtrEnable = true,
        };
        Port.Open();

#if DEBUG
        ReceiveBuffer.AsSpan().Fill(0xCC);
#endif
    }

    private HarpMessage DoTransaction(MessageType messageType, byte address, PayloadType payloadType, ReadOnlySpan<byte> rawPayload)
    {
        byte[] messageData = new byte[6 + rawPayload.Length];

        if (messageData.Length > byte.MaxValue)
            throw new NotImplementedException("This method doesn't implement extended-length message support.");
        if (payloadType.HasTimestamp)
            throw new NotImplementedException("This method doesn't implement timestamp support.");

        messageData[0] = (byte)messageType;
        messageData[1] = checked((byte)(messageData.Length - 2));
        messageData[2] = address;
        messageData[3] = 0xFF;
        messageData[4] = payloadType.RawValue;

        rawPayload.CopyTo(messageData.AsSpan().Slice(5, rawPayload.Length));

        byte checksum = 0;
        foreach (byte data in messageData)
            checksum += data;
        messageData[messageData.Length - 1] = checksum;

        Port.Write(messageData, 0, messageData.Length);

        // Wait for the response
#if PRINT_RECEIVED_MESSAGES
        Console.WriteLine($"====================== Awaiting response from {messageType} {payloadType} {(CommonRegister)address}...");
#endif
        long startTimestamp = Stopwatch.GetTimestamp();
        TryAgain:
        HarpMessageParser parser = new();
        HarpMessage? message = null;
        while (message is null)
        {
            // This is to handle the case where we keep trying again and again but never get a response
            if (Port.ReadTimeout != SerialPort.InfiniteTimeout && Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds > Port.ReadTimeout)
                throw new TimeoutException();

            WriteHead += Port.Read(ReceiveBuffer, WriteHead, ReceiveBuffer.Length - WriteHead);

            message = parser.Consume(ReceiveBuffer.AsSpan().Slice(ReadHead, WriteHead - ReadHead), out int bytesConsumed);
            ReadHead += bytesConsumed;
#if PRINT_RECEIVED_MESSAGES
            Console.WriteLine($"Parser consumed {bytesConsumed} bytes with {WriteHead - ReadHead} remaining in the buffer. ParserState = {parser.ParserState}");
#endif

            // Move data back to the start of the buffer if we've exhausted it
            if (WriteHead == ReceiveBuffer.Length)
            {
                ReadOnlySpan<byte> liveBuffer = ReceiveBuffer.AsSpan().Slice(ReadHead, WriteHead - ReadHead);
                Debug.Assert(liveBuffer.Length < ReceiveBuffer.Length); // Parser should always be consuming most of the data
                liveBuffer.CopyTo(ReceiveBuffer);
                WriteHead = liveBuffer.Length;
                ReadHead = 0;
#if DEBUG
                ReceiveBuffer.AsSpan().Slice(WriteHead).Fill(0xCC);
#endif
            }
        }

#if PRINT_RECEIVED_MESSAGES
        Console.WriteLine("Got response!");
        Console.WriteLine($"MessageType: {message.MessageType}");
        Console.WriteLine($"Address: {(CommonRegister)message.Address}");
        Console.WriteLine($"Port: {message.Port}");
        Console.WriteLine($"PayloadType: {message.PayloadType}");
        Console.WriteLine($"Timestamp: {message.Timestamp}");

        Console.Write("RawPayload: [ ");
        for (int i = 0; i < message.RawPayload.Length; i++)
        {
            if (i > 0)
                Console.Write(", ");
            Console.Write($"{message.RawPayload[i]:X2}");
        }
        Console.WriteLine(" ]");

        if (message is HarpMessage<ushort> { Payload.Length: 1 } ushortMessage)
            Console.WriteLine($"Payload: {ushortMessage.Payload[0]}");
        else if (message is HarpMessage<uint> { Payload.Length: 1 } uintMessage)
            Console.WriteLine($"Payload: {uintMessage.Payload[0]}");

        Console.WriteLine($"IsValid: {message.IsValid}");
        Console.WriteLine($"Checksum: {message.Checksum} (Received)");
        Console.WriteLine($"Checksum: {message.CalculatedChecksum} (Calculated)");

        Console.WriteLine();
#endif

        // If the message was an event, then it's not actually our response. Try again.
        if (message.MessageType == MessageType.Event)
        {
            Trace.WriteLine($"Got event in response to {messageType} {(CommonRegister)address} transaction, trying again...");
            goto TryAgain;
        }

        //TODO: Not all messages will get a reply from the same address, need a more robust way to handle this.
        if (message.Address != address)
        {
            Trace.WriteLine($"Got irrelevant {messageType} {(CommonRegister)message.Address} in response to {messageType} {(CommonRegister)address} transaction, trying again...");
            goto TryAgain;
        }

        // Reset read/write head if there isn't any extra data left in the buffer
        if (ReadHead == WriteHead)
        {
            ReadHead = 0;
            WriteHead = 0;
#if DEBUG
            ReceiveBuffer.AsSpan().Fill(0xCC);
#endif
        }

        return message;
    }

    public HarpMessage<T> Read<T>(byte register)
        where T : unmanaged
        => (HarpMessage<T>)DoTransaction(MessageType.Read, register, PayloadType.GetType<T>(), ReadOnlySpan<byte>.Empty);

    public HarpMessage Write<T>(byte register, T value)
        where T : unmanaged
        => Write<T>(register, MemoryMarshal.CreateSpan(ref value, 1));

    public HarpMessage Write<T>(byte register, ReadOnlySpan<T> value)
        where T : unmanaged
        => (HarpMessage<T>)DoTransaction(MessageType.Write, register, PayloadType.GetType<T>(), MemoryMarshal.Cast<T, byte>(value));

    public HarpMessage<T> Read<T>(CommonRegister register)
        where T : unmanaged
        => Read<T>((byte)register);

    public HarpMessage Write<T>(CommonRegister register, T value)
        where T : unmanaged
        => Write<T>((byte)register, value);

    public HarpMessage Write<T>(CommonRegister register, ReadOnlySpan<T> value)
        where T : unmanaged
        => Write<T>((byte)register, value);

    public void Dispose()
        => Port.Dispose();
}
