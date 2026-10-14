namespace Hulla.Composition
{
    /// <summary>
    /// The public half of the content signing key (ADR-0011), PEM. The private half signs the
    /// manifest at deploy time (`pnpm content:sign`) and never enters the repository. Empty means no
    /// key yet: a release build then keeps the bundled content, a development build also takes the
    /// unsigned manifest of a development box.
    /// </summary>
    public static class ContentKey
    {
        public const string Pem = "";
    }
}
