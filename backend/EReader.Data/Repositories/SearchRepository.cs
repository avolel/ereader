using EReader.Core.Books;
using EReader.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace EReader.Data.Repositories;

public sealed class SearchRepository : ISearchRepository
{
    private readonly EReaderDbContext _db;

    public SearchRepository(EReaderDbContext db)
    {
        _db = db;
    }

    public async Task<(IReadOnlyList<SearchHit> Items, bool HasMore)> SearchAsync(
        Guid userId,
        string query,
        Guid? bookFilter,
        Guid? cursorBookId,
        int? cursorSpineOrder,
        int pageSize,
        CancellationToken ct)
    {
        // Raw SQL because the Chapters.SearchVector column is a Postgres `tsvector`
        // generated column that EF Core has no first-class type for. Configuring it on
        // the model would force a no-op migration just to align the snapshot — not worth
        // the churn for one query. websearch_to_tsquery handles user-friendly syntax
        // (quoted phrases, AND/OR, -negation) and never throws on malformed input.
        //
        // ts_headline runs against ContentText (not SearchVector) because the headline
        // function needs the original document. MaxFragments=1 keeps the response small.
        var hasBookFilter = bookFilter.HasValue;
        var hasCursor = cursorBookId.HasValue && cursorSpineOrder.HasValue;

        var sql = $"""
            SELECT
                b."Id"            AS "BookId",
                b."Title"         AS "BookTitle",
                b."Author"        AS "BookAuthor",
                c."Id"            AS "ChapterId",
                c."Title"         AS "ChapterTitle",
                c."SpineOrder"    AS "ChapterSpineOrder",
                ts_headline('english', COALESCE(c."ContentText", ''), q,
                    'StartSel=<mark>,StopSel=</mark>,MaxWords=20,MinWords=10,MaxFragments=1,ShortWord=2'
                ) AS "Snippet"
            FROM "Chapters" c
            INNER JOIN "Books" b ON c."BookId" = b."Id"
            CROSS JOIN LATERAL websearch_to_tsquery('english', @query) q
            WHERE b."UserId" = @userId
              AND c."SearchVector" @@ q
            {(hasBookFilter ? "  AND c.\"BookId\" = @bookFilter" : string.Empty)}
            {(hasCursor ? "  AND (b.\"Id\", c.\"SpineOrder\") > (@cursorBook, @cursorSpine)" : string.Empty)}
            ORDER BY b."Id", c."SpineOrder"
            LIMIT @limit;
            """;

        var parameters = new List<NpgsqlParameter>
        {
            new("query", NpgsqlDbType.Text) { Value = query },
            new("userId", NpgsqlDbType.Uuid) { Value = userId },
            new("limit", NpgsqlDbType.Integer) { Value = pageSize + 1 },
        };
        if (hasBookFilter)
        {
            parameters.Add(new NpgsqlParameter("bookFilter", NpgsqlDbType.Uuid) { Value = bookFilter!.Value });
        }
        if (hasCursor)
        {
            parameters.Add(new NpgsqlParameter("cursorBook", NpgsqlDbType.Uuid) { Value = cursorBookId!.Value });
            parameters.Add(new NpgsqlParameter("cursorSpine", NpgsqlDbType.Integer) { Value = cursorSpineOrder!.Value });
        }

        var rows = await _db.Database
            .SqlQueryRaw<SearchHitRow>(sql, parameters.Cast<object>().ToArray())
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var items = rows
            .Select(r => new SearchHit(
                r.BookId,
                r.BookTitle,
                r.BookAuthor,
                r.ChapterId,
                r.ChapterTitle,
                r.ChapterSpineOrder,
                r.Snippet))
            .ToList();
        return (items, hasMore);
    }

    // Materialization target for SqlQueryRaw. Names must match the SELECT aliases.
    private sealed class SearchHitRow
    {
        public Guid BookId { get; set; }
        public string BookTitle { get; set; } = string.Empty;
        public string BookAuthor { get; set; } = string.Empty;
        public Guid ChapterId { get; set; }
        public string? ChapterTitle { get; set; }
        public int ChapterSpineOrder { get; set; }
        public string Snippet { get; set; } = string.Empty;
    }
}
