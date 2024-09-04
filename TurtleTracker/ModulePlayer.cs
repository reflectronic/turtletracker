namespace TurtleTracker;

using Silk.NET.SDL;

public sealed class ModulePlayer
{
    private readonly Sdl Sdl = SdlProvider.SDL.Value;
    private readonly AudioSpec audioSpec;
    private readonly ModuleFile module;

    private uint audioDevice;


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

    // THE PERIOD BETWEEN DIVISONS 
    // IS 1/50th OF A SECOND (ONE TICK)
    // WHEN THE TEMPO IS 125
    // I THINK

    public void Play()
    {
        var outSpec = default(AudioSpec);

        audioDevice = Sdl.OpenAudioDevice((string?)null, 0, in audioSpec, ref outSpec, 0);
        Sdl.ThrowError();

        Sdl.PauseAudioDevice(audioDevice, 0);
        Sdl.ThrowError();

        var renderedSamples = new sbyte[882];

        var speed = 6;
        int lastSampleOffset = 0;

        foreach (var currentPattern in module.Sequence)
        {
            var pattern = module.Patterns[currentPattern];

            ModuleSample currentSample = default!;
            sbyte[] currentSampleData = default!;
            short currentPeriod = 0;

            for (int currentDivision = 0; currentDivision < 64; currentDivision++)
            {
                ModuleDivision division = pattern.Divisions[currentDivision];
                var (sampleNumber, samplePeriod, effectCommand) = division.Channel2;

                if (sampleNumber != 0)
                {
                    currentSample = module.Samples[sampleNumber - 1];
                    currentSampleData = module.SampleData[sampleNumber - 1];
                    lastSampleOffset = 0;
                }

                if (samplePeriod != 0)
                {
                    currentPeriod = samplePeriod;
                    lastSampleOffset = 0;
                }

                for (int i = 0; i < speed; i++)
                {
                    if (currentPeriod != 0)
                    {
                        lastSampleOffset = RenderSample(
                            currentSample, 
                            currentSampleData, 
                            currentPeriod, 
                            lastSampleOffset, 
                            renderedSamples);
                    }

                    Sdl.QueueAudio<sbyte>(audioDevice, renderedSamples, (uint)renderedSamples.Length);
                    // System.Threading.Thread.Sleep(20);
                }
            }
        }
    }

    private static int RenderSample(ModuleSample sample, ReadOnlySpan<sbyte> sampleData, int period, int startOffset, Span<sbyte> @out)
    {
        @out.Clear();

        if (sampleData.Length == 0)
        {
            return 0;
        }

        var sampleRate = (int)(7159090.5 / (period * 2));
        var ratio = sampleRate / 44100.0;

        int resampledOffset = startOffset;
        bool looped = false;

        for (int i = 0; i < @out.Length; i++)
        {
            double interpolatedPoint;
            int lowerSample;
            int upperSample;

            int ToResampledIndex(int sampleOffset) => (int)(sampleOffset / ratio);

            void SetInterpolatedPoint()
            {
                interpolatedPoint = resampledOffset * ratio;
                lowerSample = (int)double.Floor(interpolatedPoint);
                upperSample = (int)double.Ceiling(interpolatedPoint);   
            }


            SetInterpolatedPoint();

            if (upperSample >= sampleData.Length)
            {
                if (sample.LoopOffsetStart > 1)
                {
                    looped = true;
                    resampledOffset = ToResampledIndex(sample.LoopOffsetStart * 2);
                    SetInterpolatedPoint();
                }
                else
                {
                    break;
                }
            }

            var percent = interpolatedPoint - lowerSample;
            var interpolatedSample = double.Lerp(sampleData[lowerSample], sampleData[upperSample], percent);
            @out[i] = (sbyte)interpolatedSample;

            if (looped && resampledOffset == (int)((sample.LoopOffsetStart + sample.LoopOffsetLength) * 2 / ratio))
            {
                resampledOffset = ToResampledIndex(sample.LoopOffsetStart * 2);
            }
            else
            {
                resampledOffset++;
            }
        }

        return resampledOffset;
    }
}