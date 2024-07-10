using System.Numerics;
using Alpha.Services;
using Alpha.Utils;
using ImGuiNET;
using NativeFileDialog.Extended;
using Serilog;

namespace Alpha.Gui;

public class Components {
    public static void DrawGamePaths(GameDataService gameData) {
        ImGui.TextUnformatted("Current game path: " + (gameData.CurrentGamePath ?? "None"));

        if (ImGui.Button("Add game path")) {
            var dir = NFD.PickFolder(gameData.TryGamePaths());
            if (!string.IsNullOrEmpty(dir)) gameData.AddGamePath(dir);
        }

        if (ImGui.BeginTable("Game Paths Table", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit)) {
            ImGui.TableSetupColumn("Path");
            ImGui.TableSetupColumn("Version");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            foreach (var (path, info) in gameData.GamePathInfo.ToList()) {
                ImGui.PushID(path);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(path);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.GameVersion ?? "Unknown");

                ImGui.TableNextColumn();

                var isCurrent = gameData.CurrentGamePath == path;
                if (isCurrent) ImGui.BeginDisabled();
                if (ImGui.Button("Set as current")) gameData.SetGamePath(path);
                if (isCurrent) ImGui.EndDisabled();

                ImGui.SameLine();

                if (ImGui.Button("Remove")) gameData.RemoveGamePath(path);
                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    public static void DrawAbout() {
        ImGui.TextWrapped("Thanks for using Alpha!");
        ImGui.TextWrapped(
            "Alpha is free software under the MIT license. It wouldn't be possible without the effort of people like you, building cool things for a game we love.");
        ImGui.TextWrapped("Thank you to the following developers and libraries for making Alpha possible:");

        ImGui.NewLine();

        ImGui.BulletText("Dear ImGui (the Alpha UI)");
        ImGui.BulletText("Veldrid (the graphics backend)");
        ImGui.BulletText("Lumina, (SqPack/Excel parsing)");
        ImGui.BulletText("SaintCoinach and EXDSchema (Excel schemas)");
        ImGui.BulletText("ResLogger2 (path lists)");

        ImGui.NewLine();

        ImGui.BulletText("ash, perchbird, and the Nightshades for contributing to the original version of Alpha");
        ImGui.BulletText("Many users across the FFXIV modding scene for using and promoting Alpha");
        ImGui.BulletText("You! <3");

        ImGui.NewLine();

        ImGui.TextWrapped("Good luck out there!");

        if (ImGui.Button("Open GitHub page")) Util.OpenLink("https://github.com/NotNite/Alpha");
        ImGui.SameLine();
        if (ImGui.Button("Buy NotNite a Pepsi")) Util.OpenLink("https://notnite.com/givememoney");
    }

    public static void DrawPathLists(PathService path) {
        var disabled = path.IsDownloading;
        if (disabled) ImGui.BeginDisabled();
        if (ImGui.Button("Download path list"))
            Task.Run(async () => {
                try {
                    await path.DownloadResLogger(true);
                } catch (Exception e) {
                    Log.Error(e, "Failed to download path list");
                }
            });
        if (disabled) ImGui.EndDisabled();

        if (ImGui.BeginTable("Path Lists Table", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit)) {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Path Count");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            foreach (var (name, count) in path.PathLists.ToList()) {
                ImGui.PushID(name);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(name);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(count.ToString());

                ImGui.TableNextColumn();
                if (ImGui.Button("Delete")) path.DeletePathList(name);
                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    public static void DrawHorizontalSplitter(ref float width) {
        ImGui.Button("##splitter", new Vector2(5, -1));

        ImGui.SetNextItemAllowOverlap();

        if (ImGui.IsItemActive()) {
            var mouseDelta = ImGui.GetIO().MouseDelta.X;
            width += mouseDelta;
        }
    }
}
