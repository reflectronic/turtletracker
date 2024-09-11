namespace TurtleTracker;

using System.Numerics;
using Silk.NET.SDL;

public sealed class ModulePlayer
{
    public unsafe ModulePlayer(ModuleFile module)
    {
        audioSpec = new AudioSpec
        {
            Channels = 1,
            Freq = 44100,
            Format = Sdl.AudioF32
        };

        this.module = module;

        Sdl.Init(Sdl.InitAudio);
        Sdl.ThrowError();
    }

    private readonly Sdl Sdl = SdlProvider.SDL.Value;
    private readonly AudioSpec audioSpec;
    private readonly ModuleFile module;

    private uint audioDevice;

    private int speed = 6;
    private int patternPosition;
    private int divisionPosition;

    private ModulePattern Pattern => module.Patterns[module.Sequence[patternPosition]];
    private ModuleDivision Division => Pattern.Divisions[divisionPosition];

    private record struct ChannelState(
        float[] AudioBuffer, 
        ModuleSample Sample = default!,
        sbyte[] SampleData = default!,
        short Period = 0)
    {
        public float[] AudioBuffer = AudioBuffer;

        public ModuleSample Sample = Sample;
        public sbyte[] SampleData = SampleData;

        public short Period = Period;
        public double Position;
        public byte Volume;
        public sbyte Finetune;
        public bool Looping;

        public short NotePeriod;
        public byte SlideAmount;

        public byte VibratoTicks;
        public OscillatorWaveform VibratoWaveform;
        public bool VibratoRetrigger = true;
        public sbyte VibratoAmount;
        public byte VibratoSpeed;
        public byte VibratoDepth;

        public byte TremoloTicks;
        public OscillatorWaveform TremoloWaveform;
        public bool TremoloRetrigger = true;
        public sbyte TremoloAmount;
        public byte TremoloSpeed;
        public byte TremoloDepth;
    };

    private enum OscillatorWaveform
    {
        Sine = 0,
        Sawtooth = 1,
        Square = 2,
        Random = 3
    }

    private ChannelState channel1 = new(new float[882]);
    private ChannelState channel2 = new(new float[882]);
    private ChannelState channel3 = new(new float[882]);
    private ChannelState channel4 = new(new float[882]);

    public void Play()
    {
        var outSpec = default(AudioSpec);

        audioDevice = Sdl.OpenAudioDevice((string?)null, 0, in audioSpec, ref outSpec, 0);
        Sdl.ThrowError();

        Sdl.PauseAudioDevice(audioDevice, 0);
        Sdl.ThrowError();

        var outputBuffer = new float[882];

        for (patternPosition = 0; patternPosition < module.Sequence.Length; patternPosition++)
        {
            for (divisionPosition = 0; divisionPosition < Pattern.Divisions.Length;)
            {
                var division = Division;

                UpdateChannel(ref channel1, division.Channel1);
                UpdateChannel(ref channel2, division.Channel2);
                UpdateChannel(ref channel3, division.Channel3);
                UpdateChannel(ref channel4, division.Channel4);

                for (int i = 0; i < speed; i++)
                {
                    var firstTick = i == 0;
                    RenderChannel(ref channel1, division.Channel1, firstTick);
                    RenderChannel(ref channel2, division.Channel2, firstTick);
                    RenderChannel(ref channel3, division.Channel3, firstTick);
                    RenderChannel(ref channel4, division.Channel4, firstTick);

                    for (int j = 0; j < outputBuffer.Length; j++)
                    {
                        outputBuffer[j] = 0.25f *
                            (channel1.AudioBuffer[j] +
                            channel2.AudioBuffer[j] +
                            channel3.AudioBuffer[j] +
                            channel4.AudioBuffer[j]);
                    }

                    SpinWait.SpinUntil(() => Sdl.GetQueuedAudioSize(audioDevice) < 882 * 50);
                    Sdl.QueueAudio<float>(audioDevice, outputBuffer, (uint)outputBuffer.Length * 4);
                }

                int newPatternPosition;
                var positionJump = IsPositionJump(division.Channel1, out newPatternPosition) ||
                                    IsPositionJump(division.Channel2, out newPatternPosition) ||
                                    IsPositionJump(division.Channel3, out newPatternPosition) ||
                                    IsPositionJump(division.Channel4, out newPatternPosition);

                int newDivisionPosition;
                var patternBreak = IsPatternBreak(division.Channel1, out newDivisionPosition) ||
                                    IsPatternBreak(division.Channel2, out newDivisionPosition) ||
                                    IsPatternBreak(division.Channel3, out newDivisionPosition) ||
                                    IsPatternBreak(division.Channel4, out newDivisionPosition);

                if (!patternBreak && !positionJump)
                {
                    divisionPosition++;
                }
                else if (!patternBreak && positionJump)
                {
                    divisionPosition = 0;
                    patternPosition = newPatternPosition;
                }
                else if (patternBreak && !positionJump)
                {
                    divisionPosition = newDivisionPosition;
                    patternPosition = (patternPosition + 1) % module.Sequence.Length;
                }
                else if (patternBreak && positionJump)
                {
                    divisionPosition = newDivisionPosition;
                    patternPosition = newPatternPosition;
                }
            }
        }
    }

    private void UpdateChannel(ref ChannelState channel, ModuleNote note)
    {
        var (sampleNumber, samplePeriod, _) = note;

        switch (note.Effect)
        {
            case ModuleEffect.TonePortamento or ModuleEffect.VolumeSlideAndTonePortamento:
                if (samplePeriod != 0)
                {
                    channel.NotePeriod = samplePeriod;
                    if (note.EffectParameter1 != 0 || note.EffectParameter2 != 0)
                    {
                        channel.SlideAmount = note.EffectParameter;
                    }
                }
                return;

            case ModuleEffect.Vibrato:
                if (note.EffectParameter1 != 0)
                {
                    channel.VibratoSpeed = note.EffectParameter1;
                }

                if (note.EffectParameter2 != 0)
                {
                    channel.VibratoDepth = note.EffectParameter2;
                }
                break;

            case ModuleEffect.Tremolo:
                if (note.EffectParameter1 != 0)
                {
                    channel.TremoloSpeed = note.EffectParameter1;
                }

                if (note.EffectParameter2 != 0)
                {
                    channel.TremoloDepth = note.EffectParameter2;
                }
                break;

            case ModuleEffect.SetTempo:
                if (note.EffectParameter <= 32)
                {
                    speed = note.EffectParameter;
                }
                else
                {
                    speed = 50 * note.EffectParameter;
                }
                break;

            default:
                channel.VibratoAmount = 0;
                break;
        }

        if (sampleNumber != 0)
        {
            channel.Sample = module.Samples[sampleNumber - 1];
            channel.SampleData = module.SampleData[sampleNumber - 1];
            channel.Position = 0;
            channel.Looping = false;
            channel.Volume = channel.Sample.Volume;
            channel.Finetune = channel.Sample.Finetune;
        }

        if (samplePeriod != 0)
        {
            channel.NotePeriod = samplePeriod;
            channel.Period = samplePeriod;
            channel.Position = 0;
            channel.Looping = false;

            if (channel.VibratoRetrigger)
            {
                channel.VibratoTicks = 0;
            }

            if (channel.TremoloRetrigger)
            {
                channel.TremoloTicks = 0;
            }
        }
    }

    private static void RenderChannel(ref ChannelState channel, ModuleNote note, bool firstTick)
    {
        ApplyEffect(ref channel, note, firstTick);

        var (outputData, sample, sampleData, period) = channel;

        if (period == 0)
        { 
            return;
        }

        if (sampleData.Length == 0)
        {
            return;
        }

        var sampleLength = sample.Length * 2;
        var loopPointStart = sample.LoopOffsetStart * 2;
        var loopLength = sample.LoopOffsetLength * 2;
        var loopPointEnd = loopPointStart + loopLength;

        Array.Clear(outputData);

        for (int i = 0; i < outputData.Length; i++)
        {
            var sampledPosition = (int)channel.Position;
            if (sampledPosition >= sampleLength)
            {
                if (sample.LoopOffsetLength < 1)
                {
                    break;
                }

                channel.Looping = true;
            }

            if (channel.Looping && sampledPosition >= loopPointEnd)
            {
                var newStart = loopPointStart + ((sampledPosition - loopPointEnd) % loopLength);
                channel.Position = newStart;
                sampledPosition = newStart;
            }

            var volumeAdjustment = (channel.Volume + channel.TremoloAmount) / 64f;
            outputData[i] = sampleData[sampledPosition] * volumeAdjustment / 128f;

            var finetuneAdjustment = double.Pow(2, channel.Finetune / 96d);
            var periodAdjustment = channel.VibratoAmount;
            var sampleRate = 7093789.2 / ((period + periodAdjustment) * 2 / finetuneAdjustment);
            var ratio = sampleRate / 44100.0;

            channel.Position += ratio;
        }

        channel.VibratoTicks++;
        channel.TremoloTicks++;
    }

    private static void AddWithMax<T>(ref T value, T increment, T max) where T : INumber<T>
    {
        var saturatedSum = T.CreateSaturating(int.CreateTruncating(value) + int.CreateTruncating(increment));
        value = T.Min(saturatedSum, max);
    }

    private static void SubtractWithMin<T>(ref T value, T decrement, T min) where T : INumber<T>
    {
        var saturatedDifference = T.CreateSaturating(int.CreateTruncating(value) - int.CreateTruncating(decrement));
        value = T.Max(saturatedDifference, min);
    }

    private static void ApplyEffect(ref ChannelState channel, ModuleNote note, bool firstTick)
    {
        switch (note.Effect)
        {
            case ModuleEffect.Arpeggio:
                break;

            case ModuleEffect.PortamentoUp when !firstTick:
                SubtractWithMin<short>(ref channel.Period, note.EffectParameter, 113);
                break;

            case ModuleEffect.PortamentoDown when !firstTick:
                AddWithMax<short>(ref channel.Period, note.EffectParameter, 856);
                break;

            case ModuleEffect.TonePortamento when !firstTick:
                TonePortamento(ref channel);
                break;

            case ModuleEffect.Vibrato:
                Vibrato(ref channel);
                break;

            case ModuleEffect.VolumeSlideAndTonePortamento:
                TonePortamento(ref channel);
                VolumeSlide(ref channel, note);
                break;

            case ModuleEffect.VolumeSlideAndVibrato:
                Vibrato(ref channel);
                VolumeSlide(ref channel, note);
                break;

            case ModuleEffect.Tremolo:
                Tremolo(ref channel);
                break;

            case ModuleEffect.SetOffset when firstTick:
                channel.Position = note.EffectParameter * 256;
                break;

            case ModuleEffect.VolumeSlide when !firstTick:
                VolumeSlide(ref channel, note);
                break;

            case ModuleEffect.SetVolume:
                channel.Volume = byte.Clamp(note.EffectParameter, 0, 64);
                break;

            case ModuleEffect.Extended:
                ApplyExtendedEffect(ref channel, note, firstTick);
                break;

            case ModuleEffect.SetTempo:
                break;
        }
    }

    private static void Tremolo(ref ChannelState channel)
    {
        double tremoloAmount = WaveformValue(
            channel.TremoloWaveform,
            channel.TremoloTicks,
            channel.TremoloSpeed,
            channel.TremoloDepth);

        channel.TremoloAmount = (sbyte)(-tremoloAmount * 4);
    }

    private static void Vibrato(ref ChannelState channel)
    {
        double vibratoAmount = WaveformValue(
            channel.VibratoWaveform,
            channel.VibratoTicks,
            channel.VibratoSpeed,
            channel.VibratoDepth);

        channel.VibratoAmount = (sbyte)(-vibratoAmount * 2);
    }

    private static double WaveformValue(OscillatorWaveform waveform, byte ticks, byte speed, byte depth)
    {
        var waveformOffset = ticks * speed % 64;
        var waveformPosition = waveformOffset / 64f;
        double unitWaveformValue = waveform switch
        {
            OscillatorWaveform.Sine => double.SinPi(waveformPosition * 2),
            OscillatorWaveform.Sawtooth => double.Lerp(1, -1, waveformPosition),
            OscillatorWaveform.Square => waveformPosition < 0.5 ? 1 : -1,
            OscillatorWaveform.Random => Random.Shared.NextDouble() * 2 - 1,
            _ => throw new InvalidDataException(),
        };

        return unitWaveformValue * depth;
    }

    private static void VolumeSlide(ref ChannelState channel, ModuleNote note)
    {
        if (note.EffectParameter1 != 0)
            AddWithMax<byte>(ref channel.Volume, note.EffectParameter1, 64);
        else if (note.EffectParameter2 != 0)
            SubtractWithMin<byte>(ref channel.Volume, note.EffectParameter2, 0);
    }

    private static void TonePortamento(ref ChannelState channel)
    {
        if (channel.Period < channel.NotePeriod)
            AddWithMax(ref channel.Period, channel.SlideAmount, channel.NotePeriod);
        else
            SubtractWithMin(ref channel.Period, channel.SlideAmount, channel.NotePeriod);
    }

    private static void ApplyExtendedEffect(ref ChannelState channel, ModuleNote note, bool firstTick)
    {
        switch (note.ExtendedEffect)
        {
            case ModuleExtendedEffect.FinePortamentoUp when firstTick:
                SubtractWithMin<short>(ref channel.Period, note.EffectParameter2, 113);
                break;

            case ModuleExtendedEffect.FinePortamentoDown when firstTick:
                AddWithMax<short>(ref channel.Period, note.EffectParameter2, 856);
                break;

            case ModuleExtendedEffect.FineVolumeSlideUp when firstTick:
                AddWithMax<byte>(ref channel.Volume, note.EffectParameter2, 64);
                break;

            case ModuleExtendedEffect.FineVolumeSlideDown when firstTick:
                SubtractWithMin<byte>(ref channel.Volume, note.EffectParameter2, 0);
                break;

            case ModuleExtendedEffect.SetVibratoWaveform:
                channel.VibratoWaveform = (OscillatorWaveform)(note.EffectParameter2 & 0b11);
                channel.VibratoRetrigger = (note.EffectParameter2 & 0b100) == 0;
                break;

            case ModuleExtendedEffect.SetTremoloWaveform:
                channel.TremoloWaveform = (OscillatorWaveform)(note.EffectParameter2 & 0b11);
                channel.TremoloRetrigger = (note.EffectParameter2 & 0b100) == 0;
                break;
        }
    }

    private static bool IsPositionJump(ModuleNote note, out int sequenceNumber)
    {
        if (note.Effect == ModuleEffect.PositionJump)
        {
            sequenceNumber = note.EffectParameter;
            return true;
        }
        
        sequenceNumber = 0;
        return false;
    }

    private static bool IsPatternBreak(ModuleNote note, out int currentDivision)
    {
        if (note.Effect == ModuleEffect.PatternBreak)
        {
            currentDivision = note.EffectParameter1 * 10 + note.EffectParameter2;
            return true;
        }

        currentDivision = 0;
        return false;
    }

}