namespace Hulla.Application.Services
{
    /// <summary>The phone's share sheet (GDD 10.5); where there is none, the text goes to the clipboard.</summary>
    public interface IShareSheet
    {
        /// <returns>True when the native sheet opened, false when the text was copied instead.</returns>
        bool Share(string title, string text);
    }
}
