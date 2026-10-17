# Subsystem specs

Normative behaviour for each subsystem of the rewrite. Structure is in
[02 — Architecture](../02-architecture.md), API style in
[03 — API guidelines](../03-api-guidelines.md), choices and their reasons in
[06 — Decisions](../06-decisions.md).

| Spec | Prefix | Package(s) | Phase |
|---|---|---|---|
| [Identifier syntax](syntax.md) | `SYN` | Primitives | 1 |
| [Data model: DRISL-CBOR, JSON, CID, CAR](data-model.md) | `DAT` | Primitives | 1 |
| [Cryptography and JOSE](crypto.md) | `CRY` | Primitives | 1 |
| [Repository: MST, commits, proofs](repo.md) | `REP` | Repo | 2 |
| [Lexicon: schema model, validation, code generation](lexicon.md) | `LEX` | Lexicon, CodeGen, Generator | 3, 8 |
| [Tooling: `atproto-lexgen`](tooling.md) | `TOO` | CLI | 3, 11 |
| [Identity: fetch policy, DIDs, handles, PLC](identity.md) | `IDN` | Identity | 4 |
| [XRPC client](xrpc-client.md) | `XRC` | ATProtoNet | 5 |
| [Authentication: sessions, OAuth, service auth](auth.md) | `AUT` | ATProtoNet | 5, 6 |
| [Labels](labels.md) | `LBL` | ATProtoNet, Streaming, AspNetCore | 6 |
| [Event streams](streaming.md) | `STR` | Streaming | 7 |
| [XRPC server and ASP.NET Core](server.md) | `SRV` | AspNetCore | 9 |
| [Hosting, DI and integrations](hosting.md) | `HST` | Hosting, Blazor, Aspire.Hosting | 9, 11 |
| [Stores](stores.md) | `STO` | all store interfaces, Hosting, EntityFrameworkCore | 4–9 |
| [Spaces (experimental)](spaces.md) | `SPC` | Spaces family | 10 |

## Conventions

### Keywords

**MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT** and **MAY** are used as in
[RFC 2119](https://www.rfc-editor.org/rfc/rfc2119). A MUST is a requirement a test can check;
a SHOULD needs a written reason to deviate from.

### Requirement IDs

Each requirement has a stable ID: `{PREFIX}-{n}` (`SYN-12`). IDs are never reused or renumbered.
A removed requirement keeps its ID with the text struck through and a note. Tests reference IDs
with `[Trait("Spec", "SYN-12")]`. The CI coverage report lists IDs without a test.

### Requirement format

```markdown
- **SYN-12** (MUST) A DID is at most 2,048 characters.
  *Source:* [atproto DID spec](https://atproto.com/specs/did) · 0.8 `IdentifierSyntax.cs:44` ·
  test `DidTests.Parse_TooLong_Throws`
```

*Source* names where the rule comes from: an atproto.com spec section, an RFC, the upstream
reference implementation, a 0.8 behaviour (file:line or test name) or issue (`#123`), or
**Decision** when this design chose it (with a link to the decision record or a one-line reason).
Requirements carried over from 0.8 name the 0.8 test that pins them, so the rewrite knows which
scenario to port.

### Spec layout

Every spec has these sections, in this order:

1. **Header**: status, packages, phase, normative references.
2. **Scope**: what is in and out.
3. **Public API sketch**: the main types and members in C#, consistent with the API guidelines.
   It is a sketch, not a contract: names may change in review, behaviour may not.
4. **Requirements**: grouped by topic.
5. **Limits**: every bound on untrusted input as a named constant with its value
   (principle 2 in [00 — Vision](../00-vision.md#design-principles)).
6. **Errors**: which exception types or verdicts each failure produces.
7. **Conformance and tests**: vectors from the conformance suite, and the 0.8 test classes whose
   scenarios must be ported.
8. **Changes from 0.8**: deliberate behaviour changes, each with its reason, so nobody "fixes"
   them back.
9. **Open points**: questions to settle before or during the phase, each with a proposed default.
