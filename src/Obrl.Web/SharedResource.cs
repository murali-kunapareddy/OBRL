namespace Obrl.Web;

/// <summary>
/// Marker for OBRL's interface text: Resources/SharedResource.{language}.resx.
/// Keys are the English text, so English needs no file and a missing translation falls back to English.
/// Views use it through <c>L</c> (plain text) and <c>H</c> (text that contains HTML), see Views/_ViewImports.cshtml.
/// </summary>
public sealed class SharedResource;
