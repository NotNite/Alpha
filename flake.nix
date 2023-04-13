{
  description = ''"what if i made Godbert Two"'';

  inputs = {
    flake-utils.url = "github:numtide/flake-utils";
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs = inputs: inputs.flake-utils.lib.eachDefaultSystem (system: let
    pkgs = inputs.nixpkgs.legacyPackages.${system};
  in {
    packages.default = pkgs.buildDotnetModule {
      pname = "alpha";
      version = "1.0.0.0";
      src = ./.;
      projectFile = "Alpha.sln";
      nugetDeps = ./deps.nix; # to update: `nix build .#default.passthru.fetch-deps && ./result deps.nix`
      dotnet-sdk = pkgs.dotnetCorePackages.sdk_7_0;
      dotnet-runtime = pkgs.dotnetCorePackages.runtime_7_0;
      runtimeDeps = [
        pkgs.SDL2
        pkgs.glib
        pkgs.gtk3
      ];
      nativeBuildInputs = [
        pkgs.wrapGAppsHook
      ];
      meta = {
        mainProgram = "Alpha";
      };
    };
  });
}
