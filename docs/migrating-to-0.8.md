# Migrating to 0.8

0.8 catches the SDK up with the protocol: the public Lexicons as of upstream `main` in October 2026,
and the Spaces alpha of 2026-10-01, whose wire format changed. This page lists every breaking change
with what to do about it. The [changelog](../CHANGELOG.md) has the same changes alongside the
additions and fixes.

## Lexicon methods

New optional parameters follow the [parameter order](../CONTRIBUTING.md#lexicon-models-and-clients):
filters before `limit` and `cursor`. A call that passes `limit` or `cursor` positionally stops
compiling or binds the wrong parameter; name them.

| Method | New parameter |
|---|---|
| `Feed.GetTimelineAsync` | `since`, after `algorithm` |
| `Feed.GetListFeedAsync` | `since`, after `list` |
| `Feed.GetQuotesAsync` | `sort` (`latest` or `top`) |
| `Ozone.Queue.CreateQueueAsync`, `UpdateQueueAsync` | `recommendedLabels` |

`Ozone.Report.GetLiveStatsAsync` returns its counts as a `LiveStats`, a `QueueStats` with the
fields upstream added to live statistics. Code that reads `Stats` as a `QueueStats` keeps working.
