# ATProto.NET

[![CI](https://git.grandiras.net/Grandiras/ATProto.NET/actions/workflows/ci.yml/badge.svg?branch=next)](https://git.grandiras.net/Grandiras/ATProto.NET/actions)

A .NET SDK for the [AT Protocol](https://atproto.com): identifiers and the data model,
repositories, Lexicon, identity, the XRPC client, OAuth, event streams and Spaces, with
ASP.NET Core and EF Core integration.

> **This is the `next` branch: the 0.9 rewrite, in progress.** It is built bottom-up in phases
> ([design/05-rewrite-plan.md](design/05-rewrite-plan.md)) and has no usable API yet. For the
> current release, use [0.8 on `main`](https://git.grandiras.net/Grandiras/ATProto.NET/src/branch/main)
> (`dotnet add package ATProtoNet`).

> ⚠️ This repository is mainly built by coding agents. The maintainer ([Grandiras](https://git.grandiras.net/Grandiras)) is a human.

## Where things are

- [`design/`](design/README.md): the design and specifications the rewrite implements.
- [`docs/`](docs/index.md): user documentation, written per phase as the API lands.
- [`CONTRIBUTING.md`](CONTRIBUTING.md): building, testing and the workflow.
- [`CHANGELOG.md`](CHANGELOG.md): what each phase decided that users will notice.

## License

MIT, see [LICENSE](LICENSE).
