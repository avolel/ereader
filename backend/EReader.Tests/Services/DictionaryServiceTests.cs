using System.IO.Compression;
using System.Text;
using EReader.Core.Exceptions;
using EReader.Core.Services;
using FluentAssertions;

namespace EReader.Tests.Services;

public class DictionaryServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    // The on-disk shape the service expects: gzipped JSON keyed by lower-cased lemma,
    // each sense using the short keys emitted by scripts/build-dictionary.sh.
    private const string Fixture = """
    {
      "read": [
        { "pos": "verb", "definition": "interpret something written or printed", "examples": ["read the sign"] },
        { "pos": "noun", "definition": "something that is read", "examples": [] }
      ],
      "ebook": [
        { "pos": "noun", "definition": "a book in digital format", "examples": [] }
      ]
    }
    """;

    private DictionaryService BuildService(string json = Fixture)
    {
        // Absolute path so Path.Combine(AppContext.BaseDirectory, DataPath) resolves to it
        // (Combine returns the second argument verbatim when it is rooted).
        var path = Path.Combine(Path.GetTempPath(), $"wordnet-test-{Guid.NewGuid():N}.json.gz");
        _tempFiles.Add(path);

        using (var fs = File.Create(path))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        using (var writer = new StreamWriter(gz, Encoding.UTF8))
        {
            writer.Write(json);
        }

        return new DictionaryService(new DictionaryOptions { DataPath = path });
    }

    [Fact]
    public void Should_ReturnSenses_When_WordExists()
    {
        var result = BuildService().Lookup("read");

        result.Found.Should().BeTrue();
        result.Word.Should().Be("read");
        result.Senses.Should().HaveCount(2);
        // Confirms the on-disk "pos" key maps onto DictionarySense.PartOfSpeech.
        result.Senses[0].PartOfSpeech.Should().Be("verb");
        result.Senses[0].Definition.Should().Be("interpret something written or printed");
        result.Senses[0].Examples.Should().ContainSingle().Which.Should().Be("read the sign");
    }

    [Fact]
    public void Should_ReturnFoundFalse_When_WordMissing()
    {
        var result = BuildService().Lookup("zzznotaword");

        result.Found.Should().BeFalse();
        result.Word.Should().Be("zzznotaword");
        result.Senses.Should().BeEmpty();
    }

    [Fact]
    public void Should_MatchCaseInsensitively_When_WordDiffersInCase()
    {
        var result = BuildService().Lookup("READ");

        result.Found.Should().BeTrue();
        result.Word.Should().Be("read");
        result.Senses.Should().HaveCount(2);
    }

    [Fact]
    public void Should_UseFirstToken_When_SelectionIsPhrase()
    {
        // A selection can be a phrase; only the first token is looked up (v1: no lemmatization).
        var result = BuildService().Lookup("read aloud to the class");

        result.Found.Should().BeTrue();
        result.Word.Should().Be("read");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Throw_When_WordEmpty(string word)
    {
        var act = () => BuildService().Lookup(word);

        act.Should().Throw<ValidationException>();
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}