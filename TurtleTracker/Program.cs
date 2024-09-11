using TurtleTracker;

Console.WriteLine("Enter a file: ");
var path = Console.ReadLine();

var modData = File.ReadAllBytes(path!);

var modFile = ModuleFile.Parse(modData);

Console.WriteLine($"Name: {modFile.Name}");

var notes = modFile.Patterns
    .SelectMany((p, i) => p.Divisions.Select(d => (d, i)))
    .SelectMany(p => new (int Pattern, ModuleNote Note)[] { (p.i, p.d.Channel1), (p.i, p.d.Channel2), (p.i, p.d.Channel3), (p.i, p.d.Channel4) });

var effects = notes.DistinctBy(n => n.Note.Effect);
var extendedEffects = notes.Where(n => n.Note.Effect == ModuleEffect.Extended).DistinctBy(n => n.Note.ExtendedEffect);

foreach (var (pattern, note) in effects)
{
    var effect = note.Effect;
    Console.WriteLine($"Effect {effect} {(int)effect:X} used in pattern {pattern}.");
}

foreach (var (pattern, note) in extendedEffects)
{
    var effect = note.ExtendedEffect;
    Console.WriteLine($"Extended effect {effect} {(int)effect:X} used in pattern {pattern}.");
}

var player = new ModulePlayer(modFile);
player.Play();

Thread.Sleep(-1);