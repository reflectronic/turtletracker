using TurtleTracker;

Console.WriteLine("Enter a file: ");
var path = Console.ReadLine();

var modData = File.ReadAllBytes(path!);

var modFile = ModuleFile.Parse(modData);

Console.WriteLine($"Name: {modFile.Name}");

var notes = modFile.Patterns
    .SelectMany(p => p.Divisions)
    .SelectMany<ModuleDivision, ModuleNote>(p => [p.Channel1, p.Channel2, p.Channel3, p.Channel4]);

var effects = notes.Select(n => n.Effect).Distinct().Order();
var extendedEffects = notes.Where(n => n.Effect == ModuleEffect.Extended).Select(n => n.ExtendedEffect).Distinct().Order();

foreach (var effect in effects)
{
    Console.WriteLine($"Effect {effect} {(int)effect:X} used.");
}

foreach (var effect in extendedEffects)
{
    Console.WriteLine($"Extended effect {effect} {(int)effect:X} used.");
}

var player = new ModulePlayer(modFile);
player.Play();

Thread.Sleep(-1);