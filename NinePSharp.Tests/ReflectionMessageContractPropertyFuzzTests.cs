using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Parser;
using Xunit;

namespace NinePSharp.Tests;

public class ReflectionMessageContractPropertyFuzzTests
{
    private static readonly HashSet<string> SupportedClassicMessageTypeNames = new(StringComparer.Ordinal)
    {
        nameof(Tversion), nameof(Rversion),
        nameof(Tauth), nameof(Rauth),
        nameof(Tattach), nameof(Rattach),
        nameof(Rerror),
        nameof(Tflush), nameof(Rflush),
        nameof(Twalk), nameof(Rwalk),
        nameof(Topen), nameof(Ropen),
        nameof(Tcreate), nameof(Rcreate),
        nameof(Tread), nameof(Rread),
        nameof(Twrite), nameof(Rwrite),
        nameof(Tclunk), nameof(Rclunk),
        nameof(Tremove), nameof(Rremove),
        nameof(Tstat), nameof(Rstat),
        nameof(Twstat), nameof(Rwstat)
    };

    private static readonly IReadOnlyList<Type> SerializableMessageTypes = DiscoverSerializableMessageTypes();

    [Fact]
    public void Reflection_Message_Type_Discovery_Is_NonEmpty_And_Stable()
    {
        SerializableMessageTypes.Should().NotBeEmpty();
        SerializableMessageTypes.Should().OnlyContain(t => t.Namespace == "NinePSharp.Messages");
        SerializableMessageTypes.Should().OnlyContain(t => typeof(ISerializable).IsAssignableFrom(t));
        SerializableMessageTypes.Should().OnlyContain(t => t.Name != nameof(Stat));
    }

    [Fact]
    public void Reflection_All_Serializable_Messages_Serialize_With_Consistent_Header_And_Parse()
    {
        var random = new Random(20260227);
        var failures = new List<string>();

        foreach (var type in SerializableMessageTypes)
        {
            try
            {
                var message = CreateSampleMessage(type, random);
                var frame = Serialize(message);

                BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(message.Size);
                frame[4].Should().Be((byte)message.Type);
                BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(5, 2)).Should().Be(message.Tag);

                var parsedClassic = NinePParser.parse(NinePDialect.NineP2000, frame.AsMemory());

                parsedClassic.IsOk
                    .Should()
                    .BeTrue($"{type.Name} should parse in the supported classic dialect.");
            }
            catch (Exception ex)
            {
                failures.Add($"{type.Name}: {ex.GetType().Name} - {ex.Message}");
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Property(MaxTest = 35)]
    public bool Reflection_PerMessage_Fuzzed_Frames_Are_Parser_Bounded(PositiveInt seed)
    {
        var random = new Random(seed.Get ^ 0x5f3759df);
        var types = SerializableMessageTypes
            .OrderBy(_ => random.Next())
            .Take(Math.Min(12, SerializableMessageTypes.Count))
            .ToList();

        foreach (var type in types)
        {
            ISerializable message;
            byte[] baseline;

            try
            {
                message = CreateSampleMessage(type, random);
                baseline = Serialize(message);
            }
            catch
            {
                return false;
            }

            for (int round = 0; round < 7; round++)
            {
                var mutated = (byte[])baseline.Clone();
                int flips = Math.Min(10, mutated.Length);

                for (int i = 0; i < flips; i++)
                {
                    int index = random.Next(mutated.Length);
                    mutated[index] = (byte)random.Next(0, 256);
                }

                // Keep framing valid enough to force the specific parser branch.
                BinaryPrimitives.WriteUInt32LittleEndian(mutated.AsSpan(0, 4), (uint)mutated.Length);
                mutated[4] = (byte)message.Type;

                try
                {
                    _ = NinePParser.parse(NinePDialect.NineP2000, mutated.AsMemory());
                }
                catch
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IReadOnlyList<Type> DiscoverSerializableMessageTypes()
    {
        return typeof(Tversion).Assembly.GetTypes()
            .Where(t =>
                t.IsValueType &&
                !t.IsEnum &&
                !t.IsAbstract &&
                t.Namespace == "NinePSharp.Messages" &&
                SupportedClassicMessageTypeNames.Contains(t.Name) &&
                typeof(ISerializable).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static ISerializable CreateSampleMessage(Type type, Random random)
    {
        if (type == typeof(Tcreate))
        {
            return BuildTcreate(tag: 9, fid: 1, name: "reflect-create", perm: 0644, mode: 0);
        }

        var constructor = SelectPreferredNonParserConstructor(type)
            ?? throw new InvalidOperationException($"No non-parser constructor found for {type.Name}.");

        var args = BuildArguments(constructor.GetParameters(), random);
        var instance = constructor.Invoke(args);

        if (instance is not ISerializable serializable)
        {
            throw new InvalidOperationException($"Constructed {type.Name} is not ISerializable.");
        }

        return serializable;
    }

    private static ConstructorInfo? SelectPreferredNonParserConstructor(Type type)
    {
        return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => !IsParserConstructor(c))
            .OrderBy(c => c.GetParameters().Any(p => IsSizeParameter(p)) ? 1 : 0)
            .ThenBy(c => c.GetParameters().Length)
            .FirstOrDefault();
    }

    private static bool IsParserConstructor(ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters();
        if (parameters.Length == 0)
        {
            return false;
        }

        var first = parameters[0].ParameterType;
        bool firstIsBuffer = first == typeof(ReadOnlySpan<byte>) || first == typeof(ReadOnlyMemory<byte>);
        if (!firstIsBuffer)
        {
            return false;
        }

        // Any ctor starting from a raw buffer is the parser path, regardless of extra dialect/ref parameters.
        return true;
    }

    private static bool IsSizeParameter(ParameterInfo parameter)
    {
        return parameter.ParameterType == typeof(uint) &&
               string.Equals(parameter.Name, "size", StringComparison.OrdinalIgnoreCase);
    }

    private static object?[] BuildArguments(ParameterInfo[] parameters, Random random)
    {
        uint payloadCount = (uint)random.Next(4, 24);
        byte[] payload = new byte[payloadCount];
        random.NextBytes(payload);

        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            string name = parameter.Name ?? string.Empty;
            args[i] = BuildArgumentValue(parameter.ParameterType, name, random, payload);
        }

        return args;
    }

    private static object BuildArgumentValue(Type type, string parameterName, Random random, byte[] payload)
    {
        if (type == typeof(ushort))
        {
            return (ushort)random.Next(1, ushort.MaxValue);
        }

        if (type == typeof(uint))
        {
            if (string.Equals(parameterName, "size", StringComparison.OrdinalIgnoreCase))
            {
                return 4096u;
            }

            if (parameterName.Contains("count", StringComparison.OrdinalIgnoreCase))
            {
                return (uint)payload.Length;
            }

            if (parameterName.Contains("mode", StringComparison.OrdinalIgnoreCase))
            {
                return 0644u;
            }

            return (uint)random.Next(1, 4096);
        }

        if (type == typeof(ulong))
        {
            return (ulong)random.Next(1, 8192);
        }

        if (type == typeof(byte))
        {
            if (parameterName.Contains("mode", StringComparison.OrdinalIgnoreCase))
            {
                return (byte)0;
            }

            return (byte)random.Next(0, 255);
        }

        if (type == typeof(bool))
        {
            return random.Next(0, 2) == 0;
        }

        if (type == typeof(string))
        {
            if (parameterName.Contains("version", StringComparison.OrdinalIgnoreCase))
            {
                return "9P2000";
            }

            return $"s{random.Next(1000, 9999)}";
        }

        if (type == typeof(string[]))
        {
            return new[] { "a", "b" };
        }

        if (type == typeof(Qid))
        {
            return new Qid(QidType.QTFILE, 0, (ulong)random.Next(1, 100_000));
        }

        if (type == typeof(Qid[]))
        {
            return new[] { new Qid(QidType.QTFILE, 0, (ulong)random.Next(1, 100_000)) };
        }

        if (type == typeof(ReadOnlyMemory<byte>))
        {
            return new ReadOnlyMemory<byte>(payload);
        }

        if (type == typeof(byte[]))
        {
            return payload.ToArray();
        }

        if (type == typeof(Stat))
        {
            return new Stat(
                size: 0,
                type: 0,
                dev: 0,
                qid: new Qid(QidType.QTFILE, 0, (ulong)random.Next(1, 100_000)),
                mode: 0644,
                atime: 0,
                mtime: 0,
                length: (ulong)random.Next(0, 256),
                name: "n",
                uid: "u",
                gid: "g",
                muid: "m",
                dialect: NinePDialect.NineP2000);
        }

        if (type == typeof(uint?))
        {
            return (uint?)random.Next(1, 10_000);
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(0)
                ?? throw new InvalidOperationException($"Enum {type.Name} has no values.");
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Failed to create value type {type.Name}.");
        }

        throw new NotSupportedException($"Unsupported constructor parameter type: {type.FullName}");
    }

    private static byte[] Serialize(ISerializable message)
    {
        if (message.Size < NinePConstants.HeaderSize)
        {
            throw new InvalidOperationException($"{message.GetType().Name} has invalid Size={message.Size}.");
        }

        var buffer = new byte[message.Size];
        message.WriteTo(buffer);
        return buffer;
    }

    private static Tcreate BuildTcreate(ushort tag, uint fid, string name, uint perm, byte mode)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + nameBytes.Length + 4 + 1);
        var data = new byte[size];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), size);
        data[4] = (byte)MessageTypes.Tcreate;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5, 2), tag);

        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), fid);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), (ushort)nameBytes.Length);
        offset += 2;
        nameBytes.CopyTo(data.AsSpan(offset, nameBytes.Length));
        offset += nameBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), perm);
        offset += 4;
        data[offset] = mode;

        return new Tcreate(data);
    }
}
