using EReader.Core.Lookups;

namespace EReader.Core.Interfaces;

public interface IDictionaryService
{
    // Synchronous lookup against an in-memory index; never throws on miss — returns Found=false.
    DictionaryResult Lookup(string word);
}