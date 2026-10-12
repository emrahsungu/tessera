# TESSERA008: C++ type name adjusted

**Info.** `C++ type for 'Game.Ui.Button' was renamed to 'Button_2' because another type in the same C++ namespace has
that name`

Two model types map to one C++ name in one C++ namespace. That happens when two C# namespaces map to the same C++
namespace, most often because `TesseraCppNamespace` puts every type of the assembly into one namespace, and two
classes in different C# namespaces share a name.

## Fix

Nothing is wrong: the buffers are the same either way. To choose the C++ names yourself, rename one of the C# types, or
remove `TesseraCppNamespace` so each C# namespace keeps its own C++ namespace ([MSBuild settings](../guide/msbuild.md)).

Renaming a class that is a union member changes its tag; keep the old tag with
[`[Tessera("OldName")]`](../attributes/tessera.md).
