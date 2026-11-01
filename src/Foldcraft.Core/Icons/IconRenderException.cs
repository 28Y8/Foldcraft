namespace Foldcraft.Core.Icons;

/// <summary>An icon could not be produced. The message is safe to show to the user.</summary>
public sealed class IconRenderException : Exception
{
    public IconRenderException(string message)
        : base(message)
    {
    }

    public IconRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
