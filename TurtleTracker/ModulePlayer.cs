namespace TurtleTracker;

using System.Threading.Channels;
using Silk.NET.SDL;

public sealed class ModulePlayer
{
    public unsafe ModulePlayer(ModuleFile module)
    {
        audioSpec = new AudioSpec
        {
            Channels = 1,
            Samples = 1024,
            Freq = 44100,
            Format = Sdl.AudioS8
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
        sbyte[] AudioBuffer, 
        ModuleSample Sample = default!,
        sbyte[] SampleData = default!,
        short Period = 0,
        double SamplePosition = 0,
        bool Looping = false);

    private ChannelState channel1 = new(new sbyte[882]);
    private ChannelState channel2 = new(new sbyte[882]);
    private ChannelState channel3 = new(new sbyte[882]);
    private ChannelState channel4 = new(new sbyte[882]);

    public void Play()
    {
        var outSpec = default(AudioSpec);

        audioDevice = Sdl.OpenAudioDevice((string?)null, 0, in audioSpec, ref outSpec, 0);
        Sdl.ThrowError();

        Sdl.PauseAudioDevice(audioDevice, 0);
        Sdl.ThrowError();

        var outputBuffer = new sbyte[882];

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
                    RenderChannel(ref channel1);
                    RenderChannel(ref channel2);
                    RenderChannel(ref channel3);
                    RenderChannel(ref channel4);

                    for (int j = 0; j < outputBuffer.Length; j++)
                    {
                        outputBuffer[j] = sbyte.CreateSaturating(
                            channel1.AudioBuffer[j] + 
                            channel2.AudioBuffer[j] + 
                            channel3.AudioBuffer[j] + 
                            channel4.AudioBuffer[j]);
                    }

                    Sdl.QueueAudio<sbyte>(audioDevice, outputBuffer, (uint)outputBuffer.Length);
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
        }

        if (samplePeriod != 0)
        {
            channel.Period = samplePeriod;
            channel.SamplePosition = 0;
            channel.Looping = false;
        }
    }

    private static void RenderChannel(ref ChannelState channel)
    {
        var (outputData, sample, sampleData, period, _, _) = channel;

        if (period == 0)
        { 
            return;
        }

        if (sampleData.Length == 0)
        {
            return;
        }

        var sampleRate = (int)(7159090.5 / (period * 2));
        var ratio = sampleRate / 44100.0;

        var sampleLength = sample.Length * 2;
        var loopPointStart = sample.LoopOffsetStart * 2;
        var loopPointEnd = loopPointStart + sample.LoopOffsetLength * 2;

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
                channel.SamplePosition = loopPointStart;
                sampledPosition = loopPointStart;
            }

            outputData[i] = sampleData[sampledPosition];
            channel.SamplePosition += ratio;
        }
    }
}