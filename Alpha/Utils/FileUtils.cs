using NativeFileDialogSharp;

namespace Alpha.Utils;

public static class FileUtils {
    public static void Save(byte[] data, string extension) {
        var result = Dialog.FileSave(extension);

        if (result?.Path is not null) {
            var path = result.Path;
            if (!path.EndsWith("." + extension)) {
                path += "." + extension;
            }

            File.WriteAllBytes(path, data);
        }
    }
}
