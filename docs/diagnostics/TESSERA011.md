# TESSERA011: Member initializers of another project are not visible

**Warning.** `Model types from 'Game.Data' that are not models there (Monster): initializers of their members (such as
'= 100') are not visible here, so this project takes those members' defaults as zero; in 'Game.Data', mark these
types [Tessera] or list them in [assembly: TesseraRoot], or give the members [DefaultValue]`

Your project uses a type from another project as a model. A member's default comes from its initializer
(`public short Hp = 100;`), and members equal to their default are not stored. But initializers are compiled into
constructors, which the generator cannot read in a referenced assembly. Each project's generator records the
initializers of its own models in its assembly for other projects to read, and the named project did not, because it
does not treat these types as models.

So this project takes zero as those members' defaults. Buffers stay correct (a member equal to 100 is simply stored),
but this project's C++ header returns 0 for an absent member where the other project's returns 100, and a member the
other project left out because it was 100 reads as 0 here.

## Fix

In the project that declares the types, one of:

- mark them [`[Tessera]`](../attributes/tessera.md);
- list them in [`[assembly: TesseraRoot(typeof(Monster))]`](../attributes/tessera-root.md);
- or give the members [`[DefaultValue(100)]`](../attributes/default-value.md), which every project can read.

See also: [Models in several projects](../guide/multiple-projects.md).
