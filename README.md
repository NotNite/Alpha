# Alpha

[![Nightly builds](https://github.com/NotNite/Alpha/actions/workflows/nightly.yml/badge.svg)](https://github.com/NotNite/Alpha/actions/workflows/nightly.yml)

![Screenshot of Alpha](https://namazu.photos/i/lujfhpt2.png)

Alpha is a FINAL FANTASY XIV toolkit for modding, datamining, and reverse engineering. It is written in C#, uses Lumina for game data, and uses ImGui.NET and Veldrid for rendering.

## Features

- Automatic path list acquisition through [ResLogger](https://rl2.perchbird.dev/)
- Excel sheet browser with [SaintCoinach](https://github.com/xivapi/SaintCoinach) definition support
- Filesystem browser with bulk file exports
- Memory editor via Omega

## Downloads

Alpha does not currently have a stable release, and does not operate under versioning. Nightly builds are available in many ways:

- Artifacts from GitHub Actions are available [in the Actions tab](https://github.com/NotNite/Alpha/actions).
  - Artifacts require a GitHub account to download, though you may use a third party service like [nightly.link](https://nightly.link/NotNite/Alpha/workflows/nightly/main) if you do not have one.
- Users familiar with source control may opt for compiling from source.
- Nix users can use the provided flake to build Alpha from source (Omega is not currently supported through the flake).
  - This flake is not actively monitored and may fail to build. Please open an issue if you encounter troubles!
- A prerelease is available on the Releases tab.

You will need the .NET 8 Runtime. Windows users can find it [here](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.0-windows-x64-installer).

## Omega

Omega is a Dalamud plugin that communicates with Alpha to provide various tools like memory editing. It uses Protobuf over a WebSocket connection (Omega is the server, Alpha is the client).

Omega is inherently unsafe given that you are opening a WebSocket server for arbitrary memory read/writes. As such, a plugin repository is not available, and pull requests to add one will be closed.
