using Crawler.Domain.Content;

namespace Crawler.UnitTests.Content;

public class ContentFingerprintTests
{
    [Fact]
    public void Same_content_gives_the_same_fingerprint() =>
        Assert.Equal(
            ContentFingerprint.Compute("<html><body>Hello</body></html>"),
            ContentFingerprint.Compute("<html><body>Hello</body></html>"));

    [Fact]
    public void Different_content_gives_different_fingerprints() =>
        Assert.NotEqual(
            ContentFingerprint.Compute("<html><body>Hello</body></html>"),
            ContentFingerprint.Compute("<html><body>Hello!</body></html>"));

    [Fact]
    public void Line_endings_and_surrounding_whitespace_are_ignored() =>
        Assert.Equal(
            ContentFingerprint.Compute("<html>\n<body>Hi</body>\n</html>"),
            ContentFingerprint.Compute("  <html>\r\n<body>Hi</body>\r\n</html>\r\n"));

    [Fact]
    public void Fingerprint_is_lowercase_hex_sha256()
    {
        var fingerprint = ContentFingerprint.Compute("x");

        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }
}
