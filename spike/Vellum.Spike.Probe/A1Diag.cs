using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

/// <summary>
/// The timing run showed the InvalidateFrom arm producing a *stale* layout. Before concluding
/// anything about A1, establish whether that is the API's behaviour or a misuse on our side:
/// sweep the invalidation offset, test full Invalidate(), and test the no-edit case.
/// </summary>
public static class A1Diag
{
    public static Report Run()
    {
        var r = new Report("A1 diagnostic — is the cached layout stale, and is that our fault?");

        r.Line();
        r.Line("The timing harness found the `InvalidateFrom` arm returning identical geometry for "
               + "every edit. This section establishes what the cache actually does.");

        const int Length = 100;
        var baseText = Scenarios.Paragraph(Length);

        // ------------------------------------------------------------------ 1. no edit at all
        r.Section("1. Control — cache reused with the text completely unchanged");

        {
            var source = new BenchSource(baseText);
            using var cache = new TextRunCache();
            using var first = A1Common.Build(source, cache);
            var f1 = A1Common.Fingerprint(first);
            using var second = A1Common.Build(source, cache);
            var f2 = A1Common.Fingerprint(second);
            using var plain = A1Common.Build(new BenchSource(baseText), null);
            var fp = A1Common.Fingerprint(plain);

            r.Line($"- prime  : {f1}");
            r.Line($"- reuse  : {f2}");
            r.Line($"- nocache: {fp}");
            r.Line();
            r.Check(f1 == f2 && f2 == fp,
                "with unchanged text, a reused cache reproduces the uncached layout exactly");
        }

        // ------------------------------------------------------------------ 2. offset sweep
        r.Section("2. After a 1-char insert, sweep the InvalidateFrom offset");

        r.Line("Insert one character at index 10 of a 100-char paragraph, then build with a primed "
               + "cache after calling `InvalidateFrom(k)` for each k below. `expected` is the "
               + "uncached layout of the edited text.");
        r.Line();

        const int EditOffset = 10;
        var editedText = A1Common.Insert(baseText, EditOffset, 'Z');

        using (var plainEdited = A1Common.Build(new BenchSource(editedText), null))
        {
            var expected = A1Common.Fingerprint(plainEdited);
            r.Line($"expected (uncached, edited text): `{expected}`");
            r.Line($"edited text length: {editedText.Length}; "
                   + $"sum of line lengths expected: {editedText.Length + 1} (incl. TextEndOfParagraph)");
            r.Line();

            var rows = new List<string>();
            var anyCorrect = false;

            foreach (var k in new[] { 0, 1, 5, 9, 10, 11, 25, 50 })
            {
                var source = new BenchSource(baseText);
                using var cache = new TextRunCache();
                using (A1Common.Build(source, cache)) { }

                source.Text = editedText;
                cache.InvalidateFrom(k);
                using var got = A1Common.Build(source, cache);
                var fp = A1Common.Fingerprint(got);
                var sum = got.TextLines.Sum(l => l.Length);
                var ok = fp == expected;
                anyCorrect |= ok;
                rows.Add($"{k}|{sum}|{(ok ? "**yes**" : "no")}|`{fp}`");
            }

            r.Table("InvalidateFrom(k)|sum of line lengths|matches uncached?|fingerprint", rows.ToArray());

            r.Line();
            r.Check(anyCorrect,
                "*some* InvalidateFrom offset yields a layout matching the uncached result",
                anyCorrect ? "" : "no offset produced a correct layout");
        }

        // ------------------------------------------------------------------ 3. full Invalidate
        r.Section("3. Does full `Invalidate()` produce a correct layout?");

        {
            var correct = 0;
            var total = 0;
            string? sampleGot = null, sampleWant = null;

            foreach (var pct in new[] { 10, 50, 90 })
            foreach (var edit in new[] { 0, 1, 2, 3, 4 })
            {
                var offset = Length * pct / 100;
                var text = A1Common.Insert(baseText, offset, (char)('a' + edit));

                var source = new BenchSource(baseText);
                using var cache = new TextRunCache();
                using (A1Common.Build(source, cache)) { }

                source.Text = text;
                cache.Invalidate();
                using var got = A1Common.Build(source, cache);
                using var want = A1Common.Build(new BenchSource(text), null);

                total++;
                if (A1Common.Fingerprint(got) == A1Common.Fingerprint(want)) correct++;
                else { sampleGot ??= A1Common.Fingerprint(got); sampleWant ??= A1Common.Fingerprint(want); }
            }

            r.Line($"Correct layouts after full `Invalidate()`: {correct}/{total}");
            if (sampleGot is not null)
            {
                r.Line($"- sample got : {sampleGot}");
                r.Line($"- sample want: {sampleWant}");
            }
            r.Line();
            r.Check(correct == total,
                "full `Invalidate()` always reproduces the uncached layout after an edit");
        }

        // ------------------------------------------------------------------ 4. fresh cache per edit
        r.Section("4. Does a fresh cache per edit produce a correct layout?");

        {
            var correct = 0;
            var total = 0;

            foreach (var pct in new[] { 10, 50, 90 })
            foreach (var edit in new[] { 0, 1, 2, 3, 4 })
            {
                var offset = Length * pct / 100;
                var text = A1Common.Insert(baseText, offset, (char)('a' + edit));

                using var cache = new TextRunCache();
                using var got = A1Common.Build(new BenchSource(text), cache);
                using var want = A1Common.Build(new BenchSource(text), null);

                total++;
                if (A1Common.Fingerprint(got) == A1Common.Fingerprint(want)) correct++;
            }

            r.Line($"Correct layouts with a fresh cache per edit: {correct}/{total}");
            r.Line();
            r.Check(correct == total, "a fresh `TextRunCache` per build reproduces the uncached layout");
        }

        // ------------------------------------------------------------------ 5. is it stale text?
        r.Section("5. Confirming the failure mode is stale *text*, not stale metrics");

        {
            var source = new BenchSource(baseText);
            using var cache = new TextRunCache();
            using (A1Common.Build(source, cache)) { }

            source.Text = editedText;
            cache.InvalidateFrom(EditOffset);
            using var got = A1Common.Build(source, cache);

            var rendered = string.Concat(
                got.TextLines.SelectMany(l => l.TextRuns)
                   .OfType<ShapedTextRun>()
                   .Select(sr => sr.Text.Span.ToString()));

            r.Line($"- source text now  : `{Scenarios.Escape(editedText[..30])}...` (len {editedText.Length})");
            r.Line($"- text in the runs : `{Scenarios.Escape(rendered[..Math.Min(30, rendered.Length)])}...` (len {rendered.Length})");
            r.Line();
            r.Check(rendered.Length == baseText.Length && !rendered.Contains('Z'),
                "confirmed: after `InvalidateFrom(10)` the shaped runs hold the **pre-edit** text — "
                + $"{rendered.Length} chars, and the inserted 'Z' is absent. The stale result is in "
                + "the text itself, not merely in cached metrics",
                rendered.Length == baseText.Length && !rendered.Contains('Z')
                    ? ""
                    : $"expected stale pre-edit text of {baseText.Length} chars, got {rendered.Length}");
        }

        r.Section("6. How much does the formatter re-query the source in each mode?");

        r.Line("An instrumented source counts `GetTextRun` calls. This shows directly whether the "
               + "formatter re-reads the edited text or serves the whole paragraph from the cache.");
        r.Line();

        {
            var rows = new List<string>();

            foreach (var mode in new[] { "no cache", "Invalidate()", "InvalidateFrom(0)",
                                         "InvalidateFrom(10)", "InvalidateFrom(50)" })
            {
                var source = new CountingSource(baseText);
                TextRunCache? cache = mode == "no cache" ? null : new TextRunCache();
                if (cache is not null) using (A1Common.Build(source, cache)) { }

                source.Text = editedText;
                source.Reset();

                switch (mode)
                {
                    case "Invalidate()": cache!.Invalidate(); break;
                    case "InvalidateFrom(0)": cache!.InvalidateFrom(0); break;
                    case "InvalidateFrom(10)": cache!.InvalidateFrom(10); break;
                    case "InvalidateFrom(50)": cache!.InvalidateFrom(50); break;
                }

                using var layout = A1Common.Build(source, cache);
                var ok = A1Common.Fingerprint(layout) == A1Common.Fingerprint(
                    A1Common.Build(new BenchSource(editedText), null));

                rows.Add($"{mode}|{source.Calls}|{(source.Indices.Count == 0 ? "—" : string.Join(",", source.Indices))}|{(ok ? "**yes**" : "no")}");
                cache?.Dispose();
            }

            r.Table("mode|GetTextRun calls|indices requested|layout correct?", rows.ToArray());

            r.Line();
            r.Line("A mode that re-queries **zero** positions cannot possibly reflect the edit — its "
                   + "speed is the speed of returning a stale result.");
        }

        r.Section("A1 diagnostic result");
        r.Line($"Checks failed: {r.Failures}");
        return r;
    }
}
