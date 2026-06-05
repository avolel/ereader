namespace EReader.Core.Lookups;

public sealed record DictionarySense(string PartOfSpeech, string Definition, IReadOnlyList<string> Examples);

// Found=false is a normal outcome (FR-26), not an error — we do NOT throw NotFoundException here.
public sealed record DictionaryResult(string Word, bool Found, IReadOnlyList<DictionarySense> Senses);