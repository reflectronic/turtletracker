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

        var audioBuf = new sbyte[882];

        var speed = 6;
        int samplesPlayed = 0;

        foreach (var currentPattern in module.Sequence)
        {
            var pattern = module.Patterns[currentPattern];

            sbyte[] currentSample = default!;

            for (int currentDivision = 0; currentDivision < 64; currentDivision++)
            {
                ModuleDivision division = pattern.Divisions[currentDivision];
                var (sampleNumber, samplePeriod, effectCommand) = division.Channel2;

                if (sampleNumber != 0 || samplePeriod != 0)
                {
                    if (sampleNumber != 0)
                    {
                        currentSample = module.SampleData[sampleNumber - 1];
                    }

                    samplesPlayed = 0;
                }

                for (int i = 0; i < speed; i++)
                {
                    int bytesWritten = Resample(currentSample, (int)(7159090.5 / (samplePeriod * 2)), audioBuf, samplesPlayed);
                    samplesPlayed += 882;
                    Sdl.QueueAudio<sbyte>(audioDevice, audioBuf, (uint)audioBuf.Length);
                }
            }
        }
    }

    private static int Resample(ReadOnlySpan<sbyte> sample, int sampleRate, Span<sbyte> @out, int sampleStart)
    {
        @out.Clear();

        if (sample.Length == 0)
        {
            return 0;
        }

        var ratio = sampleRate / 44100.0;

        int i;
        for (i = 0; i < @out.Length; i++)
        {
            var interpolatedPoint = (sampleStart + i) * ratio;
            var lowerSample = (int)double.Floor(interpolatedPoint);
            var upperSample = (int)double.Ceiling(interpolatedPoint);

            if (upperSample > sample.Length)
            {
                break;
            }

            if (upperSample == sample.Length)
            {
                @out[i] = sample[lowerSample];
                break;
            }

            var percent = interpolatedPoint - lowerSample;

            var interpolatedSample = double.Lerp(sample[lowerSample], sample[upperSample], percent);

            @out[i] = (sbyte)interpolatedSample;
        }

        return i;
    }
}