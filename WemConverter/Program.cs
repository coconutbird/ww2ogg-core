using System;

namespace WemConverter;

public static class Program
{
    public static void Main(string[] args)
    {
        var binary = typeof(Program).Assembly.GetManifestResourceNames();
        
        Console.WriteLine("Hello World!");
    }
}