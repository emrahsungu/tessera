# Reading in .NET

.NET programs read Tessera buffers in two ways:

| | Read in place: `MonsterView.Read(buffer)` | Create objects: `TesseraSerializer.Deserialize<Monster>(buffer)` |
|---|---|---|
| What you get | a read-only view of the buffer, with a property per field | a new `Monster`, with new lists, dictionaries and strings |
| What it costs | checking the buffer once; then each field is read from the buffer when you ask for it | checking the buffer, then creating every object, list and string in it |
| Use it when | you read the data once, or only part of it; for lookups in large buffers; in hot loops | you change the data, keep it after the buffer is gone, or pass it to code that expects your classes |

Both read buffers written by older or newer versions of your classes ([Changing your classes](versioning.md)).

## Read in place

For each class, the source generator writes a read type named after it: `MonsterView` for `Monster`, in the same
namespace. `Read` checks the buffer and returns the object at its top:

```csharp
using Game;
using Tessera;

byte[] buffer = File.ReadAllBytes("monster.bin");
MonsterView orc = MonsterView.Read(buffer);   // checks the buffer once; copies nothing
Vec3 p = orc.Position;
Console.WriteLine($"{orc.Name}: hp {orc.Hp}, mana {orc.Mana}, position {p.X} {p.Y} {p.Z}");
foreach (WeaponView w in orc.Weapons) Console.WriteLine($"  {w.Name}: damage {w.Damage}");
```

```text
Orc: hp 100, mana 25, position 1 2 3
  Axe: damage 9
  Bow: damage 4
```

`Read` throws a `TesseraReadException` when the buffer cannot be read; its `Error` says why ([Errors](#errors)).
`TryRead` returns `false` and the reason instead:

```csharp
if (!MonsterView.TryRead(buffer, out MonsterView orc, out TesseraReadError error))
{
    Console.WriteLine("cannot read monster.bin: " + TesseraReadException.Describe(error));
    return;
}
```

Both take any memory: a `byte[]`, part of an array, a `Memory<byte>`'s span, `stackalloc`'d bytes, or memory from a
memory-mapped file. The buffer needs no alignment.

**Keep the buffer unchanged while you use its views.** A view reads the buffer each time you read a property, so it
must not be modified or returned to a pool while you use the view. Read types are `ref struct`s: the compiler keeps
them on the stack, so a view cannot outlive the method that holds the buffer's span, nor be stored in a field, a
collection, or across an `await`. To keep data, copy the fields you need, or use
[`Deserialize`](#create-objects).

Reading never modifies the buffer, so any number of threads can read one buffer at the same time.

### What each field reads as

| C# field | Read type property |
|---|---|
| numbers, `bool`, `char`, enums | the value; the declared default (`Hp = 100`) when it was not stored |
| nullable value types (`int?`, `Vec3?`) | the value, or `null` |
| `string` | `string?`, decoded each time you read it; `NameUtf8` returns its UTF-8 bytes without decoding or allocating |
| plain structs (`Vec3`) | a copy of the struct |
| a class (`Weapon`) | its read type, `WeaponView`; when the field was null, a view whose `IsNull` is `true` and whose fields all read as their defaults |
| lists and arrays of numbers, `bool`, `char`, enums or plain structs | `ReadOnlySpan<T>` over the buffer: no copy |
| lists of strings | `TesseraStringList`: `Count`, `this[i]`, `GetUtf8(i)`, `IsNull(i)` |
| lists of nullable values (`List<int?>`) | `TesseraNullableList<T>` |
| lists of lists of numbers (`List<int[]>`) | `TesseraSpanList<T>`: each item is a `ReadOnlySpan<T>` |
| lists of classes, of base classes, of other lists | a generated list type: `WeaponViewList` (`Count`, `this[i]`, `foreach`) |
| dictionaries | a generated dictionary type, named after its key and value (`StringInt32Map` for `Dictionary<string, int>`) |
| a field declared as a base class or interface | a generated type named after it (`ItemView` for `Item`): `Tag`, `IsPotion`, `AsPotion` |

Every field also has `Has{Name}`: whether it was stored (`false` when it was null, or equal to its default).

A missing string reads as `null`; a missing list or dictionary reads as empty (`Count` is 0); a missing object reads
as a view whose fields return their defaults, so `save.Player.Weapon.Damage` never throws along the way. Lists check
their index (`ArgumentOutOfRangeException`).

### Dictionaries

A dictionary's keys are stored sorted, so looking a key up is a binary search, without creating anything:

| Member | Returns |
|---|---|
| `TryGetValue(key, out value)` | whether the key is there, and its value |
| `this[key]` | the value; `KeyNotFoundException` when the key is not there |
| `ContainsKey(key)` | whether the key is there |
| `IndexOf(key)` | the key's position in `Keys` and `Values`, or -1 |
| `Keys`, `Values` | the keys in ascending order (strings by their UTF-8 bytes), and the values in the same order |
| `Count` | the number of entries |

Dictionaries with string keys also look up UTF-8 bytes directly: `IndexOf(ReadOnlySpan<byte> utf8Key)`.

### Fields declared as a base class or interface

```csharp
ItemView loot = chest.Loot;
if (loot.IsPotion) Console.WriteLine(loot.AsPotion.Heal);
switch (loot.Tag)
{
    case ItemView.Tags.Key: Open(loot.AsKey.Door); break;
    case ItemView.Tags.None: break;   // the field was null
}
```

`AsPotion` returns a view whose `IsNull` is `true` when the value is another class. A buffer written by a newer version
of your classes can hold a class this program does not know: `Tag` then has a value that is not in `Tags`, and every
`AsX` is null.

## Create objects

```csharp
Monster orc = TesseraSerializer.Deserialize<Monster>(buffer);
```

`Deserialize` checks the buffer as `Read` does and creates the objects in it, in one pass: each object, list and string
is checked right before it is read. It accepts exactly the buffers that `Read` accepts, and fails with the same errors.
`TryDeserialize` returns `false` and the reason instead of throwing:

```csharp
if (!TesseraSerializer.TryDeserialize(buffer, out Monster? orc, out TesseraReadError error)) Reject(error);
```

The generator writes the code for each class the same way it writes serializers: for classes with `[Tessera]`, for
classes listed in `[assembly: TesseraRoot(...)]`, and for each `T` in a `Serialize<T>` or `Deserialize<T>` call, with
every type they use ([Which types get generated code](generated-types.md)).

### How objects are created

Every stored field is set to the value it was written with; a field that was not stored gets its declared default
(null for strings, lists and objects). Fields the class does not store (`[TesseraIgnore]`) keep what the constructor
gave them (nothing, when no constructor runs: see the table), except `required` ones, which C# makes every object
creation set: they get their type's default.

| The class has | It is created with |
|---|---|
| a constructor without parameters that this project can call | that constructor, then each field set as an object initializer would |
| else, a constructor whose parameters all have the names (in any case) and types of fields, as a record's | that constructor (the one with the most parameters), then the other fields |
| else, a private constructor without parameters | that constructor, through an `UnsafeAccessor` |
| none of these | no constructor at all (`RuntimeHelpers.GetUninitializedObject`), then every field |

Fields are set however the class declares them, including those that code outside the class cannot set: private or
`init` setters, get-only auto-properties (`{ get; }`) and `readonly` fields are set directly in the object, through an
`UnsafeAccessor`. `required` members are set like any other field.

Lists and dictionaries are created as their declared types:

| Declared as | Created as |
|---|---|
| `T[]`, `List<T>` | the same |
| `IList<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `IReadOnlyCollection<T>`, `IEnumerable<T>` | `List<T>` |
| `Dictionary<K, V>`, `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>` | `Dictionary<K, V>` |
| any other class with a constructor without parameters and an `Add` method: `HashSet<T>`, `SortedSet<T>`, `LinkedList<T>`, `Collection<T>`, `ObservableCollection<T>`, `SortedDictionary<K, V>`, `SortedList<K, V>`, `ConcurrentDictionary<K, V>`... | the same, filled through `Add` (or the dictionary's indexer) |
| any other class with a constructor taking the items: `Queue<T>`, `Stack<T>`, `ReadOnlyCollection<T>`, `ReadOnlyDictionary<K, V>`... | the same, from a list or dictionary of the items; a stack in its original order |

Other collection types make `Deserialize` throw a `TesseraException` that names the type.

### Repeated values

A buffer written with [`Sharing.All`](../options/sharing.md) stores equal objects and lists once.
`Deserialize` creates one object per reference, as other serializers do: two references to one stored object give
two equal objects. The exception is a buffer that shares items so much that this would create far more objects than
the buffer holds (more than 16 per 4 bytes, as in a deep tree whose branches are all the same): its stored objects
are then created once each, and shared by their references, so that the time and memory stay in proportion to the
buffer's size.

Strings are immutable, so sharing them changes nothing but memory: a string stored once and referenced from several
places (the default, [`Sharing.Strings`](../options/sharing.md)) is usually decoded once and shared by its references.

## Options

`Read`, `TryRead`, `Deserialize` and `TryDeserialize` take a `TesseraReadOptions`:

```csharp
var options = new TesseraReadOptions { ValidateUtf8 = true, MaxDepth = 32 };
MonsterView orc = MonsterView.Read(buffer, options);
```

| Option | Default | What it does |
|---|---|---|
| [`Verify`](../options/dotnet-read-options.md#verify-default-true) | `true` | Checks the buffer before the first read, so that no read can go outside it. |
| [`ValidateUtf8`](../options/dotnet-read-options.md#validateutf8-default-false) | `false` | Also rejects strings that are not valid UTF-8. |
| [`MaxDepth`](../options/dotnet-read-options.md#maxdepth-default-128) | `128` | The deepest nesting of objects and lists to accept. |
| [`MaxItems`](../options/dotnet-read-options.md#maxitems-default-16777216) | 16,777,216 | The most objects and lists to check. |

`TesseraReadOptions.Trusted` skips the check, for data your own program wrote:
[.NET read options](../options/dotnet-read-options.md).

## Errors

| `TesseraReadError` | Meaning |
|---|---|
| `TooSmall`, `BadMagic` | not a Tessera buffer, or truncated before the end of the header |
| `BadVersion` | written in a format version this library does not support |
| `SchemaMismatch` | written with other classes, and the buffer has no description of them (saved with [`IncludeSchema = false`](../options/include-schema.md)), or its top-level class is not the one read |
| `BadSchema` | the description of the classes in the buffer is damaged |
| `OutOfBounds`, `BadOffset`, `BadString`, `BadUnionTag` | damaged (or forged) data |
| `ReservedBits` | bits that must be zero are set: damaged data, or written by a newer version of Tessera |
| `TooDeep`, `TooManyItems` | over `MaxDepth` or `MaxItems` |

`TesseraReadException.Describe(error)` returns a sentence for each.

## Other versions of your classes

A buffer stores a hash of the layout of the classes it was written with. When it matches the reading program's, the
buffer is read where it is. When it does not, the fields are matched by name through the description of the classes
that the buffer carries, and the buffer is converted once into the program's layout, in a new array; the read types
then read that array ([Changing your classes](versioning.md)). Buffers saved with
[`IncludeSchema = false`](../options/include-schema.md) can only be read with the classes they were written with.

Next: [Changing your classes](versioning.md).
