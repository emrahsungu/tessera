# Security policy

## Supported versions

Tessera is in preview. Security fixes go into the latest version only.

## Reporting a vulnerability

Please do not open a public issue. Report the problem privately through GitHub: on this repository's **Security** tab,
choose **Report a vulnerability**. Include the model, a buffer that triggers the problem if you have one, and the
compiler and options you used.

## Scope

`tessera::Reader<T>` and `tessera::verify<T>` are the security boundary for buffers from untrusted sources: after a
successful verification, every access must stay inside the buffer. A buffer that passes verification and then causes
an out-of-bounds read, a crash or undefined behavior is a vulnerability, as is a buffer that makes verification itself
read out of bounds, crash or run without the limits of `Options::max_depth` and `Options::max_items`.

Buffers opened with `tessera::trusted` are not verified by design. Reading an untrusted buffer that way is not a
vulnerability in Tessera.
