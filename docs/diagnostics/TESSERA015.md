# TESSERA015: Tessera referenced without its source generator

**Warning (MSBuild).** `This project references src/Tessera without Tessera's source generator, so no serializers are
generated and Serialize will throw. The generator runs only as an analyzer, which a project reference does not pass
on. Replace the reference with <Import Project="../../src/Tessera.ProjectReference.targets" /> at the end of the
project file (as samples/Quickstart does), or reference the Tessera package.`

A project inside the Tessera repository has a `<ProjectReference>` to `src/Tessera/Tessera.csproj`, and no reference
to the source generator. It compiles, because the attributes and `TesseraSerializer` are in `src/Tessera`, but the
generator that writes every model's serializer runs only when it is referenced as an analyzer, and analyzers do not
flow through project references. Without it, `TesseraSerializer.Serialize` throws `TesseraException` at run time:
no writer was generated.

The check lives in the repository's `Directory.Build.targets`, so it runs only for projects inside the repository's
folder. A project elsewhere gets the run-time exception, whose message names the same fix.

## Fix

Replace the project reference with the import the message names (its path is relative to your project):

```xml
<Import Project="../../src/Tessera.ProjectReference.targets" />
```

It adds the runtime, the generator as an analyzer, and the header export, exactly like the package. Or reference the
Tessera package. See [Installation](../guide/installation.md#b-this-repositorys-projects-in-your-solution).
