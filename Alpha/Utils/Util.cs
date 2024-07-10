using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumina.Data.Files;
using NativeFileDialog.Extended;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Alpha.Utils;

public static class Util {
    public static void OpenLink(string url) {
        switch (Environment.OSVersion.Platform) {
            case PlatformID.Unix:
                Process.Start("xdg-open", url);
                break;

            case PlatformID.MacOSX:
                Process.Start("open", url);
                break;

            default:
                Process.Start(url);
                break;
        }
    }

    public static (uint Folder, uint File) GetHash(string path) {
        path = path.ToLower();
        var folder = path.AsSpan(0, path.LastIndexOf('/'));
        var file = path.AsSpan(path.LastIndexOf('/') + 1);

        var folderHash = Lumina.Misc.Crc32.Get(MemoryMarshal.Cast<char, byte>(folder));
        var fileHash = Lumina.Misc.Crc32.Get(MemoryMarshal.Cast<char, byte>(file));
        return (folderHash, fileHash);
    }

    public static ulong GetFullHash(string path) {
        var (folder, file) = GetHash(path);
        return ((ulong) folder << 32) | file;
    }

    public static ulong GetFullHash(uint folder, uint file) {
        return ((ulong) folder << 32) | file;
    }

    public static (uint Folder, uint File) GetHash(ulong fullHash) {
        return ((uint) (fullHash >> 32), (uint) fullHash);
    }

    public static string PrintFileHash(uint hash) {
        return "~" + hash.ToString("X8");
    }

    public static string PrintFileHash(ulong hash) {
        var (folder, file) = GetHash(hash);
        return $"{PrintFileHash(folder)}/{PrintFileHash(file)}";
    }

    public static void ExportAsTex(TexFile tex) {
        var bytes = tex.Data;
        var filename = tex.FilePath.Path.Split('/').Last().Replace(".tex", "");
        Save(bytes, "tex", filename);
    }

    public static void ExportAsPng(TexFile tex) {
        var img = Image.LoadPixelData<Bgra32>(tex.ImageData, tex.Header.Width, tex.Header.Height);
        var bytes = new MemoryStream();
        img.SaveAsPng(bytes);

        var filename = tex.FilePath.Path.Split('/').Last().Replace(".tex", "");
        Save(bytes.ToArray(), "png", filename);
    }

    public static void Save(byte[] data, string extension, string name = "file") {
        var result = NFD.SaveDialog(string.Empty, $"{name}.{extension}", new Dictionary<string, string> {
            {extension.ToUpper(), $"*.{extension}"}
        });

        if (!string.IsNullOrWhiteSpace(result)) {
            File.WriteAllBytes(result, data);
        }
    }

    public static Vector2 ClampImageSize(Vector2 orig, Vector2 max) {
        var ratio = orig.X / orig.Y;
        if (orig.X > max.X) return max with {Y = max.X / ratio};
        if (orig.Y > max.Y) return max with {X = max.Y * ratio};
        return orig;
    }
}
