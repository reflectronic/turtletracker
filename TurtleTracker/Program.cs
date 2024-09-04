using TurtleTracker;

Console.WriteLine("Enter a file: ");
var path = Console.ReadLine();

var modData = File.ReadAllBytes(path!);

var modFile = ModuleFile.Parse(modData);

Console.WriteLine($"Name: {modFile.Name}");

var player = new ModulePlayer(modFile);
player.Play();
