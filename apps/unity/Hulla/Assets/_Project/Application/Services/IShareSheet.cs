namespace Hulla.Application.Services
{
    /// <summary>The phone's share sheet (GDD 10.5); where there is none, the text goes to the clipboard.</summary>
    public interface IShareSheet
    {
        /// <returns>True when the native sheet opened, false when the text was copied instead.</returns>
        bool Share(string title, string text);

        /// <summary>Shares a PNG with a caption (the share card, GDD 10.9).</summary>
        /// <returns>True when the native sheet opened, false when only the text was copied.</returns>
        bool ShareImage(string title, string text, byte[] png, string fileName);
    }
}
