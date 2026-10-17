# TESSERA015: Tessera referenced without its source generator

**Warning (MSBuild).** `This project references src/Tessera without Tessera's source generator, so no serializers are
generated and Serialize will throw. The generator runs only as an analyzer, which a project reference does not pass
on. Replace the reference with <Import Project="../../src/Tessera.ProjectReference.targets" /> at the end of the
project file (as samples/Quickstart does), or reference the Tessera.Serialization package.`

**What it means:** a project inside the Tessera repository references `src/Tessera/Tessera.csproj`, but not the
source generator. It compiles, because the attributes and `TesseraSerializer` are in `src/Tessera`. But the C#
compiler only runs a source generator referenced **as an analyzer**, and a project reference doesn't pass analyzers
on. So no serializers are generated, and `TesseraSerializer.Serialize` throws a `TesseraException` at run time.

The check is in the repository's `Directory.Build.targets`, so it only runs for projects inside the repository's
folder. Elsewhere you get the run-time exception, whose message names the same fix.

## Fix

Replace the project reference with the line from the message (its path is relative to your project):

```xml
<Import Project="../../src/Tessera.ProjectReference.targets" />
```

It adds the library, the generator as an analyzer, and the C++ header export, exactly like the package. Or reference
the Tessera.Serialization package. See [Installation](../guide/installation.md#b-this-repositorys-projects-in-your-solution).
