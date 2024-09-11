using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace TurtleTracker;

public sealed class ModuleFile
{
    private ModuleFile()
    {
    }

    public static ModuleFile Parse(byte[] modFile)
    {
        static string FromNullPadded(ReadOnlySpan<byte> ascii)
        {
            return Encoding.ASCII.GetString(ascii.TrimEnd((byte)0));
        }

        var reader = new SequenceReader<byte>(new(modFile));

        string name = FromNullPadded(reader.Read(20));

        // 15 or 31 samples?
        ReadOnlySpan<byte> tag = modFile.AsSpan(1080, 4);

        if (!tag.SequenceEqual("M.K."u8))
        {
            throw new InvalidDataException("Unsupported module format.");
        }

        var samples = ImmutableArray.CreateBuilder<ModuleSample>();

        for (var i = 0; i < 31; i++)
        {
            string sampleName = FromNullPadded(reader.Read(22));
            var sampleLength = reader.ReadInt16BigEndian();
            var finetune = ToSigned4Bit(reader.ReadByte());
            var volume = reader.ReadByte();
            var loopOffsetStart = reader.ReadInt16BigEndian();
            var loopOffsetLength = reader.ReadInt16BigEndian();

            static sbyte ToSigned4Bit(byte raw8Bits)
            {
                if ((raw8Bits & 0b1000) == 0)
                {
                    return (sbyte)raw8Bits;
                }
                else 
                {
                    return (sbyte)(raw8Bits | 0xF0);
                }
            }

            samples.Add(new(
                sampleName,
                sampleLength,
                finetune,
                volume,
                loopOffsetStart,
                loopOffsetLength
            ));
        }

        var sequenceLength = reader.ReadByte();
        reader.Advance(1);
        
        var sequence = reader.Read(128);

        int maxPattern = -1;
        foreach (var p in sequence)
        {
            if (maxPattern < p)
            {
                maxPattern = p;
            }
        }

        // We already verified the tag
        reader.Advance(4);

        var patterns = ImmutableArray.CreateBuilder<ModulePattern>();

        for (int i = 0; i <= maxPattern; i++)
        {
            var divisions = ImmutableArray.CreateBuilder<ModuleDivision>(64);

            for (var j = 0; j < 64; j++)
            {
                static ModuleNote ReadNote(ref SequenceReader<byte> reader)
                {
                    var b1 = reader.ReadByte();
                    var b2 = reader.ReadByte();
                    var b3 = reader.ReadByte();
                    var b4 = reader.ReadByte();

                    var sampleNumber = (byte)((b1 & 0xF0) | (b3 >> 4));
                    var samplePeriod = (short)(b2 | ((b1 & 0xF) << 8));
                    var effectCommand = (short)(b4 | ((b3 & 0xF) << 8));

                    return new(sampleNumber, samplePeriod, effectCommand);
                }

                divisions.Add(new ModuleDivision(
                    ReadNote(ref reader),
                    ReadNote(ref reader),
                    ReadNote(ref reader),
                    ReadNote(ref reader)
                ));
            }

            patterns.Add(new ModulePattern(divisions.MoveToImmutable()));
        }

        var sampleDatas = ImmutableArray.CreateBuilder<sbyte[]>();

        foreach (var sample in samples) 
        {
            sampleDatas.Add(MemoryMarshal.Cast<byte, sbyte>(reader.Read(sample.Length * 2)).ToArray());
        }

        return new ModuleFile()
        {
            Name = name,
            Samples = samples.DrainToImmutable(),
            SampleData = sampleDatas.DrainToImmutable(),
            Patterns = patterns.DrainToImmutable(),
            Sequence = sequence[..sequenceLength].ToImmutableArray()
        };
    }

    public required string Name { get; init; }

    public ImmutableArray<ModuleSample> Samples { get; init; }

    public ImmutableArray<sbyte[]> SampleData { get; init; }

    public ImmutableArray<ModulePattern> Patterns { get; init; }

    public ImmutableArray<byte> Sequence { get; init; }
}

public static class SequenceReaderExtensions
{
    public static ReadOnlySpan<T> Read<T>(ref this SequenceReader<T> reader, long count)
        where T : unmanaged, IEquatable<T>
    {
        var pos = reader.Position;
        reader.Advance(count);

        var seq = reader.Sequence.Slice(pos, reader.Position);
        return seq.IsSingleSegment ? seq.FirstSpan : seq.ToArray();
    }

    public static byte ReadByte(ref this SequenceReader<byte> reader)
    {
        if (!reader.TryRead(out var res))
        {
            ThrowInvalidDataException();
            return default;
        }

        return res;
    }

    public static short ReadInt16BigEndian(ref this SequenceReader<byte> reader)
    {
        if (!reader.TryReadBigEndian(out short res))
        {
            ThrowInvalidDataException();
            return default;
        }

        return res;
    }


    static void ThrowInvalidDataException()
    {
        throw new InvalidDataException("Invalid module file.");
    }

}

public sealed record ModuleSample(
    string Name,
    short Length,
    sbyte Finetune,
    byte Volume,
    short LoopOffsetStart,
    short LoopOffsetLength);

public readonly record struct ModuleNote(byte Sample, short SamplePeriod, short EffectCommand)
{
    public ModuleEffect Effect => (ModuleEffect)((EffectCommand >> 8) & 0xF);
    public ModuleExtendedEffect ExtendedEffect => (ModuleExtendedEffect)EffectParameter1;
    public byte EffectParameter => (byte)(EffectCommand & 0xFF);
    public byte EffectParameter1 => (byte)((EffectCommand >> 4) & 0xF);
    public byte EffectParameter2 => (byte)(EffectCommand & 0xF);
}

public enum ModuleEffect
{
    Arpeggio = 0x0,
    PortamentoUp = 0x1,
    PortamentoDown = 0x2,
    TonePortamento = 0x3,
    Vibrato = 0x4,
    VolumeSlideAndTonePortamento = 0x5,
    VolumeSlideAndVibrato = 0x6,
    Tremolo = 0x7,
    SetPanning = 0x8,
    SetOffset = 0x9,
    VolumeSlide = 0xA,
    PositionJump = 0xB,
    SetVolume = 0xC,
    PatternBreak = 0xD,
    Extended = 0xE,
    SetTempo = 0xF,
}

public enum ModuleExtendedEffect
{
    FinePortamentoUp = 0x1,
    FinePortamentoDown = 0x2,
    GlissandoControl = 0x3,
    SetVibratoWaveform = 0x4,
    SetFinetune = 0x5,
    PatternLoop = 0x6,
    SetTremoloWaveform = 0x7,
    SetPanning = 0x8,
    Retrigger = 0x9,
    FineVolumeSlideUp = 0xA,
    FineVolumeSlideDown = 0xB,
    NoteCut = 0xC,
    NoteDelay = 0xD,
    PatternDelay = 0xE,
    InvertLoop = 0xF
}

public sealed record ModuleDivision(
    ModuleNote Channel1,
    ModuleNote Channel2,
    ModuleNote Channel3,
    ModuleNote Channel4);

public sealed record ModulePattern(
    ImmutableArray<ModuleDivision> Divisions
);
