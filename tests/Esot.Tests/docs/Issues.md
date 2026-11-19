# Test issues

[한국어](Issues.ko.md)

The 2026-11-19 run had no confirmed outstanding failures. See the [CLI integration results](Unit/CLI/03.%20Results.md) and [UniTest results](UniTest/ThreeWay/03.%20Results.md) for the verified scope.

## Coverage boundaries

The integration test constructs a journal to exercise recovery. It does not terminate a live process or simulate a power failure. The recorded run used Windows. Filesystem behavior on other operating systems was outside its scope.
