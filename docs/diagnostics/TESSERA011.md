# TESSERA011: Another project's initial field values aren't visible

**Warning.** `Model types from 'Game.Data' that are not models there (Monster): initializers of their members (such as
'= 100') are not visible here, so this project takes those members' defaults as zero; in 'Game.Data', mark these
types [Tessera] or list them in [assembly: TesseraRoot], or give the members [DefaultValue]`

**What it means:** your project saves a class declared in another project (`Game.Data` above), and that class's fields
have initial values (`public short Hp = 100;`).

Initial values matter: a field equal to its default isn't written, and the reader fills in the default. But initial
values are compiled into constructors, and a source generator can't read code inside a referenced assembly. So each
project records the initial values of the classes **it generates code for**, and other projects read them from there.
`Game.Data` doesn't generate code for this class, so nothing was recorded, and this project assumes the defaults are
zero.

Saved data stays correct either way (a field equal to 100 is then simply written). But this project's C++ header
returns 0 for a missing field where `Game.Data`'s would return 100, and data in which a field was left out because it
was 100 reads it as 0 here.

## Fix

In the project that declares the class (`Game.Data`), one of:

- mark the class [`[Tessera]`](../attributes/tessera.md);
- list it in [`[assembly: TesseraRoot(typeof(Monster))]`](../attributes/tessera-root.md);
- or give the fields [`[DefaultValue(100)]`](../attributes/default-value.md), which every project can read.

See also: [Classes in several projects](../guide/multiple-projects.md).
