namespace Foldcraft.App.Services;

/// <summary>
/// Keeps the app's working folder on the app itself. Windows holds a process's working folder
/// open, so if that were a folder someone right-clicked, or the last one a file dialog visited,
/// it and every folder above it couldn't be renamed, moved or deleted until Foldcraft closed.
/// </summary>
public static class WorkingFolder
{
    public static void MoveToAppFolder() => Environment.CurrentDirectory = AppContext.BaseDirectory;
}
