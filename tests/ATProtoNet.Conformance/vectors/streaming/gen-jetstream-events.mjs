// Writes jetstream-events.json: Jetstream v1 and v2 frames (the shapes of Jetstream's README and
// the network.bsky.jetstream.subscribeEvents Lexicons, as 0.8's JetstreamEventParserTests and
// JetstreamV2EventParserTests paste them), each with what a consumer must make of it.
// No dependencies: `node gen-jetstream-events.mjs > jetstream-events.json`.
const v1Create = '{"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1725911162329308,"kind":"commit","commit":{"rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b","record":{"$type":"app.bsky.feed.like","createdAt":"2024-09-09T19:46:02.102Z","subject":{"cid":"bafyreidc6sydkkbchcyg62v77wbhzvb2mvytlmsychqgwf2xojjtirmzj4","uri":"at://did:plc:wa7b35aakoll7hugkrjuf3xf/app.bsky.feed.post/3l3pte3p2e325"}},"cid":"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi"}}'
const v1Delete = '{"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1725911162329309,"kind":"commit","commit":{"rev":"3l3qo2vutsx2b","operation":"delete","collection":"app.bsky.feed.post","rkey":"3l3qo2vuowo2b"}}'
const v1Identity = '{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1725911162329308,"kind":"identity","identity":{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","handle":"yohenrique.com","seq":1409752997,"time":"2024-09-09T19:46:02.102Z"}}'
const v1Account = '{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1725911162329308,"kind":"account","account":{"active":true,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","seq":1409753013,"time":"2024-09-09T19:46:02.102Z"}}'
const v2Commit = '{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#commit","seq":24664288881,"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time":"2024-09-09T19:46:02.329308Z","rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b","cid":"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi","record":{"$type":"app.bsky.feed.like","createdAt":"2024-09-09T19:46:02.102Z","subject":{"cid":"bafyreidc6sydkkbchcyg62v77wbhzvb2mvytlmsychqgwf2xojjtirmzj4","uri":"at://did:plc:wa7b35aakoll7hugkrjuf3xf/app.bsky.feed.post/3l3pte3p2e325"}}}}'
const v2Delete = '{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#commit","seq":24664288882,"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time":"2024-09-09T19:46:02.329309Z","rev":"3l3qo2vutsx2b","operation":"delete","collection":"app.bsky.feed.post","rkey":"3l3qo2vuowo2b"}}'
const v2Identity = '{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#identity","seq":24664288883,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time":"2024-09-09T19:46:02.329308Z","identity":{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","handle":"yohenrique.com","seq":1409752997,"time":"2024-09-09T19:46:02.102Z"}}}'
const v2Account = '{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#account","seq":24664288884,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time":"2024-09-09T19:46:02.329308Z","account":{"active":false,"status":"deleted","did":"did:plc:ufbl4k27gp6kzas5glhz7fim","seq":1409753013,"time":"2024-09-09T19:46:02.102Z"}}}'
const v2Sync = '{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#sync","seq":24664288885,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time":"2024-09-09T19:46:02.329308Z","sync":{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","rev":"3l3qo2vutsw2b","seq":1409753014,"time":"2024-09-09T19:46:02.102Z","blocks":{"$bytes":"emVyb0NBUg=="}}}}'
const v2Info = '{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#info","name":"OutdatedCursor","message":"resumed from seq 24664288000"}}'
const v2Error = '{"$type":"error","error":"ConsumerTooSlow","message":"reader below floor rate"}'

const replace = (text, from, to) => {
  if (!text.includes(from)) throw new Error(`"${from}" is not in the frame`)
  return text.replaceAll(from, to)
}

const like = JSON.parse(v1Create).commit.record
const commitFields = {
  kind: 'event', type: 'commit', did: 'did:plc:eygmaihciaxprqvxpfvl6flk', timeUs: 1725911162329308,
  collection: 'app.bsky.feed.like', rkey: '3l3qo2vuowo2b', operation: 'create', rev: '3l3qo2vutsw2b',
  cid: 'bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi',
  uri: 'at://did:plc:eygmaihciaxprqvxpfvl6flk/app.bsky.feed.like/3l3qo2vuowo2b',
}
const dropped = (reason) => (reason ? { kind: 'dropped', reason } : { kind: 'dropped' })

const cases = []
const add = (name, protocol, frame, expect, spec = 'STR-71') => cases.push({ name, spec, protocol, frame, expect })

// ── v1 ───────────────────────────────────────────────────
add('v1 create', 1, v1Create, { ...commitFields, cursor: null, record: like }, 'STR-64')
add('v1 update', 1, replace(v1Create, '"operation":"create"', '"operation":"update"'), { kind: 'event', operation: 'update' }, 'STR-64')
add('v1 delete', 1, v1Delete, {
  kind: 'event', type: 'commit', operation: 'delete', collection: 'app.bsky.feed.post', rev: '3l3qo2vutsx2b', cid: null, record: null,
}, 'STR-64')
add('v1 identity', 1, v1Identity, {
  kind: 'event', type: 'identity', did: 'did:plc:ufbl4k27gp6kzas5glhz7fim', handle: 'yohenrique.com', seq: 1409752997,
  time: '2024-09-09T19:46:02.102Z',
}, 'STR-64')
add('v1 active account', 1, v1Account, { kind: 'event', type: 'account', active: true, status: null, seq: 1409753013 }, 'STR-64')
add('v1 inactive account', 1, replace(v1Account, '"active":true', '"active":false,"status":"takendown"'),
  { kind: 'event', type: 'account', active: false, status: 'takendown' }, 'STR-64')
add('v1 commit with an invalid collection', 1, replace(v1Create, '"collection":"app.bsky.feed.like"', '"collection":"not an nsid"'), dropped('malformed'))
add('v1 commit with an invalid record key', 1, replace(v1Create, '"rkey":"3l3qo2vuowo2b"', '"rkey":"not a record key"'), dropped('malformed'))
add('v1 commit with a malformed rev', 1, replace(v1Create, '"rev":"3l3qo2vutsw2b"', '"rev":"not-a-tid"'), { kind: 'event', rev: null, rkey: '3l3qo2vuowo2b' })
add('v1 commit with a malformed cid', 1, replace(v1Create, 'bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi', '!!!'), { kind: 'event', cid: null, record: like })
add('v1 identity with a malformed handle', 1, replace(v1Identity, '"handle":"yohenrique.com"', '"handle":"not a handle"'),
  { kind: 'event', type: 'identity', handle: null, did: 'did:plc:ufbl4k27gp6kzas5glhz7fim' })
add('v1 unknown kind', 1, '{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1,"kind":"somethingNew","somethingNew":{}}', dropped('unknownType'))
add('v1 unknown operation', 1, replace(v1Create, '"operation":"create"', '"operation":"merge"'), dropped())
add('v1 not JSON', 1, 'not json at all', dropped('malformed'))
add('v1 truncated JSON', 1, '{"truncated":', dropped('malformed'))
add('v1 empty array', 1, '[]', dropped('malformed'))
add('v1 bare number', 1, '42', dropped('malformed'))
add('v1 missing did', 1, '{"time_us":1,"kind":"commit","commit":{"operation":"create","collection":"a.b.c","rkey":"x"}}', dropped('malformed'))
add('v1 invalid did', 1, replace(v1Create, 'did:plc:eygmaihciaxprqvxpfvl6flk', 'not-a-did'), dropped('malformed'))
add('v1 commit without its body', 1, '{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1,"kind":"commit"}', dropped('malformed'))
add('v1 unknown fields', 1, replace(v1Create, '"kind":"commit"', '"kind":"commit","futureField":{"nested":true}'), { kind: 'event', type: 'commit' })
add('v1 non-numeric time_us', 1, replace(v1Create, '"time_us":1725911162329308', '"time_us":"soon"'), dropped('malformed'))
add('v2 frame on v1', 1, v2Commit, dropped(), 'STR-64')
add('v1 frame from a v2 host', 1,
  '{"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1725911162329308,"cursor":12345,"kind":"commit","commit":{"rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b"}}',
  { kind: 'event', cursor: 12345, timeUs: 1725911162329308 }, 'STR-64')

// ── v2 ───────────────────────────────────────────────────
add('v2 commit', 2, v2Commit, { ...commitFields, cursor: 24664288881, record: like }, 'STR-64')
add('v2 delete', 2, v2Delete, { kind: 'event', type: 'commit', operation: 'delete', cid: null, record: null }, 'STR-64')
add('v2 identity', 2, v2Identity, { kind: 'event', type: 'identity', handle: 'yohenrique.com', seq: 1409752997, cursor: 24664288883 }, 'STR-64')
add('v2 account', 2, v2Account, { kind: 'event', type: 'account', active: false, status: 'deleted', cursor: 24664288884 }, 'STR-64')
add('v2 sync', 2, v2Sync, {
  kind: 'event', type: 'sync', rev: '3l3qo2vutsw2b', seq: 1409753014, cursor: 24664288885, blocks: 'emVyb0NBUg==',
}, 'STR-64')
add('v2 sync with unpadded bytes', 2, replace(v2Sync, 'emVyb0NBUg==', 'emVyb0NBUg'), { kind: 'event', type: 'sync', blocks: 'emVyb0NBUg==' })
add('v2 sync with undecodable blocks', 2, replace(v2Sync, 'emVyb0NBUg==', 'not base64!'), { kind: 'event', type: 'sync', blocks: null, rev: '3l3qo2vutsw2b' })
add('v2 info', 2, v2Info, { kind: 'info', name: 'OutdatedCursor', message: 'resumed from seq 24664288000' }, 'STR-71')
add('v2 error', 2, v2Error, { kind: 'error', error: 'ConsumerTooSlow', message: 'reader below floor rate' }, 'STR-64')
add('v2 unqualified type fragment', 2, replace(v2Commit, 'network.bsky.jetstream.subscribeEvents#commit', '#commit'), { kind: 'event', type: 'commit' }, 'STR-64')
add('v1 frame on v2', 2, '{"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1,"kind":"commit"}', dropped(), 'STR-64')
add('v2 unknown envelope', 2, '{"$type":"heartbeat"}', dropped())
add('v2 unknown message type', 2, '{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#future"}}', dropped())
add('v2 envelope without a payload', 2, '{"$type":"message"}', dropped())
add('v2 error without a name', 2, '{"$type":"error","message":"..."}', dropped())
add('v2 not JSON', 2, 'not json', dropped('malformed'))
add('v2 missing time', 2, replace(v2Commit, '"time":"2024-09-09T19:46:02.329308Z",', ''), dropped())
for (const [time, timeUs] of [
  ['2024-09-09T19:46:02.329308Z', 1725911162329308],
  ['2024-09-09T19:46:02Z', 1725911162000000],
  ['2024-09-09T19:46:02.123456+01:45', 1725904862123456],
  ['2024-09-09T19:46:02.329-14:00', 1725961562329000],
  ['2024-09-09T19:46:02.329-00:00', 1725911162329000],
  ['2024-02-29T00:00:00Z', 1709164800000000],
  ['2023-02-29T00:00:00Z', null],
  ['not a time', null],
])
  add(`v2 time ${time}`, 2, replace(v2Commit, '2024-09-09T19:46:02.329308Z', time), timeUs === null ? dropped() : { kind: 'event', timeUs })
for (const [field, escaped] of [
  ['"collection":"app.bsky.feed.like"', '"collection":"app.bsky.feed.\\u006cike"'],
  ['"rkey":"3l3qo2vuowo2b"', '"rkey":"3l3qo2vu\\u006fwo2b"'],
  ['"did":"did:plc:eygmaihciaxprqvxpfvl6flk"', '"did":"did:plc:eygmaihciaxprqvxpfvl6fl\\u006b"'],
  ['"rev":"3l3qo2vutsw2b"', '"rev":"3l3qo2vutsw2\\u0062"'],
  ['"operation":"create"', '"operation":"cr\\u0065ate"'],
])
  add(`v2 escaped ${field.split('"')[1]}`, 2, replace(v2Commit, field, escaped), { ...commitFields, cursor: 24664288881 })
for (const [name, field, invalid] of [
  ['collection that is not an NSID', '"collection":"app.bsky.feed.like"', '"collection":"app.bsky.feed.-like"'],
  ['collection that is a number', '"collection":"app.bsky.feed.like"', '"collection":7'],
  ['record key that is not one', '"rkey":"3l3qo2vuowo2b"', '"rkey":".."'],
  ['capitalised operation', '"operation":"create"', '"operation":"Create"'],
  ['null operation', '"operation":"create"', '"operation":null'],
  ['unknown operation', '"operation":"create"', '"operation":"merge"'],
  ['invalid did', 'did:plc:eygmaihciaxprqvxpfvl6flk', 'not-a-did'],
  ['non-numeric seq', '"seq":24664288881', '"seq":"next"'],
])
  add(`v2 commit with a ${name}`, 2, replace(v2Commit, field, invalid), dropped())
add('v2 commit with a malformed rev', 2, replace(v2Commit, '"rev":"3l3qo2vutsw2b"', '"rev":"not-a-tid"'), { kind: 'event', rev: null, collection: 'app.bsky.feed.like' })
add('v2 commit with a malformed cid', 2, replace(v2Commit, '"cid":"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi"', '"cid":"Qm-not-a-cid"'),
  { kind: 'event', cid: null, collection: 'app.bsky.feed.like' })
add('v2 unknown fields', 2, replace(v2Commit, '"seq":24664288881', '"seq":24664288881,"prevRev":"3l3q"'), { kind: 'event', type: 'commit' })

console.log(JSON.stringify({
  comment: 'Jetstream frames (protocol 1 or 2) and the subset of the parsed result each must produce; see gen-jetstream-events.mjs.',
  cases,
}, null, 2))
