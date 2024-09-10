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
        public double SamplePosition;
        public bool Looping;

        public short NotePeriod;
        public byte SlideAmount;

        public sbyte VibratoAmount;
        public sbyte SampleFinetune;
        
        public byte Volume;
    };

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

        if (sampleNumber != 0)
        {
            channel.Sample = module.Samples[sampleNumber - 1];
            channel.SampleData = module.SampleData[sampleNumber - 1];
            channel.SamplePosition = 0;
            channel.Looping = false;
            channel.Volume = channel.Sample.Volume;
            channel.SampleFinetune = channel.Sample.Finetune;
        }

        if (samplePeriod != 0)
        {
            channel.NotePeriod = samplePeriod;

            if (note.Effect == ModuleEffect.TonePortamento)
            {
                if (note.EffectParameter1 != 0 || note.EffectParameter2 != 0)
                {
                    channel.SlideAmount = note.EffectParameter;
                }
            }
            else
            {
                channel.Period = samplePeriod;
                channel.SamplePosition = 0;
                channel.Looping = false;
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
        var loopLength =  sample.LoopOffsetLength * 2;
        var loopPointEnd = loopPointStart + loopLength;

        Array.Clear(outputData);

        for (int i = 0; i < outputData.Length; i++)
        {
            var sampledPosition = (int)channel.SamplePosition;
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
                channel.SamplePosition = newStart;
                sampledPosition = newStart;
            }

            var volumeAdjustment = channel.Volume / 64f;
            outputData[i] = sampleData[sampledPosition] * volumeAdjustment / 128f;

            var semitoneAdjustment = channel.SampleFinetune / 8f + channel.VibratoAmount / 16f;
            var finetuneAdjustment = float.Pow(2, semitoneAdjustment / 12f);
            var sampleRate = 7159090.5 / (period * 2 / finetuneAdjustment);
            var ratio = sampleRate / 44100.0;

            channel.SamplePosition += ratio;
        }
    }

    private static void ApplyEffect(ref ChannelState channel, ModuleNote note, bool firstTick)
    {
        void AddWithMax<T>(ref T value, T increment, T max) where T : INumber<T>
        {
            var saturatedSum = T.CreateSaturating(int.CreateTruncating(value) + int.CreateTruncating(increment));
            value = T.Min(saturatedSum, max);
        }

        void SubtractWithMin<T>(ref T value, T decrement, T min) where T : INumber<T>
        {
            var saturatedDifference = T.CreateSaturating(int.CreateTruncating(value) - int.CreateTruncating(decrement));
            value = T.Max(saturatedDifference, min);
        }

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
                if (channel.Period < channel.NotePeriod)
                    AddWithMax(ref channel.Period, channel.SlideAmount, channel.NotePeriod);
                else
                    SubtractWithMin(ref channel.Period, channel.SlideAmount, channel.NotePeriod);

                break;

            case ModuleEffect.SetOffset when firstTick:
                channel.SamplePosition = note.EffectParameter * 256;
                break;

            case ModuleEffect.VolumeSlide when !firstTick:
                if (note.EffectParameter1 != 0)
                    AddWithMax<byte>(ref channel.Volume, note.EffectParameter1, 64);
                else if (note.EffectParameter2 != 0)
                    SubtractWithMin<byte>(ref channel.Volume, note.EffectParameter2, 0);

                break;

            case ModuleEffect.SetVolume:
                channel.Volume = byte.Clamp(note.EffectParameter, 0, 64);
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