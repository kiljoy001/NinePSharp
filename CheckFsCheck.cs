using System;
using System.Linq;
using System.Reflection;
using FsCheck;

public class CheckFsCheck
{
    public static void Main()
    {
        Console.WriteLine($"FsCheck Assembly: {typeof(Gen).Assembly.FullName}");
        
        var genType = typeof(Gen);
        Console.WriteLine($"Type Gen: {genType.FullName}");
        
        Console.WriteLine("Static Members of Gen:");
        foreach (var m in genType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            Console.WriteLine($"  {m.Name}");
        }

        // Check for a Gen module (which would be a class named Gen in FsCheck namespace usually, or FsCheck.GenModule?)
        var genModule = typeof(Gen).Assembly.GetTypes().FirstOrDefault(t => t.Name == "Gen" && t.IsAbstract && t.IsSealed); // Modules are static classes
        if (genModule != null)
        {
            Console.WriteLine($"
Found Module Gen: {genModule.FullName}");
            foreach (var m in genModule.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                Console.WriteLine($"  {m.Name}");
            }
        }
        else 
        {
             // Maybe it is FsCheck.FSharp.Gen?
             var extra = typeof(Gen).Assembly.GetTypes().Where(t => t.Name.Contains("Gen"));
             Console.WriteLine("
Other Gen types:");
             foreach(var t in extra) Console.WriteLine(t.FullName);
        }
    }
}
