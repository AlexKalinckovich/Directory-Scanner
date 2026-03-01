using Microsoft.WindowsAPICodePack.Dialogs;

namespace Directory_Scanner.UI.FileHelper;

public static class FileUtils
{
    public static string? PickFolder()
    {
        CommonOpenFileDialog dialog = new CommonOpenFileDialog();
        dialog.IsFolderPicker = true;
        dialog.InitialDirectory = "c:\\";
        CommonFileDialogResult result = dialog.ShowDialog();
        return result == CommonFileDialogResult.Ok ? dialog.FileName : null;
    }
}