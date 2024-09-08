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
    private ModulePattern pattern;
    private ModuleDivision division;

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

        public short SlideToPeriod;
        public byte SlideAmount;
        
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

        foreach (var currentPattern in module.Sequence)
        {
            pattern = module.Patterns[currentPattern];

            foreach (var currentDivision in pattern.Divisions)
            {
                division = currentDivision;
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
                        outputBuffer[j] = float.Clamp(
                            channel1.AudioBuffer[j] + 
                            channel2.AudioBuffer[j] + 
                            channel3.AudioBuffer[j] + 
                            channel4.AudioBuffer[j],
                            min: -1, max: 1);
                    }

                    Sdl.QueueAudio<float>(audioDevice, outputBuffer, (uint)outputBuffer.Length * 4);
                }
            }
        }
    }

    private void UpdateChannel(ref ChannelState channel, ModuleNote note)
    {
        var (sampleNumber, samplePeriod, effectCommand) = note;

        if (sampleNumber != 0)
        {
            channel.Sample = module.Samples[sampleNumber - 1];
            channel.SampleData = module.SampleData[sampleNumber - 1];
            channel.SamplePosition = 0;
            channel.Looping = false;
            channel.Volume = channel.Sample.Volume;
        }

        if (samplePeriod != 0)
        {
            if (note.Effect == ModuleEffect.TonePortamento)
            {
                channel.SlideToPeriod = samplePeriod;
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

        var sampleRate = 7159090.5 / (period * 2);
        var ratio = sampleRate / 44100.0;

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

            outputData[i] = sampleData[sampledPosition]  * (channel.Volume / 64f) / 128f;
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
                if (channel.Period < channel.SlideToPeriod)
                    AddWithMax(ref channel.Period, channel.SlideAmount, channel.SlideToPeriod);
                else
                    SubtractWithMin(ref channel.Period, channel.SlideAmount, channel.SlideToPeriod);

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
}