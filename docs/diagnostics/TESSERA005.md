# TESSERA005: Too many members

**Error.** `'Settings' has 5000 serialized members; the limit is 4096`

One type can have up to 4,096 serialized members.

## Fix

Group related members into nested models: `Settings.Audio`, `Settings.Video` and so on. That also makes the type easier
to evolve, and a missing group costs nothing in the buffer.

Members you don't need in C++ can be left out with [`[TesseraIgnore]`](../attributes/tessera-ignore.md).
