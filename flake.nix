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
          version = "2.0.0.5";
          src = ./.;
          projectFile = "Alpha/Alpha.csproj"; # don't try to build Omega
          nugetDeps =
            ./deps.nix; # to update: `nix build .#default.passthru.fetch-deps && ./result deps.nix`
          dotnet-sdk = pkgs.dotnetCorePackages.sdk_9_0;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_9_0;
          runtimeDeps = [ pkgs.SDL2 pkgs.glib pkgs.gtk3 pkgs.glfw pkgs.vulkan-loader ];
          nativeBuildInputs = [ pkgs.bintools pkgs.wrapGAppsHook ];
          meta = { mainProgram = "Alpha"; };
        };
      });
}
