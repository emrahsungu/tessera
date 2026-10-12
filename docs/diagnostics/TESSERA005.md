# TESSERA005: Too many fields in one class

**Error.** `'Settings' has 5000 serialized members; the limit is 4096`

**What it means:** a class can have at most 4,096 saved fields and properties.

## Fix

Group related fields into classes of their own: `Settings.Audio`, `Settings.Video` and so on. That also makes the
class easier to change later, and a group you don't set costs nothing in the saved data.

Fields that C++ doesn't need can be left out with [`[TesseraIgnore]`](../attributes/tessera-ignore.md).
