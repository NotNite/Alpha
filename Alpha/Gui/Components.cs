using System.Numerics;
using Alpha.Game;
using Alpha.Services;
using Alpha.Utils;
using Hexa.NET.ImGui;
using NativeFileDialog.Extended;
using Serilog;

namespace Alpha.Gui;

public class Components {
    public static void DrawHelpTooltip(string text, bool sameLine = true) {
        if (sameLine) ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }

    public static void DrawGamePaths(GameDataService gameData) {
        if (ImGui.Button("Add game path")) {
            var dir = NFD.PickFolder(gameData.TryGamePaths());
            if (!string.IsNullOrEmpty(dir)) gameData.AddGamePath(dir);
        }

        if (ImGui.BeginTable("Game Paths Table", 3, ImGuiTableFlags.SizingFixedFit)) {
            ImGui.TableSetupColumn("Path");
            ImGui.TableSetupColumn("Version");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            foreach (var (path, info) in gameData.GameDatas.ToList()) {
                ImGui.PushID(path);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(path);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.GameInstallationInfo.GameVersion ?? "Unknown");

                ImGui.TableNextColumn();

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
        ImGui.BulletText("HexaEngine (UI bindings)");
        ImGui.BulletText("Lumina (SqPack/Excel parsing)");
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

    public static void DrawPathLists(PathListService pathList) {
        var disabled = pathList.IsDownloading;
        if (disabled) ImGui.BeginDisabled();
        if (ImGui.Button("Download path list")) {
            Task.Run(async () => {
                try {
                    await pathList.DownloadResLogger(true);
                } catch (Exception e) {
                    Log.Error(e, "Failed to download path list");
                }
            });
        }
        if (disabled) ImGui.EndDisabled();
        DrawHelpTooltip(
            "Paths in the filesystem are crowdsourced by the community. This will download or update paths from ResLogger2.");

        if (ImGui.BeginTable("Path Lists Table", 2, ImGuiTableFlags.SizingFixedFit)) {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            foreach (var name in pathList.PathLists.ToList()) {
                ImGui.PushID(name);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(name);

                ImGui.TableNextColumn();
                if (ImGui.Button("Delete")) pathList.DeletePathList(name);
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

    public static void DrawFakeHamburger(Action draw) {
        const string contextMenuId = "##fakeHamburger";
        if (ImGui.Button("#")) ImGui.OpenPopup(contextMenuId);
        if (ImGui.BeginPopup(contextMenuId)) {
            draw();
            ImGui.EndPopup();
        }
    }

    public static AlphaGameData? DrawGameDataPicker(GameDataService gameData, AlphaGameData current) {
        AlphaGameData? ret = null;

        var options = gameData.GameDatas.Values.ToList();
        var names = options.Select(x => x.GameInstallationInfo.GameVersion ?? x.GamePath).ToArray();
        var currentIdx = options.IndexOf(current);
        if (ImGui.Combo("##gameDataPicker", ref currentIdx, names, names.Length)) {
            ret = options[currentIdx];
        }

        return ret;
    }

    public static bool DrawEnumCombo<T>(string label, ref T current, T[] values, string[]? names = null)
        where T : struct, Enum {
        var realNames = names ?? values.Select(x => x.ToString()).ToArray();
        var idx = Array.IndexOf(values, current);
        if (idx == -1) idx = 0;

        if (ImGui.Combo(label, ref idx, realNames, realNames.Length)) {
            current = values[idx];
            return true;
        } else {
            return false;
        }
    }
}
