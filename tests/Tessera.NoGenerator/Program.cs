using System;
using Tessera;

// Without the source generator there is no writer for Dto, and Serialize must say how to add the generator.
try
{
    TesseraSerializer.Serialize(new Dto { Name = "x" });
    Console.WriteLine("Serialize did not throw");
    return 1;
}
catch (TesseraException e) when (e.Message.Contains("Tessera.ProjectReference.targets", StringComparison.Ordinal))
{
    Console.WriteLine(e.Message);
    return 0;
}

public sealed class Dto
{
    public string? Name;
}
