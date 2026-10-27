namespace Hulla.Application.Settings
{
    /// <summary>
    /// The look of the app and the table (docs/design/table-themes.md): the scene behind every
    /// screen, the table, seat frames, card backs, buttons and the ambient motion. Cosmetic only;
    /// every game draws its own table parts over the theme's scene.
    /// </summary>
    public enum TableSkin
    {
        Club = 0,
        Haveli = 1,
        Dhaba = 2,
        Mehndi = 3,
        Adda = 4,
        Basant = 5,
        Pop = 6,
    }
}
