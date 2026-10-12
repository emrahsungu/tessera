# TESSERA008: A C++ class was renamed to avoid a clash

**Info.** `The C++ class for 'Game.Ui.Button' was renamed to 'Button_2' because another class in the same C++
namespace has that name`

**What it means:** two of your classes would get the same C++ class name in the same C++ namespace, so the generator
added a number to one. This happens when two C# namespaces map to one C++ namespace, usually because
`TesseraCppNamespace` puts every class of the project into one namespace, and two classes in different C# namespaces
have the same name.

## Fix

Nothing is wrong: the saved data is the same either way. To choose the C++ names yourself, rename one of the C#
classes, or remove `TesseraCppNamespace`, so each C# namespace keeps its own C++ namespace
([MSBuild settings](../guide/msbuild.md)).

If the class is stored in fields declared as a base class or interface, renaming it changes its type ID in saved
data; keep the old name with [`[Tessera("OldName")]`](../attributes/tessera.md).
