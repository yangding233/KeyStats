namespace KeyStats.Core;

public readonly record struct KeyProcessResult(KeyId? KeyId, bool Recognized, bool Counted)
{
    public static KeyProcessResult Unrecognized => new(null, false, false);

    public static KeyProcessResult RecognizedOnly(KeyId keyId) => new(keyId, true, false);

    public static KeyProcessResult CountedPress(KeyId keyId) => new(keyId, true, true);
}
