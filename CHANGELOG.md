# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This is the changelog of the 0.9 rewrite on `next`. It starts at 0.9.0 and records, per phase,
the decisions users will notice; the 0.8 history is on `main`. The 0.9.0 release notes are the
migration guide.

## [Unreleased]

### Breaking changes

- **0.9 targets `net11.0` only** — every package targets .NET 11 and C# 15; there is no `net10.0` build. Migration: stay on 0.8 until the app moves to .NET 11 (#225)

### Added

- **The 0.9 tree** — a fresh tree on `next`, built from the accepted design in `design/`, with a conformance suite that runs the same protocol vectors against 0.8 and the rewrite (#225)
