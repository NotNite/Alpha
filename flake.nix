{
  description =
    "FINAL FANTASY XIV toolkit for modding, datamining, and reverse engineering";

  inputs = {
    flake-utils.url = "github:numtide/flake-utils";
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs = inputs:
    inputs.flake-utils.lib.eachDefaultSystem (system:
      let pkgs = inputs.nixpkgs.legacyPackages.${system};
      in {
        packages.default = pkgs.buildDotnetModule {
          pname = "alpha";
          version = "1.0.0.0";
          src = ./.;
          projectFile = "Alpha/Alpha.csproj";
          nugetDeps = ./deps.json; # to update: `nix build .#default.passthru.fetch-deps && ./result deps.json`
          dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
          runtimeDeps = [ pkgs.SDL pkgs.glib pkgs.gtk3 ];
          nativeBuildInputs = [ pkgs.bintools pkgs.wrapGAppsHook3 ];
          meta = { mainProgram = "Alpha"; };
        };
      });
}
