// See https://aka.ms/new-console-template for more information
using System.Reflection;
using TurtleTracker;

Console.WriteLine("Enter a file: ");
var path = Console.ReadLine();

var modData = File.ReadAllBytes(path!);

var modFile = ModuleFile.Parse(modData);

Console.WriteLine($"Name: {modFile.Name}");
