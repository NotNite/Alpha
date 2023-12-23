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
          projectFile = "Alpha/Alpha.csproj"; # don't try to build Omega
          nugetDeps =
            ./deps.nix; # to update: `nix build .#default.passthru.fetch-deps && ./result deps.nix`
          dotnet-sdk = pkgs.dotnetCorePackages.sdk_8_0;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_8_0;
          postConfigure = ''
            # Fixes execution of native protoc binaries during build
            for binary in "$HOME"/.nuget/packages/grpc.tools/2.54.0/tools/linux_*/{grpc_csharp_plugin,protoc}; do
              patchelf --set-interpreter "$(cat $NIX_BINTOOLS/nix-support/dynamic-linker)" "$binary"
            done
          '';
          runtimeDeps = [ pkgs.SDL2 pkgs.glib pkgs.gtk3 ];
          nativeBuildInputs = [ pkgs.bintools pkgs.wrapGAppsHook ];
          meta = { mainProgram = "Alpha"; };
        };
      });
}
