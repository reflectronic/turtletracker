namespace TurtleTracker;

using Silk.NET.SDL;

public sealed class ModulePlayer
{
    private readonly Sdl Sdl = SdlProvider.SDL.Value;
    private readonly AudioSpec audioSpec;
    private readonly ModuleFile module;

    private int patternIndex;
    private int divisionIndex;

    private uint audioDevice;


    public unsafe ModulePlayer(ModuleFile module)
    {
        audioSpec = new AudioSpec
        {
            Channels = 2,
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

        var audioBuf = new sbyte[100_000];
        var pattern = module.Patterns[patternIndex];

        var resampledTick = new sbyte[882];

        for (int i = 0; i < module.SampleData.Length; i++)
        {
            // var division = pattern.Divisions[divisionIndex];
            // var (sampleIndex, samplePeriod, effectCommand) = division.Channel1;
            // var sample = module.SampleData[4];

            var sample = module.SampleData[i];
            unsafe 
            {
                int bufSize = Resample(sample, 8287, audioBuf);
                fixed (sbyte* audio = audioBuf)
                {
                    Sdl.QueueAudio(audioDevice, audio, (uint)bufSize);
                }
            }

            System.Threading.Thread.Sleep(2000);
        }
    }

    private int Resample(ReadOnlySpan<sbyte> sample, int sampleRate, Span<sbyte> @out)
    {
        var ratio = sampleRate / 44100.0;        

        int i;
        for (i = 0; i < @out.Length; i++)
        {
            var interpolatedPoint = i * ratio;
            var lowerSample = (int)double.Floor(interpolatedPoint);
            var upperSample = (int)double.Ceiling(interpolatedPoint);

            if (upperSample >= sample.Length)
            {
                break;
            }

            var percent = interpolatedPoint - lowerSample;

            var interpolatedSample = double.Lerp(sample[lowerSample], sample[upperSample], percent);

            @out[i] = (sbyte)interpolatedSample;
        }

        return i;
    }
}